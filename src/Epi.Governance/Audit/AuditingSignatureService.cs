using System.Text.Json;
using System.Text.Json.Serialization;
using Epi.ContentCore;
using Epi.Signature;

namespace Epi.Governance.Audit;

/// <summary>
/// Records every signing attempt, successful or not (CAP-AUD-003, ADR-020 decision 9).
/// </summary>
/// <remarks>
/// A decorator, so recording is on the signing path by construction rather than by a caller
/// remembering. 21 CFR Part 11 Section 11.300(d) requires attempted unauthorised use of a
/// credential to be detected and reported, and a wrong password at an approval gate is that
/// signal - so a refusal is recorded as deliberately as a signature.
/// </remarks>
public sealed class AuditingSignatureService(IElectronicSignatureService inner, IAuditSink audit)
    : IElectronicSignatureService
{
    /// <summary>
    /// The meaning of a signature is written out by name. An enum recorded as its ordinal is a
    /// number whose meaning lives in a source file that may have moved on by the time anyone
    /// reads the record.
    /// </summary>
    private static readonly JsonSerializerOptions ManifestFormat = new()
    {
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly IElectronicSignatureService _inner =
        inner ?? throw new ArgumentNullException(nameof(inner));

    private readonly IAuditSink _audit = audit ?? throw new ArgumentNullException(nameof(audit));

    public Task<SignatureManifest> SignAsync(
        EpiDocument document,
        string signerIdentifier,
        string password,
        SignatureMeaning meaning,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        return RecordedAsync(
            $"{document.Identity}@{document.Version}",
            signerIdentifier,
            reason,
            token => _inner.SignAsync(
                document, signerIdentifier, password, meaning, reason, token),
            cancellationToken);
    }

    public Task<SignatureManifest> SignAsync(
        SignableArtefact artefact,
        string signerIdentifier,
        string password,
        SignatureMeaning meaning,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artefact);

        return RecordedAsync(
            $"{artefact.Identity}@{artefact.Version}",
            signerIdentifier,
            reason,
            token => _inner.SignAsync(
                artefact, signerIdentifier, password, meaning, reason, token),
            cancellationToken);
    }

    /// <summary>
    /// Signs, and records the attempt either way. Written once for both overloads: an audit
    /// decorator that recorded one route and not the other would be a control with a hole in
    /// exactly the shape of whatever was added last.
    /// </summary>
    private async Task<SignatureManifest> RecordedAsync(
        string target,
        string signerIdentifier,
        string? reason,
        Func<CancellationToken, Task<SignatureManifest>> sign,
        CancellationToken cancellationToken)
    {
        try
        {
            var manifest = await sign(cancellationToken);

            // The manifest is the record. It carries no credential - only who signed, under
            // what name, what they meant, when, and the hash of what they signed.
            await Record(manifest.SignerIdentifier, target, AuditOutcome.Succeeded,
                after: JsonSerializer.Serialize(manifest, ManifestFormat),
                reason: reason, cancellationToken: cancellationToken);

            return manifest;
        }
        catch (SignatureRefusedException refusal)
        {
            // The actor is the identifier that was claimed, which is the point of recording the
            // attempt at all. The record says so rather than leaving a reader to assume the
            // platform vouched for it.
            await Record(signerIdentifier, target, AuditOutcome.Denied, after: null,
                reason: $"Signing was refused: {refusal.Reason} The actor is the identifier "
                        + "claimed by the caller, not a verified signer.",
                cancellationToken: cancellationToken);

            throw;
        }
        catch (Exception error)
        {
            await Record(signerIdentifier, target, AuditOutcome.Failed, after: null,
                reason: $"Signing failed: {error.Message} The actor is the identifier claimed "
                        + "by the caller, not a verified signer.",
                cancellationToken: cancellationToken);

            throw;
        }
    }

    private Task Record(string actor, string target, AuditOutcome outcome, string? after,
        string? reason, CancellationToken cancellationToken) =>
        _audit.AppendAsync(
            new AuditRecord(actor, "signature.sign", target, outcome, default, null, after, reason),
            cancellationToken);
}
