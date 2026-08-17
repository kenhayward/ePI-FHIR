using Epi.ContentCore;
using Epi.Lifecycle;
using Xunit;

namespace Epi.Search.Tests;

// FN-SCH-002 Retrieve the current-approved version for a market
//   CAP-SCH-002 Retrieve a specific version and the current-approved version per market
//   CAP-SCH-004 Never leak out-of-scope content
public sealed class CurrentApprovedVersionsTests
{
    private static readonly DateTimeOffset Projected =
        new DateTimeOffset(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

    private const string Approved = "approved";

    private static async Task<(CurrentApprovedVersions Resolver, InMemoryMarketApprovalStore Approvals)>
        BuildAsync(params (int Version, DocumentScope Scope)[] versions)
    {
        var index = new InMemorySearchIndex();
        foreach (var (version, scope) in versions)
        {
            await index.ProjectAsync(SearchFixtures.Document("doc-1", version, scope), "approved", Projected);
        }

        var approvals = new InMemoryMarketApprovalStore();
        return (new CurrentApprovedVersions(index, approvals, Approved), approvals);
    }

    private static Task ApproveAsync(
        InMemoryMarketApprovalStore approvals, int version, string market, string state) =>
        approvals.AppendAsync(new MarketStateTransition(
            new MarketVersion(new VersionRef("doc-1", version), market),
            "under-assessment", state, "record-approval", "user-rae",
            new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero)));

    [Fact]
    public async Task FN_SCH_002_the_approved_version_in_a_market_is_the_one_that_market_approved()
    {
        var (resolver, approvals) = await BuildAsync((1, SearchFixtures.Uk), (2, SearchFixtures.Uk));
        await ApproveAsync(approvals, 1, "GB", Approved);

        var hit = await resolver.ForAsync("doc-1", "GB", [SearchFixtures.Uk]);

        Assert.Equal(1, hit!.Version);
    }

    [Fact]
    public async Task CAP_SCH_002_a_version_approved_in_one_market_is_not_approved_in_another()
    {
        // The normal case, not an edge one (ADR-005). Answering from internal lifecycle state,
        // or from a field on the content, cannot express it at all.
        var (resolver, approvals) = await BuildAsync((1, SearchFixtures.Uk), (2, SearchFixtures.Uk));
        await ApproveAsync(approvals, 1, "GB", Approved);
        await ApproveAsync(approvals, 2, "EU", Approved);

        Assert.Equal(1, (await resolver.ForAsync("doc-1", "GB", [SearchFixtures.Uk]))!.Version);
        Assert.Equal(2, (await resolver.ForAsync("doc-1", "EU", [SearchFixtures.Uk]))!.Version);
    }

    [Fact]
    public async Task FN_SCH_002_the_latest_approved_version_wins_when_a_market_has_approved_several()
    {
        var (resolver, approvals) = await BuildAsync((1, SearchFixtures.Uk), (2, SearchFixtures.Uk));
        await ApproveAsync(approvals, 1, "GB", Approved);
        await ApproveAsync(approvals, 2, "GB", Approved);

        Assert.Equal(2, (await resolver.ForAsync("doc-1", "GB", [SearchFixtures.Uk]))!.Version);
    }

    [Fact]
    public async Task FN_SCH_002_a_market_that_has_approved_nothing_has_no_current_approved_version()
    {
        // Null rather than the latest version. Falling back to "the newest one we have" would
        // answer a regulatory question with an internal one, and it would look right.
        var (resolver, _) = await BuildAsync((1, SearchFixtures.Uk), (2, SearchFixtures.Uk));

        Assert.Null(await resolver.ForAsync("doc-1", "GB", [SearchFixtures.Uk]));
    }

    [Fact]
    public async Task FN_SCH_002_a_withdrawn_approval_is_no_longer_the_current_approved_version()
    {
        // Resolved from the state the version holds now, not from the fact that it was once
        // approved. State is read from the store that owns it (ADR-022 decision 8).
        var (resolver, approvals) = await BuildAsync((1, SearchFixtures.Uk));
        await ApproveAsync(approvals, 1, "GB", Approved);
        await approvals.AppendAsync(new MarketStateTransition(
            new MarketVersion(new VersionRef("doc-1", 1), "GB"),
            Approved, "withdrawn", "withdraw-approval", "user-rae",
            new DateTimeOffset(2026, 8, 14, 11, 0, 0, TimeSpan.Zero)));

        Assert.Null(await resolver.ForAsync("doc-1", "GB", [SearchFixtures.Uk]));
    }

    [Fact]
    public async Task CAP_SCH_004_a_caller_outside_the_scope_is_told_nothing_rather_than_refused()
    {
        // Indistinguishable from "no market approval", so this endpoint cannot be used to
        // discover that a document exists.
        var (resolver, approvals) = await BuildAsync((1, SearchFixtures.Eu));
        await ApproveAsync(approvals, 1, "EU", Approved);

        Assert.Null(await resolver.ForAsync("doc-1", "EU", [SearchFixtures.Uk]));
        Assert.Null(await resolver.ForAsync("doc-1", "EU", []));
    }
}
