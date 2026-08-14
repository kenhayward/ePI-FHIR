using Epi.ContentCore;
using Epi.Lifecycle;

namespace Epi.Search;

/// <summary>
/// An in-memory search projection. The reference implementation the conformance suite holds
/// every search implementation to; the dedicated index (OpenSearch, D3 Section 12) is a later
/// implementation of the same two ports.
/// </summary>
public sealed class InMemorySearchIndex(IdentifierAuthority? authority = null)
    : ISearchProjection, ILabelSearch
{
    public Task ProjectAsync(
        EpiDocument document, string state, CancellationToken cancellationToken = default)
    {
        _ = authority;
        throw new NotImplementedException();
    }

    public Task ProjectStateAsync(
        VersionRef version, string state, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<SearchResults> SearchAsync(
        ScopedSearchQuery query, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
