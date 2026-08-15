using Xunit;

namespace Epi.Lifecycle.Tests;

// FN-LCM-007 Require a signature to submit to a regulator, and none to record its decision
//   IT-018 A regulatory submission is refused unsigned; recording the regulator's decision
//          needs no signature
//
// CAP-LCM-012. Signing follows accountability rather than significance: submitting to a
// regulator is an act of this organisation by an accountable person, while recording what the
// regulator then decided is a factual entry about an event outside its control. Demanding a
// signature on the second would attach the weight of a Part 11 signature to a clerical act and
// prove nothing about the decision itself. Both are audited regardless.
public sealed class MarketSubmissionSignatureTests
{
    private static readonly VersionRef Version = new("doc-1", 1);

    /// <summary>
    /// When an approval takes effect. Stated on every one, because the engine refuses an
    /// approval that does not say - a missing date defaulted to now is a guess that reads as a
    /// fact (ADR-029 decision 3).
    /// </summary>
    private static readonly DateTimeOffset Effective =
        new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// What an approval is pinned against. Supplied on every approving transition because the
    /// engine refuses one without it - an approval that silently records no context looks
    /// exactly like one that recorded a context (ADR-024 decision 4).
    /// </summary>
    private static readonly ApprovalContext Approved = new(
        "sha-256:abc123",
        [new PinnedPackage("hl7.fhir.uv.emedicinal-product-info", "1.0.0", "c99767")],
        "https://epi.example.org/identifier/document");

    private static readonly IReadOnlySet<string> Markets =
        new HashSet<string>(["GB", "EU"], StringComparer.Ordinal);

    private sealed class Signatures(SignatureCheckResult? answer = null) : ISignatureCheck
    {
        private readonly SignatureCheckResult _answer = answer ?? SignatureCheckResult.Valid;

        public List<(string Reference, string Actor, string Meaning)> Asked { get; } = [];

        public Task<SignatureCheckResult> IsValidAsync(
            string reference, VersionRef version, string actor, string meaning,
            CancellationToken cancellationToken = default)
        {
            Asked.Add((reference, actor, meaning));
            return Task.FromResult(_answer);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EpiPlatform.sln")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }

    private static LifecycleModel Model(string file) => LifecycleModelConfiguration.LoadFrom(
        Path.Combine(RepositoryRoot(), "config", "lifecycle", file));

    private static (MarketApprovalService Service, InMemoryMarketApprovalStore Store, Signatures Signatures)
        Approvals(SignatureCheckResult? answer = null, ISpentSignatures? spent = null)
    {
        var store = new InMemoryMarketApprovalStore();
        var signatures = new Signatures(answer);
        return (
            new MarketApprovalService(
                Model("market-approval-states.json"), store, Markets, time: null,
                signatureCheck: signatures, spent: spent),
            store,
            signatures);
    }

    [Fact]
    public async Task IT_018_a_submission_without_a_signature_is_refused()
    {
        var (service, _, _) = Approvals();

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(Version, "GB", "submit", "user-rae"));

        Assert.Contains("signature", refused.Reason);
        Assert.Equal("not-submitted", await service.CurrentStateAsync(Version, "GB"));
    }

    [Fact]
    public async Task IT_018_recording_the_regulators_decision_needs_no_signature()
    {
        // The other half of CAP-LCM-012, and the half that would be easy to get wrong by
        // signing everything for safety.
        var (service, _, _) = Approvals();
        await service.TransitionAsync(Version, "GB", "submit", "user-rae", signatureReference: "sig-1");
        await service.TransitionAsync(Version, "GB", "begin-assessment", "user-rae");

        await service.TransitionAsync(
            Version, "GB", "record-approval", "user-rae", reason: "MHRA decision letter",
            effectiveFrom: Effective);

        Assert.Equal("approved", await service.CurrentStateAsync(Version, "GB"));
    }

    [Fact]
    public async Task FN_LCM_007_a_submission_signature_must_mean_responsibility()
    {
        // Not approval. The approval signature was given earlier, on the content; this one
        // says who is sending it to the regulator.
        var (service, _, signatures) = Approvals();

        await service.TransitionAsync(Version, "GB", "submit", "user-rae", signatureReference: "sig-1");

        var asked = Assert.Single(signatures.Asked);
        Assert.Equal("sig-1", asked.Reference);
        Assert.Equal("user-rae", asked.Actor);
        Assert.Equal("responsibility", asked.Meaning);
    }

    [Fact]
    public async Task FN_LCM_007_a_signature_the_check_rejects_refuses_the_submission()
    {
        var (service, _, _) = Approvals(
            answer: SignatureCheckResult.Invalid("it was made over a different version."));

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(Version, "GB", "submit", "user-rae", signatureReference: "sig-1"));

        Assert.Contains("different version", refused.Reason);
    }

