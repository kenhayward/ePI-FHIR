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
        ArgumentNullException.ThrowIfNull(context);

        lock (_gate)
        {
            // Refused rather than overwritten. Overwriting is silent, and a record that can be
            // replaced is not a record (ADR-023 decision 3).
            if (!_pins.TryAdd(context.Version, context))
            {
                throw new ContextAlreadyPinnedException(context.Version);
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
