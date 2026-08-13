namespace Epi.Governance.Audit;

/// <summary>
/// An append-only audit sink held in memory. The durable implementation is an append-only
/// table with WORM export (D3 Section 3.1); this exists so the governance layer can be
/// exercised without one, and is the reference the same tests hold both to.
/// </summary>
public sealed class InMemoryAuditSink(TimeProvider? time = null) : IAuditSink
{
    private readonly List<AuditRecord> _records = [];
    private readonly Lock _gate = new();
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public Task AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        lock (_gate)
        {
            // The sink stamps the time, not the caller. A contemporaneous record is one the
            // system timed, and a caller-supplied timestamp is a claim rather than evidence.
            _records.Add(record with { RecordedAt = _time.GetUtcNow() });
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<AuditRecord>> ReadAsync(CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            // A copy: a reader holding the list could otherwise change history by mistake.
            return Task.FromResult<IReadOnlyList<AuditRecord>>([.. _records]);
        }
    }
}
