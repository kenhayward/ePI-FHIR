namespace Epi.Governance.Audit;

/// <summary>What happened, to what, by whom, and what changed (CAP-AUD-001).</summary>
/// <remarks>
/// One shape for every capability, per the D2.5 cross-capability note on keeping the audit
/// contract uniform. ALCOA+ asks that a record be attributable, legible, contemporaneous,
/// original, and accurate: attribution is <see cref="Actor"/>, contemporaneity is
/// <see cref="RecordedAt"/> taken by the sink rather than supplied by the caller, and accuracy
/// is the before and after pair rather than a description of the change.
/// </remarks>
public sealed record AuditRecord(
    string Actor,
    string Action,
    string Target,
    AuditOutcome Outcome,
    DateTimeOffset RecordedAt,
    string? Before = null,
    string? After = null,
    string? Reason = null);

/// <summary>
/// Whether the action succeeded. Denials are audited as deliberately as successes: an audit
/// trail that records only what was permitted cannot answer who tried (CAP-IAM-009).
/// </summary>
public enum AuditOutcome
{
    Succeeded,
    Denied,
    Failed,
}

/// <summary>
/// The audit sink (capability 19). There is deliberately no update and no delete: records are
/// append-only, and immutability is a property of the interface rather than a convention
/// (CAP-AUD-002). Disposition at end of retention is capability 22's, not a caller's.
/// </summary>
public interface IAuditSink
{
    Task AppendAsync(AuditRecord record, CancellationToken cancellationToken = default);

    /// <summary>Every record, oldest first, for inspection and reconstruction (CAP-AUD-004).</summary>
    Task<IReadOnlyList<AuditRecord>> ReadAsync(CancellationToken cancellationToken = default);
}
