using Xunit;

namespace Epi.Lifecycle.Tests;

// FN-LCM-004 Hold per-market approval state separately from internal state
//   IT-013 A version approved in one market is not approved in another, on the same content
//
// The separation ADR-005 exists to preserve. A version can be approved by one regulator and
// under assessment by another on identical content, and a model that cannot say so cannot
// describe the normal case in this domain, let alone an edge one.
public sealed class MarketApprovalServiceTests
{
    private static readonly VersionRef Version = new("doc-1", 1);

    private static readonly IReadOnlySet<string> Markets =
        new HashSet<string>(["GB", "EU"], StringComparer.Ordinal);

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EpiPlatform.sln")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }

    private static LifecycleModel Model(string file = "market-approval-states.json") =>
        LifecycleModelConfiguration.LoadFrom(
            Path.Combine(RepositoryRoot(), "config", "lifecycle", file));

    private static (MarketApprovalService Service, InMemoryMarketApprovalStore Store) Approvals()
    {
        var store = new InMemoryMarketApprovalStore();
        return (
            new MarketApprovalService(
                Model(), store, Markets, time: null, signatureCheck: new AlwaysValid()),
            store);
    }

    /// <summary>
    /// Takes a version all the way to approved in one market. Submission is signed and the
    /// decision is not (CAP-LCM-012); the signature reference differs per market because one
    /// signature covers one act.
    /// </summary>
    private static async Task ApproveInAsync(MarketApprovalService service, string market)
    {
        await service.TransitionAsync(
            Version, market, "submit", "user-rae", signatureReference: $"sig-{market}");
        await service.TransitionAsync(Version, market, "begin-assessment", "user-rae");
        await service.TransitionAsync(Version, market, "record-approval", "user-rae");
    }

    [Fact]
    public async Task FN_LCM_004_a_version_is_unsubmitted_in_every_market_until_it_moves()
    {
        // Nothing is written to say a version has not been submitted anywhere. The initial
        // state is the answer when there is no history, so onboarding a market does not mean
        // backfilling a row for every version that already exists.
        var (service, store) = Approvals();

        Assert.Equal("not-submitted", await service.CurrentStateAsync(Version, "GB"));
        Assert.Equal("not-submitted", await service.CurrentStateAsync(Version, "EU"));
        Assert.Empty(await store.HistoryAsync(new MarketVersion(Version, "GB")));
    }

    [Fact]
    public async Task IT_013_a_version_approved_in_one_market_is_not_approved_in_another()
    {
        // Acceptance criterion 4, on identical content.
        var (service, _) = Approvals();

        await ApproveInAsync(service, "GB");
        await service.TransitionAsync(Version, "EU", "submit", "user-rae", signatureReference: "sig-EU");

        Assert.Equal("approved", await service.CurrentStateAsync(Version, "GB"));
        Assert.Equal("submitted", await service.CurrentStateAsync(Version, "EU"));
    }

    [Fact]
    public async Task IT_013_a_rejection_in_one_market_leaves_the_other_untouched()
    {
        var (service, _) = Approvals();

        await ApproveInAsync(service, "GB");
        await service.TransitionAsync(Version, "EU", "submit", "user-rae", signatureReference: "sig-EU");
        await service.TransitionAsync(Version, "EU", "begin-assessment", "user-rae");
        await service.TransitionAsync(Version, "EU", "record-rejection", "user-rae", reason: "wording query");

        Assert.Equal("approved", await service.CurrentStateAsync(Version, "GB"));
        Assert.Equal("rejected", await service.CurrentStateAsync(Version, "EU"));
    }

    [Fact]
    public async Task FN_LCM_004_market_state_does_not_disturb_internal_state()
    {
        // The point of two records rather than one (ADR-019 decision 2). A regulator's decision
        // is not the organisation's, and neither writes over the other.
        var lifecycle = new InMemoryLifecycleStore();
        var internalState = new LifecycleService(
            Model("label-states.json"), lifecycle, signatureCheck: new AlwaysValid());
        await internalState.RegisterAsync(Version, "user-anna");
        await internalState.TransitionAsync(Version, "submit", "user-anna");

        var (markets, _) = Approvals();
        await ApproveInAsync(markets, "GB");

        Assert.Equal("approved", await markets.CurrentStateAsync(Version, "GB"));
        Assert.Equal("in-review", await lifecycle.CurrentStateAsync(Version));
    }

