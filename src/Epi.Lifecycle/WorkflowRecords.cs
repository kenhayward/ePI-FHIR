namespace Epi.Lifecycle;

/// <summary>What happened to a task (ADR-031 decision 5).</summary>
public enum TaskEventKind
{
    /// <summary>Somebody was asked to act, because a transition put a version here.</summary>
    Raised,

    /// <summary>The ask moved to somebody else, or to another role.</summary>
    Reassigned,

    /// <summary>The transition the task asked for happened, so the ask is over.</summary>
    Closed,
}

/// <summary>
/// One thing that happened to a task, append-only like everything else that is evidence.
/// </summary>
/// <param name="Assignee">
/// The role or person the ask sits with after this event. A role by default: a task assigned to
/// somebody on leave is a task nobody sees, and the failure looks like nothing happening
/// (ADR-031 decision 4).
/// </param>
public sealed record TaskEvent(
    string TaskIdentifier,
    VersionRef Version,
    TaskEventKind Kind,
    string Action,
    string Assignee,
    string Actor,
    DateTimeOffset At,
    string? Reason = null);

/// <summary>
/// A task as it stands now, derived from its events.
/// </summary>
/// <remarks>
/// Derived rather than stored, for the reason ADR-019 gives about state: a field is correct when
/// it is written, and the history is correct always.
/// </remarks>
public sealed record WorkflowTask(
    string Identifier,
    VersionRef Version,
    string Action,
    string Assignee,
    DateTimeOffset RaisedAt,
    bool IsOpen)
{
    /// <summary>
    /// Whether this task has been open longer than the period allowed for it.
    /// </summary>
    /// <remarks>
    /// Asked rather than scheduled (ADR-031 decision 6). A task overdue is overdue whether or
    /// not anything noticed, and a job that has failed for a week is indistinguishable from a
    /// queue that is empty.
    /// </remarks>
    public bool IsOverdueAt(DateTimeOffset moment, TimeSpan? allowed) =>
        IsOpen && allowed is { } period && moment - RaisedAt > period;
}

/// <summary>
/// How a task's standing is worked out from its events.
/// </summary>
/// <remarks>
/// Shared by every store rather than implemented in each, because two derivations of one rule
/// drift - and the one that drifts is the one that decides whether somebody still has a job to
/// do. Derived rather than stored for the reason ADR-019 gives about state: a field is correct
/// when it is written, and the history is correct always.
/// </remarks>
public static class WorkflowTasks
{
    public static IReadOnlyList<WorkflowTask> Derive(IEnumerable<TaskEvent> events)
    {
        ArgumentNullException.ThrowIfNull(events);

        return
        [
            .. events
                .GroupBy(e => e.TaskIdentifier, StringComparer.Ordinal)
                .Where(group => group.Any(e => e.Kind == TaskEventKind.Raised))
                .Select(group =>
                {
                    var raised = group.First(e => e.Kind == TaskEventKind.Raised);
                    var last = group.Last();

                    return new WorkflowTask(
                        group.Key,
                        raised.Version,
                        raised.Action,
                        last.Assignee,
                        raised.At,
                        last.Kind != TaskEventKind.Closed);
                }),
        ];
    }
}

/// <summary>
/// The tasks routing has raised, append-only (CAP-WFL-001, ADR-031).
/// </summary>
/// <remarks>
/// A task records that somebody was asked to act. It is not state, and nothing here is consulted
/// to decide whether a transition may happen: the state model remains the only authority on
/// that, so a task may be missing, stale or wrong and the gate still holds.
/// </remarks>
public interface IWorkflowStore
{
    Task AppendAsync(TaskEvent taskEvent, CancellationToken cancellationToken = default);

    /// <summary>Every task raised against a version, as each stands now.</summary>
    Task<IReadOnlyList<WorkflowTask>> ForVersionAsync(
        VersionRef version, CancellationToken cancellationToken = default);

    /// <summary>Everything that has happened to one task, oldest first.</summary>
    Task<IReadOnlyList<TaskEvent>> HistoryAsync(
        string taskIdentifier, CancellationToken cancellationToken = default);

    /// <summary>Every open task assigned to any of these roles or people.</summary>
    Task<IReadOnlyList<WorkflowTask>> OpenForAsync(
        IReadOnlyCollection<string> assignees, CancellationToken cancellationToken = default);
}
