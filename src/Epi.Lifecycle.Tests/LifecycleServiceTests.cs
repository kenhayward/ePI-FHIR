using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Epi.Lifecycle.Tests;

// FN-LCM-002 Reject a transition the state model does not permit
// FN-LCM-003 Record a transition with actor, timestamp and reason
// FN-WFL-002 Refuse an approval by the author of the version, by any route
// FN-WFL-003 Require a valid, unused signature at a gate the model says must be signed
//   IT-010 An unpermitted transition is rejected; a permitted one records actor and timestamp
//   IT-011 The author of a version cannot approve it
public sealed class LifecycleServiceTests
{
    private static readonly VersionRef Version = new("doc-1", 1);

    /// <summary>
    /// What an approval is pinned against. Supplied on every approving transition because the
    /// engine refuses one without it - an approval that silently records no context looks
    /// exactly like one that recorded a context (ADR-024 decision 4).
    /// </summary>
    private static readonly ApprovalContext Approved = new(
        "sha-256:abc123",
        [new PinnedPackage("hl7.fhir.uv.emedicinal-product-info", "1.0.0", "c99767")],
        "https://epi.example.org/identifier/document");

    /// <summary>
    /// Stands in for the signature module. Records what it was asked, so a test can assert the
    /// gate demanded the meaning the model requires rather than merely demanding something.
    /// </summary>
    private sealed class Signatures(SignatureCheckResult? answer = null) : ISignatureCheck
    {
        private readonly SignatureCheckResult _answer = answer ?? SignatureCheckResult.Valid;

        public List<(string Reference, VersionRef Version, string Actor, string Meaning)> Asked { get; } = [];

        public Task<SignatureCheckResult> IsValidAsync(
            string reference, VersionRef version, string actor, string meaning,
            CancellationToken cancellationToken = default)
        {
            Asked.Add((reference, version, actor, meaning));
            return Task.FromResult(_answer);
        }
    }

    private static LifecycleModel Model() => LifecycleModelConfiguration.LoadFrom(
        Path.Combine(RepositoryRoot(), "config", "lifecycle", "label-states.json"));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EpiPlatform.sln")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }

    private static LifecycleService Service(
        ILifecycleStore store, ISignatureCheck? signatures = null, TimeProvider? time = null) =>
        new(Model(), store, time, signatures ?? new Signatures());

    private static async Task<(LifecycleService Service, InMemoryLifecycleStore Store, Signatures Signatures)>
        InReviewAsync(string author = "user-anna", SignatureCheckResult? answer = null)
    {
        var store = new InMemoryLifecycleStore();
        var signatures = new Signatures(answer);
        var service = Service(store, signatures);
        await service.RegisterAsync(Version, author);
        await service.TransitionAsync(Version, "submit", author);
        return (service, store, signatures);
    }

