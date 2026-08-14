using Epi.ContentCore;
using Hl7.Fhir.Model;

namespace Epi.Governance.Audit;

/// <summary>
/// Records every content write to the audit sink (CAP-AUD-001, FN-AUD-001, FN-AUD-002).
/// </summary>
/// <remarks>
/// A decorator, so auditing is on the write path by construction. It records failures as well
/// as successes: an audit trail that only contains what worked cannot answer what was
/// attempted, which is the question an investigation usually starts with.
/// </remarks>
public sealed class AuditingContentStore(IContentStore inner, IAuditSink audit, string actor) : IContentStore
{
    private readonly IContentStore _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IAuditSink _audit = audit ?? throw new ArgumentNullException(nameof(audit));

    public async Task<EpiDocument> CreateAsync(DocumentIdentity identity, Bundle bundle, CancellationToken cancellationToken = default)
    {
        try
        {
            var stored = await _inner.CreateAsync(identity, bundle, cancellationToken);
            await Record("content.create", $"{stored.Identity}@{stored.Version}", AuditOutcome.Succeeded,
                before: null, after: EpiBundleReader.Write(stored.Bundle), cancellationToken: cancellationToken);
            return stored;
        }
        catch (Exception error)
        {
            await Record("content.create", "(not created)", Outcome(error), null, null, error.Message, cancellationToken);
            throw;
        }
    }

    public async Task<EpiDocument> CreateVersionAsync(
        DocumentIdentity identity, int version, Bundle bundle, CancellationToken cancellationToken = default)
    {
        // Read the prior version first: the before value is the point of the record, and it
        // cannot be recovered after the fact if the write fails midway.
        var previous = await _inner.GetLatestAsync(identity, cancellationToken);

        try
        {
            var stored = await _inner.CreateVersionAsync(identity, version, bundle, cancellationToken);
            await Record("content.version", $"{stored.Identity}@{stored.Version}", AuditOutcome.Succeeded,
                previous is null ? null : EpiBundleReader.Write(previous.Bundle),
                EpiBundleReader.Write(stored.Bundle), cancellationToken: cancellationToken);
            return stored;
        }
        catch (Exception error)
        {
            await Record("content.version", identity.ToString(), Outcome(error),
                previous is null ? null : EpiBundleReader.Write(previous.Bundle), null, error.Message, cancellationToken);
            throw;
        }
    }

    public Task<EpiDocument?> GetAsync(
        DocumentIdentity identity, int version, CancellationToken cancellationToken = default) =>
        _inner.GetAsync(identity, version, cancellationToken);

    public Task<EpiDocument?> GetLatestAsync(
        DocumentIdentity identity, CancellationToken cancellationToken = default) =>
        _inner.GetLatestAsync(identity, cancellationToken);

    public Task<IReadOnlyList<int>> VersionsAsync(
        DocumentIdentity identity, CancellationToken cancellationToken = default) =>
        _inner.VersionsAsync(identity, cancellationToken);

    private static AuditOutcome Outcome(Exception error) =>
        error.GetType().Name.Contains("Denied", StringComparison.Ordinal)
            ? AuditOutcome.Denied
            : AuditOutcome.Failed;

    private Task Record(string action, string target, AuditOutcome outcome,
        string? before, string? after, string? reason = null, CancellationToken cancellationToken = default) =>
        _audit.AppendAsync(
            new AuditRecord(actor, action, target, outcome, default, before, after, reason), cancellationToken);
}
