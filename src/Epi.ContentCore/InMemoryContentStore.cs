using Hl7.Fhir.Model;

namespace Epi.ContentCore;

/// <summary>
/// An in-memory content store. Real persistence is <see cref="FhirRestContentStore"/>; this
/// exists so the domain can be exercised without a server, and is the reference implementation
/// the conformance suite holds every store to.
/// </summary>
public sealed class InMemoryContentStore : IContentStore
{
    private readonly Dictionary<string, SortedList<int, Bundle>> _documents = [];
    private readonly Lock _gate = new();

    public Task<EpiDocument> CreateAsync(Bundle bundle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ContentIdentity.RejectClaimedIdentity(bundle);

        var identity = ContentIdentity.Mint();

        lock (_gate)
        {
            _documents[identity.Value] = [];
            return Task.FromResult(Store(identity, 1, bundle));
        }
    }

    public Task<EpiDocument> CreateVersionAsync(
        DocumentIdentity identity, Bundle bundle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(bundle);

        lock (_gate)
        {
            if (!_documents.TryGetValue(identity.Value, out var versions))
            {
                throw new UnknownDocumentException(identity);
            }

            return Task.FromResult(Store(identity, versions.Count == 0 ? 1 : versions.Keys[^1] + 1, bundle));
        }
    }

    public Task<EpiDocument?> GetAsync(
        DocumentIdentity identity, int version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        lock (_gate)
        {
            if (_documents.TryGetValue(identity.Value, out var versions)
                && versions.TryGetValue(version, out var stored))
            {
                return Task.FromResult<EpiDocument?>(
                    new EpiDocument(identity, version, EpiBundleReader.Copy(stored)));
            }

            return Task.FromResult<EpiDocument?>(null);
        }
    }

    public Task<EpiDocument?> GetLatestAsync(
        DocumentIdentity identity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        lock (_gate)
        {
            if (_documents.TryGetValue(identity.Value, out var versions) && versions.Count > 0)
            {
                var latest = versions.Keys[^1];
                return Task.FromResult<EpiDocument?>(
                    new EpiDocument(identity, latest, EpiBundleReader.Copy(versions[latest])));
            }

            return Task.FromResult<EpiDocument?>(null);
        }
    }

    public Task<IReadOnlyList<int>> VersionsAsync(
        DocumentIdentity identity, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        lock (_gate)
        {
            IReadOnlyList<int> versions = _documents.TryGetValue(identity.Value, out var stored)
                ? [.. stored.Keys]
                : [];
            return Task.FromResult(versions);
        }
    }

    /// <summary>Every document identity the store holds. Used by tests asserting that a
    /// rejected write left nothing behind.</summary>
    public IReadOnlyCollection<string> KnownIdentities
    {
        get { lock (_gate) { return [.. _documents.Keys]; } }
    }

    private EpiDocument Store(DocumentIdentity identity, int version, Bundle bundle)
    {
        // Copy on the way in as well as on the way out: the caller keeps their instance and
        // may go on editing it.
        var snapshot = ContentIdentity.Stamp(EpiBundleReader.Copy(bundle), identity, version);

        _documents[identity.Value][version] = snapshot;
        return new EpiDocument(identity, version, EpiBundleReader.Copy(snapshot));
    }
}
