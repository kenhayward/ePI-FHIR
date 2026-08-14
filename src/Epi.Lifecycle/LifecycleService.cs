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
    ISignatureCheck? signatureCheck = null)
{
    private readonly LifecycleModel _model = model ?? throw new ArgumentNullException(nameof(model));
    private readonly ILifecycleStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly TimeProvider _time = time ?? TimeProvider.System;
    private readonly ISignatureCheck? _signatureCheck = signatureCheck;

    /// <summary>Registers a version in the model's initial state.</summary>
    public Task RegisterAsync(VersionRef version, string author, CancellationToken cancellationToken = default) =>
        _store.RegisterAsync(version, author, _model.Initial, cancellationToken);

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

        if (permitted.RequiresSignature && string.IsNullOrWhiteSpace(signatureReference))
        {
            // A gate the model says must be signed cannot be passed unsigned, whatever the
            // caller intends. The signature mechanism itself is a separate concern.
            throw new TransitionRefusedException(version, action,
                "this transition requires an electronic signature.");
        }

        var transition = new StateTransition(
            version, current, permitted.To, action, actor, _time.GetUtcNow(), reason, signatureReference);

        await _store.AppendAsync(transition, cancellationToken);
        return transition;
    }
}
