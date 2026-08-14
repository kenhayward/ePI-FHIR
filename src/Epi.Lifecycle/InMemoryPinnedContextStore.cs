namespace Epi.Lifecycle;

/// <summary>
/// An in-memory pinned-context store: the reference implementation the conformance suite holds
/// every implementation to. Real persistence is PostgreSQL.
/// </summary>
public sealed class InMemoryPinnedContextStore : IPinnedContextStore
{
    private readonly Dictionary<VersionRef, PinnedContext> _pins = [];
    private readonly Lock _gate = new();

    public Task PinAsync(PinnedContext context, CancellationToken cancellationToken = default)
    {
        _ = _pins;
        _ = _gate;
        throw new NotImplementedException();
    }

    public Task<PinnedContext?> ForAsync(
        VersionRef version, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
