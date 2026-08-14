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

    public Task<string?> CurrentStateAsync(
        MarketVersion subject, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<MarketStateTransition>> HistoryAsync(
        MarketVersion subject, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<IReadOnlyDictionary<string, string>> StatesForAsync(
        VersionRef version, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task AppendAsync(
        MarketStateTransition transition, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
