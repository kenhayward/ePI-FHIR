using Epi.ContentCore;
using Epi.Signature;

namespace Epi.Governance.Audit;

/// <summary>
/// Records every signing attempt, successful or not (CAP-AUD-003, ADR-020 decision 9).
/// </summary>
public sealed class AuditingSignatureService(IElectronicSignatureService inner, IAuditSink audit)
    : IElectronicSignatureService
{
    private readonly IElectronicSignatureService _inner =
        inner ?? throw new ArgumentNullException(nameof(inner));

    private readonly IAuditSink _audit = audit ?? throw new ArgumentNullException(nameof(audit));

    public Task<SignatureManifest> SignAsync(
        EpiDocument document,
        string signerIdentifier,
        string password,
        SignatureMeaning meaning,
        string? reason = null,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();
}
