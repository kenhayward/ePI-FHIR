using Hl7.Fhir.Model;
using Hl7.Fhir.Rest;

namespace Epi.ContentCore;

/// <summary>
/// The canonical content store backed by a FHIR server, reached only through its REST API
/// (D3 Section 2.3 makes that API the boundary; FN-CC-004).
/// </summary>
/// <remarks>
/// Nothing here depends on a particular server product. Identity is our own business
/// identifier and the version is our own tag, so the server's logical id and meta.versionId
/// are implementation details we never store or quote - which is what keeps ADR-003
/// reversible while the mandated-vs-open component table is still unconfirmed.
/// </remarks>
public sealed class FhirRestContentStore(FhirClient client) : IContentStore
{
    private readonly FhirClient _client = client ?? throw new ArgumentNullException(nameof(client));

    public async Task<EpiDocument> CreateAsync(
        Bundle bundle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ContentIdentity.RejectClaimedIdentity(bundle);

        var identity = ContentIdentity.Mint();
        return await StoreAsync(identity, 1, bundle, cancellationToken);
    }

    public async Task<EpiDocument> CreateVersionAsync(
        DocumentIdentity identity, Bundle bundle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(bundle);

        var existing = await VersionsAsync(identity, cancellationToken);
        if (existing.Count == 0)
        {
            throw new UnknownDocumentException(identity);
        }

        return await StoreAsync(identity, existing[^1] + 1, bundle, cancellationToken);
    }

    public async Task<EpiDocument?> GetAsync(
        DocumentIdentity identity, int version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var stored = await FindAsync(identity, cancellationToken);
        var match = stored.FirstOrDefault(s => ContentIdentity.VersionOf(s) == version);
        return match is null ? null : new EpiDocument(identity, version, match);
    }

    public async Task<EpiDocument?> GetLatestAsync(
        DocumentIdentity identity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var stored = await FindAsync(identity, cancellationToken);
        var latest = stored
            .Select(s => (Bundle: s, Version: ContentIdentity.VersionOf(s)))
            .Where(s => s.Version is not null)
            .OrderByDescending(s => s.Version)
            .FirstOrDefault();

        return latest.Bundle is null ? null : new EpiDocument(identity, latest.Version!.Value, latest.Bundle);
    }

    public async Task<IReadOnlyList<int>> VersionsAsync(
        DocumentIdentity identity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        var stored = await FindAsync(identity, cancellationToken);
        return [.. stored.Select(b => ContentIdentity.VersionOf(b)).Where(v => v is not null).Select(v => v!.Value).Order()];
    }

    private async Task<EpiDocument> StoreAsync(
        DocumentIdentity identity, int version, Bundle bundle, CancellationToken cancellationToken)
    {
        var snapshot = ContentIdentity.Stamp(EpiBundleReader.Copy(bundle), identity, version);

        var created = await _client.CreateAsync(snapshot, cancellationToken)
            ?? throw new InvalidOperationException(
                "The FHIR server accepted the document but returned no content.");

        return new EpiDocument(identity, version, created);
    }

    /// <summary>Every stored version of a document, found by its business identifier.</summary>
    private async Task<IReadOnlyList<Bundle>> FindAsync(
        DocumentIdentity identity, CancellationToken cancellationToken)
    {
        var parameters = new SearchParams()
            .Where($"identifier={identity.System}|{identity.Value}");

        var results = await _client.SearchAsync<Bundle>(parameters, ct: cancellationToken);

        var found = new List<Bundle>();
        while (results is not null)
        {
            found.AddRange(results.Entry
                .Select(e => e.Resource)
                .OfType<Bundle>()
                // A search result is itself a Bundle; take only the stored documents, which
                // are the ones carrying our identifier.
                .Where(b => b.Identifier?.Value == identity.Value));

            results = await _client.ContinueAsync(results, ct: cancellationToken);
        }

        return found;
    }
}
