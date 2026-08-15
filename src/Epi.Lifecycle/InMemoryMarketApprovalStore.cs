namespace Epi.Lifecycle;

/// <summary>
/// An in-memory per-market approval store. The durable one is a table alongside the audit
/// trail; this is the reference implementation, as elsewhere in the platform.
/// </summary>
public sealed class InMemoryMarketApprovalStore : IMarketApprovalStore
{
    private readonly Dictionary<string, string> _states = [];
    private readonly List<MarketStateTransition> _transitions = [];
    private readonly Lock _gate = new();

    public Task<IReadOnlyList<MarketStateTransition>> DocumentHistoryAsync(
        string documentIdentifier, string market, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(market);

        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<MarketStateTransition>>(
            [
                .. _transitions.Where(t =>
                    string.Equals(t.Subject.Version.DocumentIdentifier, documentIdentifier, StringComparison.Ordinal)
                    && string.Equals(t.Subject.Market, market, StringComparison.Ordinal)),
            ]);
        }
    }

    public Task<string?> CurrentStateAsync(
        MarketVersion subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        lock (_gate)
        {
            return Task.FromResult(_states.GetValueOrDefault(subject.ToString()));
        }
    }

    public Task<IReadOnlyList<MarketStateTransition>> HistoryAsync(
        MarketVersion subject, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);

        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<MarketStateTransition>>(
                [.. _transitions.Where(t => t.Subject == subject)]);
        }
    }

    public Task<IReadOnlyDictionary<string, string>> StatesForAsync(
        VersionRef version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);

        lock (_gate)
        {
            // Only the markets this version has actually moved in. Filling in the ones it has
            // not is the service's job, because only the service knows which markets exist.
            return Task.FromResult<IReadOnlyDictionary<string, string>>(
                _transitions
                    .Where(t => t.Subject.Version == version)
                    .GroupBy(t => t.Subject.Market, StringComparer.Ordinal)
                    .ToDictionary(g => g.Key, g => g.Last().To, StringComparer.Ordinal));
        }
    }

    public Task<bool> IsSignatureUsedAsync(
        string reference, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        lock (_gate)
        {
            return Task.FromResult(_transitions.Any(
                t => string.Equals(t.SignatureReference, reference, StringComparison.Ordinal)));
        }
    }

    public Task AppendAsync(
        MarketStateTransition transition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);

        lock (_gate)
        {
            _transitions.Add(transition);
            _states[transition.Subject.ToString()] = transition.To;
        }

        return Task.CompletedTask;
    }
}
