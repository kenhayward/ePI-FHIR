using Hl7.Fhir.Model;

namespace Epi.ContentCore;

/// <summary>
/// The canonical content store (capability 2). There is deliberately no update or delete:
/// content is immutable once stored, and a correction is a new version (CAP-LCM-002,
/// CAP-SCM-007). Immutability is a property of the interface, not a convention callers follow.
/// </summary>
public interface IContentStore
{
    /// <summary>Stores new content, minting its identity and creating version 1.</summary>
    EpiDocument Create(Bundle bundle);

    /// <summary>Stores the next version of an existing document.</summary>
    EpiDocument CreateVersion(DocumentIdentity identity, Bundle bundle);

    /// <summary>The document at a specific version, or null.</summary>
    EpiDocument? Get(DocumentIdentity identity, int version);

    /// <summary>The most recent version of a document, or null.</summary>
    EpiDocument? GetLatest(DocumentIdentity identity);

    /// <summary>Every version of a document, ascending.</summary>
    IReadOnlyList<int> Versions(DocumentIdentity identity);
}
