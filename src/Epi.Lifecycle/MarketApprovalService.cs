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

    private readonly ISignatureCheck _signatureCheck;
    private readonly ISpentSignatures _spent;

    public MarketApprovalService(
        LifecycleModel model,
        IMarketApprovalStore store,
        IReadOnlySet<string> markets,
        TimeProvider? time = null,
        ISignatureCheck? signatureCheck = null,
        ISpentSignatures? spent = null)
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(markets);

        // Segregation of duties is a control this service cannot enforce: it does not know who
        // authored a version, and a model asking for it would be configuration that reads as a
        // control while being none.
        var segregated = model.Transitions.FirstOrDefault(t => t.SegregatedFromAuthor);
        if (segregated is not null)
        {
            throw new ArgumentException(
                $"Transition '{segregated.Action}' asks for segregation of duties, which the market "
                + "approval service cannot enforce because it does not know who authored a version.",
                nameof(model));
        }

        // Same reasoning as the internal engine: a signed gate with nothing to check the
        // signature would accept any reference at all (CAP-LCM-012).
        if (model.Transitions.Any(t => t.RequiresSignature) && signatureCheck is null)
        {
            throw new ArgumentException(
                "This market approval model has transitions that require an electronic signature, "
                + "so a signature check must be supplied.",
                nameof(signatureCheck));
        }

        if (markets.Count == 0)
        {
            throw new ArgumentException(
                "No markets are configured, so no regulatory-approval state could ever be "
                + "recorded against one.",
                nameof(markets));
        }

        _model = model;
        _store = store;
        _markets = markets;
        _time = time ?? TimeProvider.System;
        _signatureCheck = signatureCheck ?? NoSignedGates.Instance;

        // Defaults to this store alone, which is right when it is the only one. Composition
        // passes a SpentSignatures over both stores so that a signature spent on an internal
        // approval cannot then carry a regulatory submission.
        _spent = spent ?? store;
    }

    /// <summary>Stands in where a model has no signed gates, so the field need not be nullable.</summary>
    private sealed class NoSignedGates : ISignatureCheck
    {
        public static NoSignedGates Instance { get; } = new();

        public Task<SignatureCheckResult> IsValidAsync(
            string reference, VersionRef version, string actor, string meaning,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SignatureCheckResult.Invalid(
                "this deployment has no signature check configured."));
    }

    /// <summary>
    /// Whether the model gates this action with a signature, from any state it is permitted in.
    /// </summary>
    /// <remarks>
    /// The distinction CAP-LCM-012 draws, exposed so a caller can authorise on it: a signed
    /// transition is an act of this organisation by an accountable person, an unsigned one is
    /// the recording of a decision taken outside it. Answering "yes" if any state gates the
    /// action is the safe direction - it asks for the stronger permission, never the weaker.
    /// </remarks>
    public bool RequiresSignature(string action) => _model.Transitions.Any(
        t => string.Equals(t.Action, action, StringComparison.Ordinal) && t.RequiresSignature);

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

    /// <summary>
    /// Which version of a document is in force in a market at an instant, or null if none is.
    /// </summary>
    /// <remarks>
    /// Derived at the moment of asking and never stored (ADR-029 decision 2). A stored flag is
    /// correct when it is written and wrong from the first midnight afterwards, and nothing
    /// notices, because no write happened: the failure needs a clock and there is no clock in
    /// the write path.
    /// <para>
    /// In force means the latest version whose approval here took effect at or before the
    /// instant asked about and has not since been withdrawn. Answerable for any past instant,
    /// because it is a query over an append-only history.
    /// </para>
    /// </remarks>
    public async Task<int?> InForceAsync(
        string documentIdentifier, string market, DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(market);

        var history = await _store.DocumentHistoryAsync(documentIdentifier, market, cancellationToken);

        // The effect of each version in this market as at the instant asked about: the latest
        // date it took effect from, unless something recorded before that instant ended it.
        var effect = new Dictionary<int, DateTimeOffset>();
        foreach (var transition in history.Where(t => t.At <= at))
        {
            var version = transition.Subject.Version.Version;

            if (transition.EffectiveFrom is { } from)
            {
                effect[version] = from;
                continue;
            }

            // Anything that moves a version out of the approved state ends its effect - a
            // withdrawal, and any other route a market model defines out of approval. Read from
            // the model rather than from a list of action names, so a different regulator's
            // model does not need this code to know about it.
            if (string.Equals(transition.From, _model.ApprovedState, StringComparison.Ordinal))
            {
                effect.Remove(version);
            }
        }

        var inForce = effect
            .Where(e => e.Value <= at)
            .OrderByDescending(e => e.Value)
            .ThenByDescending(e => e.Key)
            .Select(e => (int?)e.Key)
            .FirstOrDefault();

        return inForce;
    }

    /// <summary>Moves a version's state in one market, or explains why it may not move.</summary>
    /// <param name="effectiveFrom">
    /// When the approval takes effect, required on a transition that records one and refused on
    /// any other. There is no default: "immediately" is stated by giving the approval's own
    /// timestamp, because a missing date defaulted to now is a guess that reads as a fact
    /// (ADR-029 decision 3).
    /// </param>
    public async Task<MarketStateTransition> TransitionAsync(
        VersionRef version,
        string market,
        string action,
        string actor,
        string? reason = null,
        string? signatureReference = null,
        DateTimeOffset? effectiveFrom = null,
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

        if (permitted.RequiresSignature)
        {
            if (string.IsNullOrWhiteSpace(signatureReference))
            {
                throw new TransitionRefusedException(version, action,
                    $"submitting to the regulator for {market} requires an electronic signature.");
            }

            if (await _spent.IsSignatureUsedAsync(signatureReference, cancellationToken))
            {
                throw new TransitionRefusedException(version, action,
                    "that signature has already been used for another transition.");
            }

            var signature = await _signatureCheck.IsValidAsync(
                signatureReference, version, actor, permitted.SignatureMeaning!, cancellationToken);

            if (!signature.IsValid)
            {
                throw new TransitionRefusedException(version, action,
                    $"the signature is not valid for this transition: {signature.Problem}");
            }
        }

        var now = _time.GetUtcNow();
        var approving = string.Equals(permitted.To, _model.ApprovedState, StringComparison.Ordinal);

        if (approving && effectiveFrom is null)
        {
            throw new TransitionRefusedException(version, action,
                $"recording this market's approval must state when it takes effect, and "
                + "'immediately' is stated by giving this moment rather than by leaving it out "
                + "(CAP-LCM-004).");
        }

        if (!approving && effectiveFrom is not null)
        {
            // Only an approval takes effect. A date on anything else would be a field nobody
            // reads, until somebody does.
            throw new TransitionRefusedException(version, action,
                $"'{action}' does not bring a version into force, so it cannot carry an "
                + "effective date.");
        }

        if (effectiveFrom is { } from && from < now)
        {
            // A market cannot bring a version into force before it decided to. Tolerated, it
            // produces a history in which effect precedes cause, and every "in force at" answer
            // computed from it is wrong in a way no later check can detect (ADR-029 decision 5).
            throw new TransitionRefusedException(version, action,
                $"an effective date of {from:u} precedes the approval it belongs to.");
        }

        var transition = new MarketStateTransition(
            subject, current, permitted.To, action, actor, now, reason,
            permitted.RequiresSignature ? signatureReference : null,
            effectiveFrom);

        await _store.AppendAsync(transition, cancellationToken);
        return transition;
    }
}