    [Fact]
    public async Task IT_010_a_permitted_transition_records_actor_and_time()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 13, 10, 0, 0, TimeSpan.Zero));
        var store = new InMemoryLifecycleStore();
        var service = Service(store, time: clock);
        await service.RegisterAsync(Version, "user-anna");

        var transition = await service.TransitionAsync(
            Version, "submit", "user-anna", reason: "ready for review");

        Assert.Equal("draft", transition.From);
        Assert.Equal("in-review", transition.To);
        Assert.Equal("user-anna", transition.Actor);
        Assert.Equal(clock.GetUtcNow(), transition.At);
        Assert.Equal("ready for review", transition.Reason);
        Assert.Equal("in-review", await store.CurrentStateAsync(Version));
    }

    [Fact]
    public async Task IT_010_a_transition_the_model_does_not_permit_is_refused()
    {
        var store = new InMemoryLifecycleStore();
        var service = Service(store);
        await service.RegisterAsync(Version, "user-anna");

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(Version, "approve", "user-ben", signatureReference: "sig-1", approvalContext: Approved));

        Assert.Contains("permits no", refused.Reason);
        Assert.Equal("draft", await store.CurrentStateAsync(Version));
    }

    [Fact]
    public async Task IT_010_a_refused_transition_leaves_no_history_behind()
    {
        var (service, store, _) = await InReviewAsync();

        await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(Version, "supersede", "user-ben"));

        Assert.Single(await store.HistoryAsync(Version));
    }

    [Fact]
    public async Task IT_011_the_author_of_a_version_may_not_approve_it()
    {
        var (service, _, _) = await InReviewAsync(author: "user-anna");

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(Version, "approve", "user-anna", signatureReference: "sig-1", approvalContext: Approved));

        Assert.Contains("may not approve", refused.Reason);
    }

    [Fact]
    public async Task IT_011_someone_other_than_the_author_may_approve_it()
    {
        var (service, store, _) = await InReviewAsync(author: "user-anna");

        await service.TransitionAsync(Version, "approve", "user-ben", signatureReference: "sig-1", approvalContext: Approved);

        Assert.Equal("approved", await store.CurrentStateAsync(Version));
    }

    [Fact]
    public async Task IT_011_an_unknown_author_refuses_approval_rather_than_allowing_it()
    {
        // If the platform cannot say who wrote a version it cannot assure segregation of
        // duties, and the safe answer is no. Treating unknown as "not the approver" would make
        // the control depend on data being present rather than on it being checked.
        var store = new InMemoryLifecycleStore();
        var service = Service(store);

        var orphan = new VersionRef("doc-3", 1);
        await store.AppendAsync(new StateTransition(
            orphan, "draft", "in-review", "submit", "someone", DateTimeOffset.UtcNow));

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(orphan, "approve", "user-ben", signatureReference: "sig-1", approvalContext: Approved));

        Assert.Contains("author", refused.Reason);
    }

    [Fact]
    public async Task FN_LCM_003_a_transition_the_model_says_must_be_signed_cannot_be_made_unsigned()
    {
        var (service, _, _) = await InReviewAsync();

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(Version, "approve", "user-ben", approvalContext: Approved));

        Assert.Contains("signature", refused.Reason);
    }

    [Fact]
    public async Task FN_WFL_003_a_signature_the_check_rejects_refuses_the_transition()
    {
        var (service, store, _) = await InReviewAsync(
            answer: SignatureCheckResult.Invalid("it was made over a different version."));

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(Version, "approve", "user-ben", signatureReference: "sig-1", approvalContext: Approved));

        Assert.Contains("different version", refused.Reason);
        Assert.Equal("in-review", await store.CurrentStateAsync(Version));
    }

    [Fact]
    public async Task FN_WFL_003_the_gate_demands_the_meaning_the_model_requires()
    {
        // Not merely that a signature exists. A signature captured as a review is not an
        // approval, and the model says which is needed where.
        var (service, _, signatures) = await InReviewAsync();

        await service.TransitionAsync(Version, "approve", "user-ben", signatureReference: "sig-1", approvalContext: Approved);

        var asked = Assert.Single(signatures.Asked);
        Assert.Equal("sig-1", asked.Reference);
        Assert.Equal(Version, asked.Version);
        Assert.Equal("user-ben", asked.Actor);
        Assert.Equal("approval", asked.Meaning);
    }

    [Fact]
    public async Task FN_WFL_003_a_signature_already_spent_cannot_be_used_again()
    {
        // Without this a single approval signature could be replayed against every later gate,
        // which would make the signature a token the holder can spend rather than an assertion
        // about one act.
        var (service, _, _) = await InReviewAsync();
        await service.TransitionAsync(Version, "approve", "user-ben", signatureReference: "sig-1", approvalContext: Approved);

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(Version, "withdraw", "user-ben", signatureReference: "sig-1"));

        Assert.Contains("already", refused.Reason);
    }

    [Fact]
    public async Task FN_WFL_003_a_signature_spent_on_one_document_cannot_be_used_on_another()
    {
        // The reference is unique across the platform, so re-use is refused wherever it is
        // attempted rather than only within the version that spent it.
        var (service, store, _) = await InReviewAsync();
        await service.TransitionAsync(Version, "approve", "user-ben", signatureReference: "sig-1", approvalContext: Approved);

        var other = new VersionRef("doc-2", 1);
        await service.RegisterAsync(other, "user-anna");
        await service.TransitionAsync(other, "submit", "user-anna");

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(other, "approve", "user-ben", signatureReference: "sig-1", approvalContext: Approved));

        Assert.Contains("already", refused.Reason);
        Assert.Equal("in-review", await store.CurrentStateAsync(other));
    }

    [Fact]
    public void FN_WFL_003_a_model_with_a_signed_gate_refuses_to_start_without_a_signature_check()
    {
        // The dangerous default would be to accept any non-empty string when no check is
        // configured, which is a gate that looks like a control and is not one. Failing at
        // composition is loud, early, and impossible to deploy past.
        var error = Assert.Throws<ArgumentException>(
            () => new LifecycleService(Model(), new InMemoryLifecycleStore()));

        Assert.Contains("signature", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FN_LCM_003_history_is_kept_in_order_and_the_store_offers_no_way_to_amend_it()
    {
        var (service, store, _) = await InReviewAsync();
        await service.TransitionAsync(Version, "approve", "user-ben", signatureReference: "sig-1", approvalContext: Approved);

        var history = await store.HistoryAsync(Version);

        Assert.Equal(["submit", "approve"], history.Select(t => t.Action));
        Assert.Equal(["draft", "in-review"], history.Select(t => t.From));
        Assert.DoesNotContain(typeof(ILifecycleStore).GetMethods(),
            m => m.Name.Contains("Update", StringComparison.Ordinal)
                 || m.Name.Contains("Delete", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FN_LCM_003_the_service_reports_the_state_and_author_it_is_holding()
    {
        // Reads go through the service for the same reason writes do: a caller reaching past
        // it to the store would be reading a different notion of state from the one enforced.
        var (service, _, _) = await InReviewAsync(author: "user-anna");

        Assert.Equal("in-review", await service.CurrentStateAsync(Version));
        Assert.Equal("user-anna", await service.AuthorOfAsync(Version));
    }

    [Fact]
    public async Task CAP_LCM_011_an_approval_with_nothing_to_pin_is_refused()
    {
        // An approval that silently records no context looks exactly like one that recorded a
        // context, until somebody asks - and by then the configuration has moved and the
        // answer cannot be reconstructed (ADR-024 decision 4).
        var (service, store, _) = await InReviewAsync();

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(Version, "approve", "user-ben", signatureReference: "sig-1"));

        Assert.Contains("approved against", refused.Reason, StringComparison.Ordinal);

        // And the transition did not happen, so the refusal is a gate rather than a warning.
        Assert.Equal("in-review", await store.CurrentStateAsync(Version));
    }

    [Fact]
    public async Task CAP_LCM_011_a_transition_that_is_not_an_approval_needs_nothing_to_pin()
    {
        // Most transitions pin nothing, and demanding a context for a submit would make the
        // control noise that callers learn to satisfy with anything to hand.
        var store = new InMemoryLifecycleStore();
        var service = Service(store);
        await service.RegisterAsync(Version, "user-anna");

        await service.TransitionAsync(Version, "submit", "user-anna");

        Assert.Equal("in-review", await store.CurrentStateAsync(Version));
        Assert.Null(await store.ForAsync(Version));
    }

    [Fact]
    public async Task CAP_LCM_011_an_approval_pins_the_context_it_was_given()
    {
        var (service, store, _) = await InReviewAsync();

        await service.TransitionAsync(
            Version, "approve", "user-ben", signatureReference: "sig-1", approvalContext: Approved);

        var pinned = await store.ForAsync(Version);
        Assert.NotNull(pinned);
        Assert.Equal("sha-256:abc123", pinned!.ContentHash);
        Assert.Equal("approved", pinned.State);

        // The model's own name and the transition's own timestamp, not the caller's: the
        // engine is the thing that knows both (ADR-024 decision 3).
        Assert.Equal("label", pinned.StateModel);
        Assert.Equal((await store.HistoryAsync(Version))[^1].At, pinned.PinnedAt);
    }

    [Fact]
    public async Task FN_LCM_003_a_version_nobody_registered_has_no_state_to_report()
    {
        // Null rather than the initial state. A version the platform has never seen is not a
        // draft, and reporting one would invent a document.
        var service = Service(new InMemoryLifecycleStore());

        Assert.Null(await service.CurrentStateAsync(new VersionRef("never-registered", 1)));
        Assert.Null(await service.AuthorOfAsync(new VersionRef("never-registered", 1)));
    }

    [Fact]
    public async Task FN_LCM_003_a_version_not_under_management_cannot_transition()
    {
        var service = Service(new InMemoryLifecycleStore());

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(new VersionRef("never-registered", 1), "submit", "user-anna"));

        Assert.Contains("not under lifecycle management", refused.Reason);
    }

    /// <summary>Registers a version and walks it to approved, one hour per step.</summary>
    private static async Task<LifecycleService> ApprovedAsync(FakeTimeProvider clock)
    {
        var service = Service(new InMemoryLifecycleStore(), time: clock);
        await service.RegisterAsync(Version, "user-anna");

        clock.Advance(TimeSpan.FromHours(1));
        await service.TransitionAsync(Version, "submit", "user-anna");

        clock.Advance(TimeSpan.FromHours(1));
        await service.TransitionAsync(Version, "approve", "user-ben", signatureReference: "sig-1", approvalContext: Approved);

        return service;
    }

    [Fact]
    public async Task FN_LCM_006_the_state_at_a_past_moment_is_derived_from_the_transitions()
    {
        // The question an inspection asks is "what was this on the third of March", not "what
        // is it now". An append-only history can answer it; a state column never could
        // (ADR-019 decision 4).
        var start = new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(start);
        var service = await ApprovedAsync(clock);

        Assert.Equal("draft", await service.StateAtAsync(Version, start.AddMinutes(30)));
        Assert.Equal("in-review", await service.StateAtAsync(Version, start.AddMinutes(90)));
        Assert.Equal("approved", await service.StateAtAsync(Version, start.AddHours(3)));
    }

    [Fact]
    public async Task FN_LCM_006_the_state_at_the_moment_of_a_transition_is_the_state_it_moved_to()
    {
        // The boundary, stated rather than left to whichever comparison was written. A
        // transition timestamped 10:00 means the version was in its new state at 10:00.
        var start = new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(start);
        var service = await ApprovedAsync(clock);

        Assert.Equal("in-review", await service.StateAtAsync(Version, start.AddHours(1)));
        Assert.Equal("approved", await service.StateAtAsync(Version, start.AddHours(2)));
    }

    [Fact]
    public async Task FN_LCM_006_before_a_version_existed_it_was_in_no_state_at_all()
    {
        // Null rather than the initial state. Reporting "draft" for a moment before the
        // version was registered would place a document in history that was not there.
        var start = new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(start);
        var service = await ApprovedAsync(clock);

        Assert.Null(await service.StateAtAsync(Version, start.AddDays(-1)));
        Assert.Null(await service.StateAtAsync(new VersionRef("never-registered", 1), start));
    }

    [Fact]
    public async Task FN_LCM_006_a_version_that_has_never_moved_holds_its_initial_state()
    {
        var start = new DateTimeOffset(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);
        var clock = new FakeTimeProvider(start);
        var service = Service(new InMemoryLifecycleStore(), time: clock);
        await service.RegisterAsync(Version, "user-anna");

        Assert.Equal("draft", await service.StateAtAsync(Version, start.AddYears(1)));
    }
}
