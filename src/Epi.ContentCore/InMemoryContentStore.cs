using Hl7.Fhir.Model;

namespace Epi.ContentCore;

/// <summary>
/// An in-memory content store. Real persistence is the FHIR REST adapter; this exists so the
/// domain can be exercised without a server, and is the reference implementation the
/// conformance suite holds every store to.
/// </summary>
public sealed class InMemoryContentStore : IContentStore
{
    public EpiDocument Create(Bundle bundle) => throw new NotImplementedException();

    public EpiDocument CreateVersion(DocumentIdentity identity, Bundle bundle) =>
        throw new NotImplementedException();

    public EpiDocument? Get(DocumentIdentity identity, int version) => throw new NotImplementedException();

    public EpiDocument? GetLatest(DocumentIdentity identity) => throw new NotImplementedException();

    public IReadOnlyList<int> Versions(DocumentIdentity identity) => throw new NotImplementedException();
}
