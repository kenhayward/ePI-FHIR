using Epi.ContentCore;

namespace Epi.Signature;

/// <summary>
/// Signs a version: checks the credentials, builds the manifest, and records it
/// (FN-AUD-005, CAP-AUD-003).
/// </summary>
public sealed class ElectronicSignatureService(
    ICredentialVerifier verifier, ISignatureStore store, TimeProvider? time = null)
{
    private readonly ICredentialVerifier _verifier = verifier;
    private readonly ISignatureStore _store = store;
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <summary>Signs a version, or explains why the signature was refused.</summary>
    public Task<SignatureManifest> SignAsync(
        EpiDocument document,
        string signerIdentifier,
        string password,
        SignatureMeaning meaning,
        string? reason = null,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();
}
