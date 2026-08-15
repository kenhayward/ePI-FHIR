namespace Epi.Lifecycle;

/// <summary>
/// An in-memory lifecycle store. The durable one is a table alongside the audit trail; this is
/// the reference implementation, as elsewhere in the platform.
/// </summary>
public sealed class InMemoryLifecycleStore : ILifecycleStore, IPinnedContextStore
{
    private readonly Dictionary<string, string> _authors = [];
    private readonly Dictionary<string, string> _states = [];
    private readonly Dictionary<string, DateTimeOffset> _registered = [];
    private readonly List<StateTransition> _transitions = [];
    private readonly Dictionary<VersionRef, PinnedContext> _pins = [];
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

    public Task<IReadOnlyList<int>> VersionsInStateAsync(
        string documentIdentifier, string state, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);

        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<int>>(
            [
                .. _states
                    .Where(e => string.Equals(e.Value, state, StringComparison.Ordinal))
                    .Select(e => e.Key.Split('@'))
                    .Where(parts => parts.Length == 2
                        && string.Equals(parts[0], documentIdentifier, StringComparison.Ordinal))
                    .Select(parts => int.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture))
                    .Order(),
            ]);
        }
    }

    public Task AppendAsync(
        StateTransition transition, PinnedContext? pin = null,
        StateTransition? consequence = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);

        lock (_gate)
        {
            // Under one lock, so the reference implementation makes the same guarantee the
            // durable one does with a transaction (ADR-024 decision 1).
            if (pin is not null && !_pins.TryAdd(pin.Version, pin))
            {
                throw new ContextAlreadyPinnedException(pin.Version);
            }

            _transitions.Add(transition);
            _states[transition.Version.ToString()] = transition.To;

            if (consequence is not null)
            {
                _transitions.Add(consequence);
                _states[consequence.Version.ToString()] = consequence.To;
            }
        }

        return Task.CompletedTask;
    }

    public Task<PinnedContext?> ForAsync(
        VersionRef version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);

        lock (_gate)
        {
            return Task.FromResult(_pins.GetValueOrDefault(version));
        }
    }
}
