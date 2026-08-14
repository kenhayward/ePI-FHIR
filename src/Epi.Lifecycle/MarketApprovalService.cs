namespace Epi.Lifecycle;

/// <summary>
/// Applies a market's regulatory-approval state model to a version, independently of the
/// version's internal lifecycle state (FN-LCM-004, CAP-LCM-003, ADR-005).
/// </summary>
/// <remarks>
/// Deliberately a separate service over a separate store from <see cref="LifecycleService"/>.
/// Internal state says what the organisation has done with a version; this says what a regulator
/// has decided about it in one market. One record could not express "approved in Great Britain,
/// under assessment in the European Union" on the same content, which is the normal case in this
/// domain rather than an edge one.
/// </remarks>
public sealed class MarketApprovalService
{
    private readonly LifecycleModel _model;
    private readonly IMarketApprovalStore _store;
    private readonly IReadOnlySet<string> _markets;
    private readonly TimeProvider _time;

    public MarketApprovalService(
        LifecycleModel model,
        IMarketApprovalStore store,
        IReadOnlySet<string> markets,
        TimeProvider? time = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(markets);

        // A regulatory-approval transition records what a regulator decided, not an assertion by
        // the person entering it, so nothing here checks a signature. Refusing a model that asks
        // for one is the honest response: configuration that is silently ignored reads as a
        // control while being none. If these transitions should be signed, that is a deliberate
        // change here rather than a field someone sets hopefully.
        var gated = model.Transitions.FirstOrDefault(t => t.RequiresSignature || t.SegregatedFromAuthor);
        if (gated is not null)
        {
            throw new ArgumentException(
                $"Transition '{gated.Action}' asks for a signature or segregation of duties, and "
                + "the market approval service checks neither. Regulatory-approval state records a "
                + "regulator's decision rather than an assertion by the person recording it.",
                nameof(model));
        }

        if (markets.Count == 0)
        {
            throw new ArgumentException(
                "No markets are configured, so no regulatory-approval state could ever be "
                + "recorded against one.",
                nameof(markets));
        }

        _model = model;
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _markets = markets;
        _time = time ?? TimeProvider.System;
    }

    /// <summary>The state this version holds in this market.</summary>
    /// <remarks>
    /// A version with no history in a market is at the model's initial state. Nothing is written
    /// to say a version has not been submitted somewhere, so onboarding a market does not mean
    /// backfilling a row for every version that already exists.
    /// </remarks>
    public async Task<string> CurrentStateAsync(
        VersionRef version, string market, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(market);

        return await _store.CurrentStateAsync(new MarketVersion(version, market), cancellationToken)
               ?? _model.Initial;
    }

    /// <summary>The state this version holds in every known market.</summary>
    /// <remarks>
    /// Every market, not only those the version has moved in: a market missing from the answer
    /// would be indistinguishable from one nobody had looked at.
    /// </remarks>
    public async Task<IReadOnlyDictionary<string, string>> StatesAsync(
        VersionRef version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);

        var recorded = await _store.StatesForAsync(version, cancellationToken);
        return _markets.ToDictionary(
            market => market,
            market => recorded.GetValueOrDefault(market, _model.Initial),
            StringComparer.Ordinal);
    }

    /// <summary>Moves a version's state in one market, or explains why it may not move.</summary>
    public async Task<MarketStateTransition> TransitionAsync(
        VersionRef version,
        string market,
        string action,
        string actor,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(market);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        // Markets are configuration (capability 21). State recorded against a code nobody
        // configured would be state no report could ever explain.
        if (!_markets.Contains(market))
        {
            throw new TransitionRefusedException(version, action,
                $"'{market}' is not a market this platform is configured for.");
        }

        var subject = new MarketVersion(version, market);
        var current = await _store.CurrentStateAsync(subject, cancellationToken) ?? _model.Initial;

        var permitted = _model.Find(current, action)
            ?? throw new TransitionRefusedException(version, action,
                $"the market approval model permits no {action} from {current} in {market}.");

        var transition = new MarketStateTransition(
            subject, current, permitted.To, action, actor, _time.GetUtcNow(), reason);

        await _store.AppendAsync(transition, cancellationToken);
        return transition;
    }
}
