namespace Epi.Lifecycle;

/// <summary>
/// An in-memory lifecycle store. The durable one is a table alongside the audit trail; this is
/// the reference implementation, as elsewhere in the platform.
/// </summary>
public sealed class InMemoryLifecycleStore : ILifecycleStore
{
    private readonly Dictionary<string, string> _authors = [];
    private readonly Dictionary<string, string> _states = [];
    private readonly List<StateTransition> _transitions = [];
    private readonly Lock _gate = new();

    public Task RegisterAsync(VersionRef version, string author, string initialState,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);

        lock (_gate)
        {
            _authors[version.ToString()] = author;
            _states[version.ToString()] = initialState;
        }

        return Task.CompletedTask;
    }

    public Task<string?> AuthorOfAsync(VersionRef version, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_authors.GetValueOrDefault(version.ToString()));
        }
    }

    public Task<string?> CurrentStateAsync(VersionRef version, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_states.GetValueOrDefault(version.ToString()));
        }
    }

    public Task<IReadOnlyList<StateTransition>> HistoryAsync(
        VersionRef version, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<StateTransition>>(
                [.. _transitions.Where(t => t.Version == version)]);
        }
    }

    public Task AppendAsync(StateTransition transition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);

        lock (_gate)
        {
            _transitions.Add(transition);
            _states[transition.Version.ToString()] = transition.To;
        }

        return Task.CompletedTask;
    }
}
