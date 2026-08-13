namespace Epi.Lifecycle;

/// <summary>One permitted move between states (ADR-019 decision 3).</summary>
public sealed record LifecycleTransition(
    string From,
    string To,
    string Action,
    bool RequiresSignature = false,
    bool SegregatedFromAuthor = false);

/// <summary>
/// The states a label may hold and the moves permitted between them. Configuration, not code:
/// an organisation with a different approval process changes a file (capability 21, ADR-012).
/// </summary>
public sealed class LifecycleModel(
    string name, string initial, IReadOnlyList<string> states, IReadOnlyList<LifecycleTransition> transitions)
{
    public string Name { get; } = name;

    public string Initial { get; } = initial;

    public IReadOnlyList<string> States { get; } = states;

    public IReadOnlyList<LifecycleTransition> Transitions { get; } = transitions;

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
