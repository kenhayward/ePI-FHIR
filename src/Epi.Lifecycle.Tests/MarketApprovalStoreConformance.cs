using Xunit;

namespace Epi.Lifecycle.Tests;

/// <summary>
/// The behaviour every per-market approval store must exhibit, whatever backs it (FN-LCM-004).
/// </summary>
public abstract class MarketApprovalStoreConformance
{
    private static readonly VersionRef Version = new("doc-1", 1);

    /// <summary>A store ready to use, with its schema in place if it needs one.</summary>
    protected abstract Task<IMarketApprovalStore> CreateStoreAsync();

    private static MarketStateTransition Transition(
        string market, string from, string to, string action,
        string actor = "user-rae", string? reason = null, string? signature = null,
        VersionRef? version = null) =>
        new(new MarketVersion(version ?? Version, market), from, to, action, actor,
            new DateTimeOffset(2026, 8, 14, 11, 0, 0, TimeSpan.Zero), reason, signature);

    [Fact]
    public async Task FN_LCM_004_a_version_that_has_never_moved_in_a_market_has_no_recorded_state()
    {
        // Null rather than the initial state. Nothing is written to say a version has not been
        // submitted anywhere, so onboarding a market does not mean backfilling a row for every
        // version that already exists - the service supplies the initial state instead.
        var store = await CreateStoreAsync();

        Assert.Null(await store.CurrentStateAsync(new MarketVersion(Version, "GB")));
        Assert.Empty(await store.HistoryAsync(new MarketVersion(Version, "GB")));
        Assert.Empty(await store.StatesForAsync(Version));
    }

    [Fact]
    public async Task FN_LCM_004_state_is_recorded_per_market_on_the_same_version()
    {
        // The separation ADR-005 exists to preserve, asserted at the store rather than only at
        // the service: a store that keyed on the version alone would pass every service test
        // that touched one market.
        var store = await CreateStoreAsync();

        await store.AppendAsync(Transition("GB", "not-submitted", "submitted", "submit", signature: "sig-GB"));
        await store.AppendAsync(Transition("GB", "submitted", "approved", "record-approval"));
        await store.AppendAsync(Transition("EU", "not-submitted", "submitted", "submit", signature: "sig-EU"));

        Assert.Equal("approved", await store.CurrentStateAsync(new MarketVersion(Version, "GB")));
        Assert.Equal("submitted", await store.CurrentStateAsync(new MarketVersion(Version, "EU")));
    }

    [Fact]
    public async Task FN_LCM_004_states_for_a_version_report_every_market_it_has_moved_in()
    {
        var store = await CreateStoreAsync();
        await store.AppendAsync(Transition("GB", "not-submitted", "submitted", "submit", signature: "sig-GB"));
        await store.AppendAsync(Transition("GB", "submitted", "approved", "record-approval"));
        await store.AppendAsync(Transition("EU", "not-submitted", "submitted", "submit", signature: "sig-EU"));

        var states = await store.StatesForAsync(Version);

        Assert.Equal(2, states.Count);
        Assert.Equal("approved", states["GB"]);
        Assert.Equal("submitted", states["EU"]);
    }

    [Fact]
    public async Task FN_LCM_004_states_for_a_version_do_not_leak_from_another_version()
    {
        var store = await CreateStoreAsync();
        var second = new VersionRef("doc-1", 2);

        await store.AppendAsync(Transition("GB", "not-submitted", "submitted", "submit", signature: "sig-1"));
        await store.AppendAsync(
            Transition("EU", "not-submitted", "submitted", "submit", signature: "sig-2", version: second));

        Assert.Equal(["GB"], (await store.StatesForAsync(Version)).Keys);
        Assert.Equal(["EU"], (await store.StatesForAsync(second)).Keys);
    }

    [Fact]
    public async Task FN_LCM_004_history_comes_back_oldest_first_with_every_field_intact()
    {
        var store = await CreateStoreAsync();

        await store.AppendAsync(Transition("GB", "not-submitted", "submitted", "submit", signature: "sig-GB"));
        await store.AppendAsync(Transition(
            "GB", "submitted", "approved", "record-approval", "user-rae", "MHRA decision letter"));

        var history = await store.HistoryAsync(new MarketVersion(Version, "GB"));

        Assert.Equal(["submit", "record-approval"], history.Select(t => t.Action));

        var decision = history[1];
        Assert.Equal(new MarketVersion(Version, "GB"), decision.Subject);
        Assert.Equal("submitted", decision.From);
        Assert.Equal("approved", decision.To);
        Assert.Equal("user-rae", decision.Actor);
        Assert.Equal("MHRA decision letter", decision.Reason);

        // CAP-LCM-012: the decision is not signed, and the record says so by carrying nothing.
        Assert.Null(decision.SignatureReference);
        Assert.Equal("sig-GB", history[0].SignatureReference);
        Assert.Equal(new DateTimeOffset(2026, 8, 14, 11, 0, 0, TimeSpan.Zero), decision.At);
    }

    [Fact]
    public async Task FN_WFL_003_a_signature_is_spent_once_it_has_been_cited()
    {
        var store = await CreateStoreAsync();

        Assert.False(await store.IsSignatureUsedAsync("sig-GB"));

        await store.AppendAsync(Transition("GB", "not-submitted", "submitted", "submit", signature: "sig-GB"));

        Assert.True(await store.IsSignatureUsedAsync("sig-GB"));
        Assert.False(await store.IsSignatureUsedAsync("sig-EU"));
    }
}
