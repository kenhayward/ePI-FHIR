using Hl7.Fhir.Model;

namespace Epi.ContentCore;

/// <summary>
/// An in-memory content store. Real persistence is the FHIR REST adapter; this exists so the
/// domain can be exercised without a server, and is the reference implementation the
/// conformance suite holds every store to.
/// </summary>
public sealed class InMemoryContentStore : IContentStore
{
    private readonly Dictionary<string, SortedList<int, Bundle>> _documents = [];
    private readonly Lock _gate = new();

    public EpiDocument Create(Bundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        RejectClaimedIdentity(bundle);

        // ADR-015: opaque, time-ordered, and minted here rather than derived from content.
        var identity = new DocumentIdentity(
            ContentCoreDefaults.DocumentIdentifierSystem,
            Guid.CreateVersion7().ToString());

        lock (_gate)
        {
            _documents[identity.Value] = [];
            return Store(identity, 1, bundle);
        }
    }

    public EpiDocument CreateVersion(DocumentIdentity identity, Bundle bundle)
    {
        ArgumentNullException.ThrowIfNull(identity);
        ArgumentNullException.ThrowIfNull(bundle);

        lock (_gate)
        {
            if (!_documents.TryGetValue(identity.Value, out var versions))
            {
                throw new UnknownDocumentException(identity);
            }

            return Store(identity, versions.Count == 0 ? 1 : versions.Keys[^1] + 1, bundle);
        }
    }

    public EpiDocument? Get(DocumentIdentity identity, int version)
    {
        ArgumentNullException.ThrowIfNull(identity);

        lock (_gate)
        {
            if (_documents.TryGetValue(identity.Value, out var versions)
                && versions.TryGetValue(version, out var stored))
            {
                return new EpiDocument(identity, version, EpiBundleReader.Copy(stored));
            }

            return null;
        }
    }

    public EpiDocument? GetLatest(DocumentIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        lock (_gate)
        {
            if (_documents.TryGetValue(identity.Value, out var versions) && versions.Count > 0)
            {
                var latest = versions.Keys[^1];
                return new EpiDocument(identity, latest, EpiBundleReader.Copy(versions[latest]));
            }

            return null;
        }
    }

    public IReadOnlyList<int> Versions(DocumentIdentity identity)
    {
        ArgumentNullException.ThrowIfNull(identity);

        lock (_gate)
        {
            return _documents.TryGetValue(identity.Value, out var versions)
                ? [.. versions.Keys]
                : [];
        }
    }

    private EpiDocument Store(DocumentIdentity identity, int version, Bundle bundle)
    {
        // Copy on the way in as well as on the way out: the caller keeps their instance and
        // may go on editing it.
        var snapshot = EpiBundleReader.Copy(bundle);
        snapshot.Identifier = new Identifier(identity.System, identity.Value);

        _documents[identity.Value][version] = snapshot;
        return new EpiDocument(identity, version, EpiBundleReader.Copy(snapshot));
    }

    private static void RejectClaimedIdentity(Bundle bundle)
    {
        if (string.Equals(bundle.Identifier?.System,
                ContentCoreDefaults.DocumentIdentifierSystem, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidEpiBundleException([
                "Submitted content must not carry an identifier in the platform's own identifier "
                + "system: identity is minted by the platform (ADR-015). A legacy or external "
                + "identifier belongs in a secondary identifier with provenance to its source."
            ]);
        }
    }
}
