namespace Epi.Lifecycle;

/// <summary>
/// Applies a market's regulatory-approval state model to a version, independently of the
/// version's internal lifecycle state (FN-LCM-004, CAP-LCM-003, ADR-005).
/// </summary>
public sealed class MarketApprovalService(
    LifecycleModel model,
    IMarketApprovalStore store,
    IReadOnlySet<string> markets,
    TimeProvider? time = null)
{
    private readonly LifecycleModel _model = model ?? throw new ArgumentNullException(nameof(model));
    private readonly IMarketApprovalStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly IReadOnlySet<string> _markets = markets ?? throw new ArgumentNullException(nameof(markets));
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>The state this version holds in this market.</summary>
    public Task<string> CurrentStateAsync(
        VersionRef version, string market, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    /// <summary>The state this version holds in every known market.</summary>
    public Task<IReadOnlyDictionary<string, string>> StatesAsync(
        VersionRef version, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    /// <summary>Moves a version's state in one market, or explains why it may not move.</summary>
    public Task<MarketStateTransition> TransitionAsync(
        VersionRef version,
        string market,
        string action,
        string actor,
        string? reason = null,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();
}
