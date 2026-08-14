using System.Security.Cryptography;
using System.Text;
using Epi.ContentCore;
using Hl7.Fhir.Model;

namespace Epi.Signature;

/// <summary>
/// The hash a signature is made over (ADR-020 decisions 4 and 5).
/// </summary>
public static class ContentHash
{
    /// <summary>Names the algorithm in the value, so a later change to it is visible.</summary>
    /// <remarks>
    /// A bare hex string is a hash computed by whatever the code said at the time. Signatures
    /// have to be verifiable long after the code has moved on, so the record carries how it was
    /// computed rather than relying on that being remembered.
    /// </remarks>
    private const string Algorithm = "sha-256";

    /// <summary>The hash of a version's canonical content, prefixed with the algorithm used.</summary>
    public static string Of(Bundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        // A copy, because the caller's bundle is on its way to being stored or returned to
        // someone. A hash function that stripped metadata from its argument would quietly
        // delete the server's own metadata as a side effect of being called.
        var canonical = (Bundle)bundle.DeepCopy();

        // The logical id, meta.versionId and meta.lastUpdated belong to whichever FHIR server
        // holds the content, and differ after a restore, a re-index, or a migration between
        // servers. Hashing them would make every historical signature unverifiable for reasons
        // that have nothing to do with the content. The platform's own identifier and version
        // tag stay, so the hash says which version was signed and not merely what it said.
        canonical.Id = null;
        if (canonical.Meta is not null)
        {
            canonical.Meta.VersionId = null;
            canonical.Meta.LastUpdated = null;
        }

        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(EpiBundleReader.Write(canonical)));
        return $"{Algorithm}:{Convert.ToHexStringLower(hash)}";
    }
}
