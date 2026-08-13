using Epi.ContentCore;
using Hl7.Fhir.Model;

namespace Epi.Validation;

/// <summary>
/// The write gate (CAP-VAL-004): content is validated before it reaches the store, so
/// rejected content leaves no trace behind it.
/// </summary>
public sealed class ValidatingContentStore(IContentStore inner, StructuralValidator validator) : IContentStore
{
    private readonly StructuralValidator _validator = validator;

    public Task<EpiDocument> CreateAsync(Bundle bundle, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<EpiDocument> CreateVersionAsync(DocumentIdentity identity, Bundle bundle, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<EpiDocument?> GetAsync(DocumentIdentity identity, int version, CancellationToken cancellationToken = default) =>
        inner.GetAsync(identity, version, cancellationToken);

    public Task<EpiDocument?> GetLatestAsync(DocumentIdentity identity, CancellationToken cancellationToken = default) =>
        inner.GetLatestAsync(identity, cancellationToken);

    public Task<IReadOnlyList<int>> VersionsAsync(DocumentIdentity identity, CancellationToken cancellationToken = default) =>
        inner.VersionsAsync(identity, cancellationToken);
}
