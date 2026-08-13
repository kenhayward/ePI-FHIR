using Hl7.Fhir.Model;

namespace Epi.ContentCore;

/// <summary>
/// The canonical content store (capability 2). There is deliberately no update or delete:
/// content is immutable once stored, and a correction is a new version (CAP-LCM-002,
/// CAP-SCM-007). Immutability is a property of the interface, not a convention callers follow.
/// </summary>
/// <remarks>
/// Asynchronous because the real implementation talks to a FHIR server over HTTP. A
/// synchronous facade over that would be a deadlock waiting to happen under load.
/// </remarks>
public interface IContentStore
{
    /// <summary>Stores new content, minting its identity and creating version 1.</summary>
    Task<EpiDocument> CreateAsync(Bundle bundle, CancellationToken cancellationToken = default);

    /// <summary>Stores the next version of an existing document.</summary>
    Task<EpiDocument> CreateVersionAsync(
        DocumentIdentity identity, Bundle bundle, CancellationToken cancellationToken = default);

    /// <summary>The document at a specific version, or null.</summary>
    Task<EpiDocument?> GetAsync(
        DocumentIdentity identity, int version, CancellationToken cancellationToken = default);

    /// <summary>The most recent version of a document, or null.</summary>
    Task<EpiDocument?> GetLatestAsync(
        DocumentIdentity identity, CancellationToken cancellationToken = default);

    /// <summary>Every version of a document, ascending.</summary>
    Task<IReadOnlyList<int>> VersionsAsync(
        DocumentIdentity identity, CancellationToken cancellationToken = default);
}
