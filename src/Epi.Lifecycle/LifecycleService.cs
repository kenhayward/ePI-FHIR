namespace Epi.Lifecycle;

/// <summary>
/// Applies the state model to a version: validates the move, enforces the conditions the model
/// attaches to it, and records it (FN-LCM-002, FN-LCM-003, FN-WFL-002).
/// </summary>
/// <remarks>
/// Every route into a state change comes through here. Segregation of duties enforced in one
/// endpoint and not another is not segregation of duties, and the way that usually happens is a
/// second code path that changes state without going past the check.
/// </remarks>
public sealed class LifecycleService(
    LifecycleModel model,
    ILifecycleStore store,
    TimeProvider? time = null,
    ISignatureCheck? signatureCheck = null,
    ISpentSignatures? spent = null)
{
    // Defaults to this store alone, which is right when it is the only one. Composition passes
    // a SpentSignatures over every store that records signature use, so a signature spent on a
    // regulatory submission cannot then carry an internal approval.
    private readonly ISpentSignatures _spent = spent ?? store;

    private readonly LifecycleModel _model = model ?? throw new ArgumentNullException(nameof(model));
    private readonly ILifecycleStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    private readonly ISignatureCheck _signatureCheck =
        signatureCheck ?? (model?.Transitions.Any(t => t.RequiresSignature) == true

            // Refusing to start is the point. The tempting alternative - accept any non-empty
            // reference when nothing is configured to check it - is a gate that looks like a
            // control and is not one, and it would deploy silently.
            ? throw new ArgumentException(
                "This state model has transitions that require an electronic signature, so a "
                + "signature check must be supplied. Without one the gate would accept any "
                + "reference at all.",
                nameof(signatureCheck))
            : NoSignedGates.Instance);

    /// <summary>
    /// Stands in where a model has no signed gates at all, so the field need not be nullable.
    /// Refuses everything, because reaching it would mean a signed gate appeared unnoticed.
    /// </summary>
    private sealed class NoSignedGates : ISignatureCheck
    {
        public static NoSignedGates Instance { get; } = new();

        public Task<SignatureCheckResult> IsValidAsync(
            string reference, VersionRef version, string actor, string meaning,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SignatureCheckResult.Invalid(
                "this deployment has no signature check configured."));
    }

    /// <summary>Registers a version in the model's initial state.</summary>
    public Task RegisterAsync(VersionRef version, string author, CancellationToken cancellationToken = default) =>
        _store.RegisterAsync(version, author, _model.Initial, _time.GetUtcNow(), cancellationToken);

    /// <summary>The state this version holds now, or null if it is not under management.</summary>
    /// <remarks>
    /// Reads go through the service for the same reason writes do: a caller that reached past
    /// it to the store would be reading a different notion of state from the one the engine
    /// enforces. Null rather than an initial state, because a version nobody registered has no
    /// state to report - unlike a market, where every version starts unsubmitted.
    /// </remarks>
    public Task<string?> CurrentStateAsync(VersionRef version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        return _store.CurrentStateAsync(version, cancellationToken);
    }

    /// <summary>Who authored this version, or null if it is unknown.</summary>
    public Task<string?> AuthorOfAsync(VersionRef version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        return _store.AuthorOfAsync(version, cancellationToken);
    }

    /// <summary>
    /// The state this version held at a past moment, or null if it did not exist then.
    /// </summary>
    /// <remarks>
    /// Derived from the append-only history rather than read from a field, which is what
    /// ADR-019 decision 4 bought: a state column can say what a version is now and never what
    /// it was. A transition timestamped at a moment means the version was in its new state at
    /// that moment, so the comparison is inclusive of the transition's own instant.
    /// </remarks>
    public async Task<string?> StateAtAsync(
        VersionRef version, DateTimeOffset moment, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);

        // Two different nulls, and conflating them would be a lie in one direction or the
        // other. A version nobody registered has no state at any moment; a version registered
        // before the platform recorded registration times has an unknown start, and absence of
        // a recorded time is not evidence that it did not exist.
        if (await _store.CurrentStateAsync(version, cancellationToken) is null)
        {
            return null;
        }

        var registered = await _store.RegisteredAtAsync(version, cancellationToken);
        if (registered is not null && moment < registered)
        {
            // Null, not the initial state. Answering "draft" for a moment before the version
            // was registered would place a document in history that was not there.
            return null;
        }

        var last = (await _store.HistoryAsync(version, cancellationToken))
            .Where(transition => transition.At <= moment)
            .LastOrDefault();

        return last?.To ?? _model.Initial;
    }

    /// <summary>Every transition this version has been through, oldest first.</summary>
    /// <remarks>
    /// The evidence a reconstruction is built from: who moved a version, when, and on the
    /// strength of which signature (CAP-LCM-006, ADR-023 decision 4).
    /// </remarks>
    public Task<IReadOnlyList<StateTransition>> HistoryAsync(
        VersionRef version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        return _store.HistoryAsync(version, cancellationToken);
    }

    /// <summary>Moves a version to a new state, or explains why it may not move.</summary>
    public async Task<StateTransition> TransitionAsync(
        VersionRef version,
        string action,
        string actor,
        string? reason = null,
        string? signatureReference = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);
        ArgumentException.ThrowIfNullOrWhiteSpace(actor);

        var current = await _store.CurrentStateAsync(version, cancellationToken)
            ?? throw new TransitionRefusedException(
                version, action, "the version is not under lifecycle management.");

        var permitted = _model.Find(current, action)
            ?? throw new TransitionRefusedException(
                version, action, $"the model permits no {action} from {current}.");

        if (permitted.SegregatedFromAuthor)
        {
            // Segregation of duties (CAP-IAM-006). The author is read from the store rather
            // than supplied by the caller: a caller that could state who authored a version
            // could approve its own work simply by saying otherwise.
            var author = await _store.AuthorOfAsync(version, cancellationToken);
            if (author is null)
            {
                throw new TransitionRefusedException(version, action,
                    "the author of this version is unknown, so segregation of duties cannot be assured.");
            }

            if (string.Equals(author, actor, StringComparison.Ordinal))
            {
                throw new TransitionRefusedException(version, action,
                    "the author of a version may not approve it.");
            }
        }

        if (permitted.RequiresSignature)
        {
            // A gate the model says must be signed cannot be passed unsigned, whatever the
            // caller intends.
            if (string.IsNullOrWhiteSpace(signatureReference))
            {
                throw new TransitionRefusedException(version, action,
                    "this transition requires an electronic signature.");
            }

            // Single use, answered from the transition history rather than by marking the
            // signature spent. Without it one approval signature could be replayed against
            // every later gate, making a signature a token its holder can spend rather than an
            // assertion about one act.
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

        var transition = new StateTransition(
            version, current, permitted.To, action, actor, _time.GetUtcNow(), reason, signatureReference);

        await _store.AppendAsync(transition, cancellationToken);
        return transition;
    }
}
