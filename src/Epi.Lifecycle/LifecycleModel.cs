namespace Epi.Lifecycle;

/// <summary>One permitted move between states (ADR-019 decision 3).</summary>
/// <param name="SignatureMeaning">
/// What a signature at this gate must assert. Required where <paramref name="RequiresSignature"/>
/// is set: a signature captured as a review is not an approval, and a gate that accepted either
/// would be recording assent nobody gave.
/// </param>
public sealed record LifecycleTransition(
    string From,
    string To,
    string Action,
    bool RequiresSignature = false,
    bool SegregatedFromAuthor = false,
    string? SignatureMeaning = null);

/// <summary>
/// The states a label may hold and the moves permitted between them. Configuration, not code:
/// an organisation with a different approval process changes a file (capability 21, ADR-012).
/// </summary>
public sealed class LifecycleModel(
    string name, string initial, IReadOnlyList<string> states, IReadOnlyList<LifecycleTransition> transitions,
    string? approvedState = null)
{
    public string Name { get; } = name;

    public string Initial { get; } = initial;

    public IReadOnlyList<string> States { get; } = states;

    public IReadOnlyList<LifecycleTransition> Transitions { get; } = transitions;

    /// <summary>
    /// Which of the states means approved, where the model has one (ADR-022 decision 7).
    /// </summary>
    /// <remarks>
    /// Search has to be able to ask for the current-approved version without knowing how an
    /// organisation spells approval, and a model need not have such a state at all - a review
    /// workflow that ends in "published" is a legitimate model. Named in configuration rather
    /// than inferred, because inferring it from a state called "approved" would work on the
    /// shipped file and quietly fail on anyone else's.
    /// </remarks>
    public string? ApprovedState { get; } = approvedState;

    /// <summary>The transition for this action from this state, or null if none is permitted.</summary>
    /// <remarks>
    /// Matched exactly, not loosely. A state model is a control, and matching "Approved" to
    /// "approved" would let a typo in a caller succeed silently, which is the wrong failure
    /// mode for a gate.
    /// </remarks>
    public LifecycleTransition? Find(string from, string action) => Transitions.FirstOrDefault(
        t => string.Equals(t.From, from, StringComparison.Ordinal)
             && string.Equals(t.Action, action, StringComparison.Ordinal));
}

/// <summary>Raised when a transition the model does not permit is attempted.</summary>
public sealed class TransitionNotPermittedException(string from, string action)
    : Exception($"No transition '{action}' is permitted from state '{from}'.")
{
    public string From { get; } = from;

    public string Action { get; } = action;
}
