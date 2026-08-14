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
    /// <summary>Stores new content as version 1, under an identity the caller has minted.</summary>
    /// <remarks>
    /// The identity is supplied rather than invented here, so that a version can be registered
    /// under lifecycle management before its content exists (ADR-025 decision 1). It is also
    /// the honest arrangement: a document's identity is the platform's to mint (ADR-015), not
    /// the storage layer's to decide as a side effect of a write.
    /// </remarks>
    Task<EpiDocument> CreateAsync(
        DocumentIdentity identity, Bundle bundle, CancellationToken cancellationToken = default);

    /// <summary>Stores a specific next version of an existing document.</summary>
    /// <param name="version">
    /// The version the caller believes it is creating. Stated rather than assigned, for the same
    /// reason the identity is - and it turns two authors racing to create the same next version
    /// from a silent interleave into a refusal that names the conflict (ADR-025 decision 4).
    /// </param>
    /// <exception cref="VersionConflictException">If that version already exists.</exception>
    Task<EpiDocument> CreateVersionAsync(
        DocumentIdentity identity, int version, Bundle bundle,
        CancellationToken cancellationToken = default);

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
