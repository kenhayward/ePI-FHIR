namespace Epi.Lifecycle;

/// <summary>Which version of which document a state belongs to.</summary>
public sealed record VersionRef(string DocumentIdentifier, int Version)
{
    public override string ToString() => $"{DocumentIdentifier}@{Version}";
}

/// <summary>
/// One move between states, recorded rather than applied (ADR-019 decisions 4 and 6).
/// </summary>
/// <remarks>
/// Transitions are append-only, so the state of a version at any past moment is derivable from
/// its history rather than overwritten by its present. A record saying a version is approved,
/// without when it became so and from what, cannot answer the question an inspection asks.
/// </remarks>
public sealed record StateTransition(
    VersionRef Version,
    string From,
    string To,
    string Action,
    string Actor,
    DateTimeOffset At,
    string? Reason = null,
    string? SignatureReference = null);

/// <summary>
/// What kind of artefact a registration is for.
/// </summary>
/// <remarks>
/// The engine never needed to know until it managed more than one thing. It manages templates
/// now (ADR-042 decision 3), and a registration that does not say what it is for is one the
/// reconciliation report reads as a label whose content never arrived - which is what happened,
/// and which reported every seeded template as a defect forever.
/// </remarks>
public static class RegisteredArtefact
{
    /// <summary>A label version, whose content lives in the content store.</summary>
    public const string Content = "content";

    /// <summary>A render template version, whose definition lives in the template store.</summary>
    public const string Template = "template";
}

/// <summary>A version's entry into lifecycle management: who, when, and for what.</summary>
public sealed record Registration(
    VersionRef Version,
    string Author,
    DateTimeOffset RegisteredAt,
    string Kind = RegisteredArtefact.Content);

/// <summary>
/// The lifecycle store: who authored a version, and every transition it has been through.
/// </summary>
/// <remarks>
/// No update and no delete, for the same reason the audit sink has none: the history is the
/// evidence. State is a record about a version, never a field on it (ADR-019 decision 1).
/// </remarks>
public interface ILifecycleStore : ISpentSignatures
{
    /// <summary>
    /// Registers a new version in the initial state, recording who authored it and when.
    /// </summary>
    /// <remarks>
    /// The moment matters as much as the author. Without it the history begins at the first
    /// transition, and "what state was this in on the third of March" cannot distinguish a
    /// version that was a draft then from one that did not yet exist (CAP-LCM-006).
    /// </remarks>
    /// <param name="kind">
    /// What the registration is for (<see cref="RegisteredArtefact"/>). Defaults to content,
    /// because that is what every registration was before the engine managed anything else, and
    /// because a stored registration that predates this is a label's.
    /// </param>
    Task RegisterAsync(VersionRef version, string author, string initialState,
        DateTimeOffset registeredAt, string kind = RegisteredArtefact.Content,
        CancellationToken cancellationToken = default);

    /// <summary>The person who authored a version, or null if it is unknown.</summary>
    Task<string?> AuthorOfAsync(VersionRef version, CancellationToken cancellationToken = default);

    /// <summary>When the version came under lifecycle management, or null if it never did.</summary>
    Task<DateTimeOffset?> RegisteredAtAsync(VersionRef version, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every registration made before a moment, oldest first (FN-LCM-008).
    /// </summary>
    /// <remarks>
    /// The only read here that does not start from a version somebody already knows about, and
    /// the only one reconciliation can use: an inert registration is by definition one nobody
    /// knows about. The cutoff is exclusive, and it is what lets a caller ignore registrations
    /// too recent to judge - a content write happens moments after its registration, so
    /// everything in flight looks inert.
    /// </remarks>
    Task<IReadOnlyList<Registration>> RegistrationsBeforeAsync(
        DateTimeOffset moment, CancellationToken cancellationToken = default);

    /// <summary>The state a version holds now, or null if it was never registered.</summary>
    Task<string?> CurrentStateAsync(VersionRef version, CancellationToken cancellationToken = default);

    /// <summary>Every transition a version has been through, oldest first.</summary>
    Task<IReadOnlyList<StateTransition>> HistoryAsync(VersionRef version,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a transition and, where one is supplied, the context it pinned - atomically
    /// (ADR-024 decisions 1 and 2).
    /// </summary>
    /// <exception cref="ContextAlreadyPinnedException">
    /// If a context is supplied for a version that already has one. Neither the transition nor
    /// the pin is written in that case: an approval happens once.
    /// </exception>
    /// <param name="consequence">
    /// A transition this one causes - the supersession of the version it displaces (ADR-030).
    /// Written in the same transaction, because otherwise the window it exists to close simply
    /// moves to between the two writes.
    /// </param>
    Task AppendAsync(
        StateTransition transition, PinnedContext? pin = null,
        StateTransition? consequence = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Every version of a document that holds this state now, ascending.
    /// </summary>
    /// <remarks>
    /// What supersession is worked out from: approving a version has to find the one it
    /// displaces, and that is a question about the document rather than about any one version.
    /// </remarks>
    Task<IReadOnlyList<int>> VersionsInStateAsync(
        string documentIdentifier, string state, CancellationToken cancellationToken = default);
}

/// <summary>Raised when a transition is refused. Carries why, because the caller must be told.</summary>
public sealed class TransitionRefusedException(VersionRef version, string action, string reason)
    : Exception($"Transition '{action}' on {version} was refused: {reason}")
{
    public string Action { get; } = action;

    public string Reason { get; } = reason;
}
