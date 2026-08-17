using Epi.ContentCore;

namespace Epi.Signature;

/// <summary>
/// Signs a version: checks the credentials, builds the manifest, and records it
/// (FN-AUD-005, CAP-AUD-003).
/// </summary>
/// <remarks>
/// Every route to a signature comes through here, for the reason every route to a state change
/// comes through <c>LifecycleService</c>: a control enforced in one place and not another is not
/// a control.
/// </remarks>
public sealed class ElectronicSignatureService(
    ICredentialVerifier verifier, ISignatureStore store, TimeProvider? time = null)
    : IElectronicSignatureService
{
    /// <summary>
    /// One refusal message for every failure, deliberately. Distinguishing an unknown signer
    /// from a wrong password would make an approval screen a means of enumerating who holds an
    /// account.
    /// </summary>
    private const string Refusal = "the credentials supplied did not identify a signer.";

    private readonly ICredentialVerifier _verifier =
        verifier ?? throw new ArgumentNullException(nameof(verifier));

    private readonly ISignatureStore _store = store ?? throw new ArgumentNullException(nameof(store));
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>Signs a version, or explains why the signature was refused.</summary>
    public Task<SignatureManifest> SignAsync(
        EpiDocument document,
        string signerIdentifier,
        string password,
        SignatureMeaning meaning,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        return SignAsync(
            new SignableArtefact(
                document.Identity, document.Version, ContentHash.Of(document.Bundle)),
            signerIdentifier, password, meaning, reason, cancellationToken);
    }

    /// <summary>Signs anything the platform can name and hash, or explains the refusal.</summary>
    public async Task<SignatureManifest> SignAsync(
        SignableArtefact artefact,
        string signerIdentifier,
        string password,
        SignatureMeaning meaning,
        string? reason = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(artefact);
        ArgumentException.ThrowIfNullOrWhiteSpace(signerIdentifier);

        // A blank password is refused here rather than handed on. Some directory servers treat
        // a bind with an empty password as an anonymous success, and whether the one an adopter
        // deploys does so is not something a signing gate should depend on.
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new SignatureRefusedException(Refusal);
        }

        // The two identification components, checked at the point of signing rather than
        // inferred from a session (ADR-020 decision 1).
        var signer = await _verifier.VerifyAsync(signerIdentifier, password, cancellationToken)
            ?? throw new SignatureRefusedException(Refusal);

        var manifest = new SignatureManifest(
            // Opaque and time-ordered, on the same reasoning as document identity (ADR-015).
            Guid.CreateVersion7().ToString(),

            // What the verifier returned, not what the caller said: a caller able to state the
            // signer's name could sign in someone else's (ADR-020 decision 3).
            signer.Identifier,
            signer.PrintedName,
            meaning,
            artefact.Identity,
            artefact.Version,
            artefact.ContentHash,

            // The platform's clock, not the caller's, for the reason ADR-018 gives for audit
            // records: a contemporaneous time the signer could set is not contemporaneous.
            _time.GetUtcNow(),
            reason);

        await _store.AppendAsync(manifest, cancellationToken);
        return manifest;
    }
}
