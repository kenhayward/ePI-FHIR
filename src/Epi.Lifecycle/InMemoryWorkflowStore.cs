namespace Epi.Lifecycle;

/// <summary>
/// An in-memory workflow store: the reference implementation the conformance suite holds every
/// implementation to.
/// </summary>
public sealed class InMemoryWorkflowStore : IWorkflowStore
{
    private readonly List<TaskEvent> _events = [];
    private readonly Lock _gate = new();

    public Task AppendAsync(TaskEvent taskEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(taskEvent);

        lock (_gate)
        {
            _events.Add(taskEvent);
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<TaskEvent>> HistoryAsync(
        string taskIdentifier, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskIdentifier);

        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<TaskEvent>>(
            [
                .. _events.Where(e => string.Equals(
                    e.TaskIdentifier, taskIdentifier, StringComparison.Ordinal)),
            ]);
        }
    }

    public Task<IReadOnlyList<WorkflowTask>> ForVersionAsync(
        VersionRef version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);

        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<WorkflowTask>>(
            [
                .. WorkflowTasks.Derive(_events.Where(e => e.Version == version)),
            ]);
        }
    }

    public Task<IReadOnlyList<WorkflowTask>> OpenForAsync(
        IReadOnlyCollection<string> assignees, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignees);

        // An empty set matches nothing, not everything - the same failure ADR-022 decision 3
        // guards the search predicate against.
        var wanted = assignees.ToHashSet(StringComparer.Ordinal);

        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<WorkflowTask>>(
            [
                .. WorkflowTasks.Derive(_events)
                    .Where(task => task.IsOpen && wanted.Contains(task.Assignee)),
            ]);
        }
    }

}