    [Fact]
    public async Task FN_LCM_004_states_are_reported_for_every_known_market()
    {
        // "Where does this version stand" must be answerable in one call, including for the
        // markets it has never been submitted in - otherwise a market missing from the answer
        // is indistinguishable from one nobody has looked at.
        var (service, _) = Approvals();
        await ApproveInAsync(service, "GB");

        var states = await service.StatesAsync(Version);

        Assert.Equal(2, states.Count);
        Assert.Equal("approved", states["GB"]);
        Assert.Equal("not-submitted", states["EU"]);
    }

    [Fact]
    public async Task FN_LCM_004_a_market_the_platform_does_not_know_is_refused()
    {
        // Markets are configuration (capability 21). Recording an approval against a market
        // code nobody configured would produce state no report could ever explain.
        var (service, _) = Approvals();

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(Version, "ZZ", "submit", "user-rae"));

        Assert.Contains("ZZ", refused.Reason);
    }

    [Fact]
    public async Task FN_LCM_004_a_transition_the_market_model_does_not_permit_is_refused()
    {
        var (service, _) = Approvals();

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(Version, "GB", "record-approval", "user-rae"));

        Assert.Contains("permits no", refused.Reason);
        Assert.Equal("not-submitted", await service.CurrentStateAsync(Version, "GB"));
    }

    [Fact]
    public async Task FN_LCM_004_history_is_per_market_and_in_order()
    {
        var (service, store) = Approvals();
        await ApproveInAsync(service, "GB");
        await service.TransitionAsync(Version, "EU", "submit", "user-rae", signatureReference: "sig-EU");

        var gb = await store.HistoryAsync(new MarketVersion(Version, "GB"));
        var eu = await store.HistoryAsync(new MarketVersion(Version, "EU"));

        Assert.Equal(["submit", "begin-assessment", "record-approval"], gb.Select(t => t.Action));
        Assert.Equal(["submit"], eu.Select(t => t.Action));
        Assert.DoesNotContain(typeof(IMarketApprovalStore).GetMethods(),
            m => m.Name.Contains("Update", StringComparison.Ordinal)
                 || m.Name.Contains("Delete", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FN_LCM_004_a_transition_records_actor_time_and_reason()
    {
        var (service, _) = Approvals();
        await service.TransitionAsync(Version, "GB", "submit", "user-rae", signatureReference: "sig-GB");
        await service.TransitionAsync(Version, "GB", "begin-assessment", "user-rae");

        var transition = await service.TransitionAsync(
            Version, "GB", "record-approval", "user-rae", reason: "MHRA decision letter 2026-08-14");

        Assert.Equal("under-assessment", transition.From);
        Assert.Equal("approved", transition.To);
        Assert.Equal("user-rae", transition.Actor);
        Assert.Equal("MHRA decision letter 2026-08-14", transition.Reason);
        Assert.NotEqual(default, transition.At);
    }

    [Fact]
    public void FN_LCM_004_a_market_model_may_not_gate_on_segregation_nothing_here_checks()
    {
        // Signature gating is now supported here (CAP-LCM-012), but segregation of duties is
        // not: this service does not know who authored a version. Configuration that is
        // silently ignored reads as a control while being none, so it is refused rather than
        // accepted and forgotten.
        var segregated = new LifecycleModel(
            "market-approval", "not-submitted", ["not-submitted", "approved"],
            [new LifecycleTransition("not-submitted", "approved", "record-approval",
                SegregatedFromAuthor: true)]);

        var error = Assert.Throws<ArgumentException>(
            () => new MarketApprovalService(segregated, new InMemoryMarketApprovalStore(), Markets));

        Assert.Contains("segregation", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FN_LCM_004_a_service_with_no_markets_configured_refuses_to_start()
    {
        var error = Assert.Throws<ArgumentException>(() => new MarketApprovalService(
            Model(), new InMemoryMarketApprovalStore(), new HashSet<string>(),
            time: null, signatureCheck: new AlwaysValid()));

        Assert.Contains("market", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FN_LCM_004_the_shipped_market_model_loads_and_is_not_the_internal_one()
    {
        // Two models, deliberately. If these ever became the same file the separation ADR-005
        // requires would be gone while everything still appeared to work.
        var market = Model();
        var internalModel = Model("label-states.json");

        Assert.Equal("not-submitted", market.Initial);
        Assert.Equal("draft", internalModel.Initial);
        Assert.Contains("under-assessment", market.States);
        Assert.DoesNotContain("under-assessment", internalModel.States);
    }

    private sealed class AlwaysValid : ISignatureCheck
    {
        public Task<SignatureCheckResult> IsValidAsync(
            string reference, VersionRef version, string actor, string meaning,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SignatureCheckResult.Valid);
    }
}
