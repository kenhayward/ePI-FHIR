using Epi.ContentCore;

namespace Epi.Search;

/// <summary>
/// One version that matched. Metadata only: the content is fetched through the ordinary scoped
/// read path if the caller wants it (ADR-022 decision 6).
/// </summary>
/// <remarks>
/// A hit still discloses that a document exists, along with its title, market and state, so it
/// sits behind the same gate as the content it describes. Calling it "just metadata" is how
/// search becomes a way of enumerating a corpus nobody may read.
/// </remarks>
public sealed record SearchHit(
    string DocumentIdentifier,
    int Version,
    string Title,
    DocumentScope Scope,
    string State,
    string? Language = null,
    string? Product = null,
    string? ProductIdentifier = null,
    string? DocumentType = null);

/// <summary>
/// A page of results, and the true total within the caller's scope (ADR-022 decision 1).
/// </summary>
/// <remarks>
/// <paramref name="Total"/> counts what the caller may see, not what exists. A total taken
/// before scoping is the same disclosure as returning the documents themselves, spelled as a
/// number.
/// </remarks>
public sealed record SearchResults(
    IReadOnlyList<SearchHit> Hits, int Total, int Page, int PageSize)
{
    public static SearchResults Empty(SearchCriteria criteria) =>
        new([], 0, criteria.Page, criteria.PageSize);
}

/// <summary>The read side of capability 15 (FN-SCH-001).</summary>
public interface ILabelSearch
{
    /// <summary>
    /// The page of versions matching the criteria, within the permitted scopes and nowhere
    /// else.
    /// </summary>
    Task<SearchResults> SearchAsync(ScopedSearchQuery query, CancellationToken cancellationToken = default);
}
