using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Epi.Lifecycle.Tests;

// FN-LCM-003 Supersession as a consequence of approval (ADR-030).
//   CAP-LCM-005 A new approved version supersedes a prior; withdrawal
//   CAP-LCM-006 The superseded version remains retrievable and reconstructable
public sealed class SupersessionTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly ApprovalContext Approved = new(
        "sha-256:abc123",
        [new PinnedPackage("hl7.fhir.uv.emedicinal-product-info", "1.0.0", "c99767")],
        "https://epi.example.org/identifier/document");

    private sealed class AnySignature : ISignatureCheck
    {
        public Task<SignatureCheckResult> IsValidAsync(
            string reference, VersionRef version, string actor, string meaning,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SignatureCheckResult.Valid);
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

    private static (LifecycleService Service, InMemoryLifecycleStore Store) Build()
    {
        var store = new InMemoryLifecycleStore();
        var model = LifecycleModelConfiguration.LoadFrom(
            Path.Combine(RepositoryRoot(), "config", "lifecycle", "label-states.json"));
        return (new LifecycleService(
            model, store, new FakeTimeProvider(Noon), new AnySignature()), store);
    }

    /// <summary>Registers a version and walks it to approved by someone other than its author.</summary>
    private static async Task ApproveAsync(LifecycleService service, int version)
    {
        var reference = new VersionRef("doc-1", version);
        await service.RegisterAsync(reference, "user-anna");
        await service.TransitionAsync(reference, "submit", "user-anna");
        await service.TransitionAsync(
            reference, "approve", "user-ben", signatureReference: $"sig-{version}",
            approvalContext: Approved);
    }

    [Fact]
    public async Task CAP_LCM_005_approving_a_version_supersedes_the_one_it_displaces()
    {
        var (service, store) = Build();
        await ApproveAsync(service, 1);

        await ApproveAsync(service, 2);

        Assert.Equal("superseded", await store.CurrentStateAsync(new VersionRef("doc-1", 1)));
        Assert.Equal("approved", await store.CurrentStateAsync(new VersionRef("doc-1", 2)));
    }

    [Fact]
    public async Task CAP_LCM_005_the_supersession_records_who_caused_it_and_when_and_why()
    {
        // Recorded rather than inferred: there is a moment and a person, so the history says
        // so (ADR-030 decision 2).
        var (service, store) = Build();
        await ApproveAsync(service, 1);
        await ApproveAsync(service, 2);

        var supersession = (await store.HistoryAsync(new VersionRef("doc-1", 1)))[^1];

        Assert.Equal("supersede", supersession.Action);
        Assert.Equal("user-ben", supersession.Actor);
        Assert.Equal(Noon, supersession.At);
        Assert.Contains("version 2", supersession.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CAP_LCM_006_the_superseded_version_keeps_its_history_and_its_pinned_context()
    {
        // Acceptance criterion 5. A superseded version is still the version that was in force
        // between two dates, and an inspection asks about exactly that.
        var (service, store) = Build();
        await ApproveAsync(service, 1);
        await ApproveAsync(service, 2);

        var first = new VersionRef("doc-1", 1);
        Assert.Equal(["submit", "approve", "supersede"],
            (await store.HistoryAsync(first)).Select(t => t.Action));
        Assert.Equal("user-anna", await store.AuthorOfAsync(first));

        var pinned = await store.ForAsync(first);
        Assert.NotNull(pinned);
        Assert.Equal("sha-256:abc123", pinned!.ContentHash);
    }

    [Fact]
    public async Task CAP_LCM_005_a_first_approval_supersedes_nothing()
    {
        var (service, store) = Build();

        await ApproveAsync(service, 1);

        Assert.Single(await store.HistoryAsync(new VersionRef("doc-1", 1)), t => t.Action == "approve");
        Assert.DoesNotContain(
            await store.HistoryAsync(new VersionRef("doc-1", 1)), t => t.Action == "supersede");
    }

    [Fact]
    public async Task CAP_LCM_005_a_version_that_was_never_approved_is_not_superseded()
    {
        // A draft was not the version in force to begin with, so nothing is written for it and
        // the history says only what happened (ADR-030 decision 4).
        var (service, store) = Build();
        await service.RegisterAsync(new VersionRef("doc-1", 1), "user-anna");

        await ApproveAsync(service, 2);

        Assert.Equal("draft", await store.CurrentStateAsync(new VersionRef("doc-1", 1)));
        Assert.Empty(await store.HistoryAsync(new VersionRef("doc-1", 1)));
    }

    [Fact]
    public async Task CAP_LCM_005_an_already_superseded_version_is_not_superseded_again()
    {
        var (service, store) = Build();
        await ApproveAsync(service, 1);
        await ApproveAsync(service, 2);

        await ApproveAsync(service, 3);

        Assert.Single(
            await store.HistoryAsync(new VersionRef("doc-1", 1)), t => t.Action == "supersede");
        Assert.Equal("superseded", await store.CurrentStateAsync(new VersionRef("doc-1", 2)));
    }

    [Fact]
    public async Task CAP_LCM_005_supersession_does_not_reach_across_documents()
    {
        var (service, store) = Build();
        await ApproveAsync(service, 1);

        var other = new VersionRef("doc-2", 1);
        await service.RegisterAsync(other, "user-anna");
        await service.TransitionAsync(other, "submit", "user-anna");
        await service.TransitionAsync(
            other, "approve", "user-ben", signatureReference: "sig-other",
            approvalContext: Approved);

        Assert.Equal("approved", await store.CurrentStateAsync(new VersionRef("doc-1", 1)));
    }
}
