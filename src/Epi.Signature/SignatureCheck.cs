using Epi.Lifecycle;

namespace Epi.Signature;

/// <summary>
/// Answers the lifecycle engine's question about a signature (FN-WFL-003, CAP-WFL-003).
/// </summary>
/// <remarks>
/// Every answer is a refusal unless all of it holds: the reference names a signature this
/// platform issued, made by the person making the transition, over that version, asserting what
/// the gate requires. A check that verified three of the four would be a control with a shape
/// rather than a control.
/// </remarks>
public sealed class SignatureCheck(ISignatureStore store) : ISignatureCheck
{
    private readonly ISignatureStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public async Task<SignatureCheckResult> IsValidAsync(
        string reference,
        VersionRef version,
        string actor,
        string meaning,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);
        ArgumentNullException.ThrowIfNull(version);

        var manifest = await _store.FindAsync(reference, cancellationToken);
        if (manifest is null)
        {
            return SignatureCheckResult.Invalid("no such signature.");
        }

        if (!string.Equals(manifest.SignerIdentifier, actor, StringComparison.Ordinal))
        {
            // Otherwise one person's signature would carry another person's transition, and
            // segregation of duties would be checked against an actor the signature never named.
            return SignatureCheckResult.Invalid(
                "it was made by someone other than the actor making this transition.");
        }

        // VersionRef carries the document identity's value; the platform mints into one
        // identifier system (ADR-015), so the value alone distinguishes documents.
        if (!string.Equals(manifest.Document.Value, version.DocumentIdentifier, StringComparison.Ordinal)
            || manifest.Version != version.Version)
        {
            return SignatureCheckResult.Invalid("it was made over a different version.");
        }

        if (!string.Equals(manifest.Meaning.ToString(), meaning, StringComparison.OrdinalIgnoreCase))
        {
            return SignatureCheckResult.Invalid(
                $"it was made to mean {manifest.Meaning}, and this gate requires {meaning}.");
        }

        return SignatureCheckResult.Valid;
    }
}
