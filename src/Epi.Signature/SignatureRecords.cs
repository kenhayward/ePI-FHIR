using Epi.ContentCore;

namespace Epi.Signature;

/// <summary>
/// What a signature asserts (ADR-020 decision 6). A closed set rather than free text, because
/// 21 CFR Part 11 Section 11.50(a)(3) requires the meaning of a signature to be recorded, and a
/// free-text field records whatever the caller chose to type.
/// </summary>
public enum SignatureMeaning
{
    Authorship,
    Review,
    Approval,
}

/// <summary>
/// A signer as the identity provider knows them, rather than as a caller describes them
/// (ADR-020 decision 3).
/// </summary>
public sealed record SignerIdentity(string Identifier, string PrintedName);

/// <summary>
/// Checks the two identification components a signature is built from, and returns who they
/// belong to (ADR-020 decisions 1 and 2).
/// </summary>
/// <remarks>
/// A port, deliberately. The demonstration verifies an identifier and a password against
/// Keycloak; production is expected to verify a certificate. Everything else about signing - the
/// manifest, the hash, the storage, the gate - is unchanged by that swap, which is the whole
/// reason this is an interface rather than a method.
/// </remarks>
public interface ICredentialVerifier
{
    /// <summary>The signer, or null if the credentials do not identify one.</summary>
    Task<SignerIdentity?> VerifyAsync(
        string identifier, string password, CancellationToken cancellationToken = default);
}

/// <summary>
/// The record of one signature (CAP-AUD-003, FN-AUD-005): who signed, under what name, what
/// they meant by it, when, and over exactly what content.
/// </summary>
/// <remarks>
/// Carries what 21 CFR Part 11 Section 11.50 requires to be recorded - printed name, date and
/// time, and meaning - together with the link Section 11.70 requires. The link is
/// <see cref="Document"/>, <see cref="Version"/>, and <see cref="ContentHash"/> together: a
/// manifest transplanted onto another version is detectable because its hash will not match that
/// version's content.
/// </remarks>
public sealed record SignatureManifest(
    string Reference,
    string SignerIdentifier,
    string SignerPrintedName,
    SignatureMeaning Meaning,
    DocumentIdentity Document,
    int Version,
    string ContentHash,
    DateTimeOffset SignedAt,
    string? Reason = null)
{
    /// <summary>Whether this signature was made over exactly the content supplied.</summary>
    public bool Covers(EpiDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        return Document == document.Identity
               && Version == document.Version
               && string.Equals(
                   ContentHash, Epi.Signature.ContentHash.Of(document.Bundle), StringComparison.Ordinal);
    }
}

/// <summary>
/// Where signatures are kept. No update and no delete, for the reason the audit sink has none:
/// a signature that can be amended is not evidence of anything (ADR-020 decision 7).
/// </summary>
public interface ISignatureStore
{
    Task AppendAsync(SignatureManifest manifest, CancellationToken cancellationToken = default);

    /// <summary>The signature with this reference, or null if there is none.</summary>
    Task<SignatureManifest?> FindAsync(string reference, CancellationToken cancellationToken = default);
}

/// <summary>
/// Raised when a signature is refused. Carries why, in terms safe to show the person signing.
/// </summary>
/// <remarks>
/// Deliberately does not distinguish an unknown signer from a wrong password: telling a caller
/// which of the two failed turns a signing gate into a means of enumerating users.
/// </remarks>
public sealed class SignatureRefusedException(string reason)
    : Exception($"The signature was refused: {reason}")
{
    public string Reason { get; } = reason;
}
