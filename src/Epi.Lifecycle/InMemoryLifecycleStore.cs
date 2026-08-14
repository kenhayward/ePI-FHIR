namespace Epi.Lifecycle;

/// <summary>
/// An in-memory lifecycle store. The durable one is a table alongside the audit trail; this is
/// the reference implementation, as elsewhere in the platform.
/// </summary>
public sealed class InMemoryLifecycleStore : ILifecycleStore
{
    private readonly Dictionary<string, string> _authors = [];
    private readonly Dictionary<string, string> _states = [];
    private readonly Dictionary<string, DateTimeOffset> _registered = [];
    private readonly List<StateTransition> _transitions = [];
    private readonly Lock _gate = new();

    public Task RegisterAsync(VersionRef version, string author, string initialState,
        DateTimeOffset registeredAt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);

        lock (_gate)
        {
            // Refused rather than overwritten. The recorded author is what segregation of
            // duties is checked against, so a second registration would let someone quietly
            // become eligible to approve their own work (CAP-IAM-006).
            if (!_authors.TryAdd(version.ToString(), author))
            {
                throw new InvalidOperationException(
                    $"{version} is already under lifecycle management, authored by "
                    + $"'{_authors[version.ToString()]}'.");
            }

            _states[version.ToString()] = initialState;
            _registered[version.ToString()] = registeredAt;
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

    public Task<DateTimeOffset?> RegisteredAtAsync(
        VersionRef version, CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult(_registered.TryGetValue(version.ToString(), out var at)
                ? at
                : (DateTimeOffset?)null);
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

    public Task<bool> IsSignatureUsedAsync(
        string reference, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        lock (_gate)
        {
            // Across every version, not only the one being transitioned: a reference is unique
            // platform-wide, so re-use is refused wherever it is attempted.
            return Task.FromResult(_transitions.Any(
                t => string.Equals(t.SignatureReference, reference, StringComparison.Ordinal)));
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