    [Fact]
    public async Task FN_LCM_007_a_signature_is_recorded_against_the_submission()
    {
        var (service, store, _) = Approvals();

        await service.TransitionAsync(Version, "GB", "submit", "user-rae", signatureReference: "sig-1");

        var transition = Assert.Single(await store.HistoryAsync(new MarketVersion(Version, "GB")));
        Assert.Equal("sig-1", transition.SignatureReference);
    }

    [Fact]
    public async Task FN_LCM_007_an_unsigned_transition_records_no_signature_even_if_one_is_offered()
    {
        // Otherwise a reference on an unsigned gate would look like evidence of a control that
        // was never applied, and would be spent for no reason.
        var (service, store, _) = Approvals();
        await service.TransitionAsync(Version, "GB", "submit", "user-rae", signatureReference: "sig-1");

        await service.TransitionAsync(
            Version, "GB", "begin-assessment", "user-rae", signatureReference: "sig-2");

        var transitions = await store.HistoryAsync(new MarketVersion(Version, "GB"));
        Assert.Null(transitions[1].SignatureReference);
        Assert.False(await store.IsSignatureUsedAsync("sig-2"));
    }

    [Fact]
    public async Task FN_LCM_007_a_submission_signature_cannot_be_reused_in_another_market()
    {
        // One signature, one act. Submitting the same version to a second regulator is a
        // second act and needs its own.
        var (service, _, _) = Approvals();
        await service.TransitionAsync(Version, "GB", "submit", "user-rae", signatureReference: "sig-1");

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(Version, "EU", "submit", "user-rae", signatureReference: "sig-1"));

        Assert.Contains("already", refused.Reason);
    }

    [Fact]
    public async Task FN_LCM_007_a_signature_spent_on_an_internal_approval_cannot_carry_a_submission()
    {
        // The reason single use is a shared ledger rather than a method on one store. Neither
        // store can see the other's records, so asking only one would let a signature be spent
        // twice as long as it was spent in two different places.
        var lifecycle = new InMemoryLifecycleStore();
        var internalState = new LifecycleService(
            Model("label-states.json"), lifecycle, signatureCheck: new Signatures());
        await internalState.RegisterAsync(Version, "user-anna");
        await internalState.TransitionAsync(Version, "submit", "user-anna");
        await internalState.TransitionAsync(Version, "approve", "user-ben", signatureReference: "sig-1", approvalContext: Approved);

        var market = new InMemoryMarketApprovalStore();
        var service = new MarketApprovalService(
            Model("market-approval-states.json"), market, Markets, time: null,
            signatureCheck: new Signatures(), spent: new SpentSignatures(lifecycle, market));

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(Version, "GB", "submit", "user-rae", signatureReference: "sig-1"));

        Assert.Contains("already", refused.Reason);
    }

    [Fact]
    public async Task FN_LCM_007_a_signature_spent_on_a_submission_cannot_carry_an_internal_approval()
    {
        // And the same in the other direction, because a ledger consulted from one side only
        // would be half a control.
        var market = new InMemoryMarketApprovalStore();
        var approvals = new MarketApprovalService(
            Model("market-approval-states.json"), market, Markets, time: null,
            signatureCheck: new Signatures());
        await approvals.TransitionAsync(Version, "GB", "submit", "user-rae", signatureReference: "sig-1");

        var lifecycle = new InMemoryLifecycleStore();
        var internalState = new LifecycleService(
            Model("label-states.json"), lifecycle, signatureCheck: new Signatures(),
            spent: new SpentSignatures(lifecycle, market));
        await internalState.RegisterAsync(Version, "user-anna");
        await internalState.TransitionAsync(Version, "submit", "user-anna");

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => internalState.TransitionAsync(Version, "approve", "user-ben", signatureReference: "sig-1", approvalContext: Approved));

        Assert.Contains("already", refused.Reason);
    }

    [Fact]
    public void FN_LCM_007_a_signed_market_model_refuses_to_start_without_a_signature_check()
    {
        var error = Assert.Throws<ArgumentException>(() => new MarketApprovalService(
            Model("market-approval-states.json"), new InMemoryMarketApprovalStore(), Markets));

        Assert.Contains("signature", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FN_LCM_007_the_shipped_market_model_signs_submission_and_not_the_decision()
    {
        // The configuration this repository ships is the demonstration's control. If it ever
        // stopped signing submissions, or started signing decisions, that is a change someone
        // should have to justify rather than one that slips through.
        var model = Model("market-approval-states.json");

        foreach (var signed in new[] { "submit", "resubmit" })
        {
            var transition = model.Transitions.Single(t => t.Action == signed);
            Assert.True(transition.RequiresSignature);
            Assert.Equal("responsibility", transition.SignatureMeaning);
        }

        foreach (var unsigned in new[] { "begin-assessment", "record-approval", "record-rejection" })
        {
            Assert.False(model.Transitions.Single(t => t.Action == unsigned).RequiresSignature);
        }
    }
}
