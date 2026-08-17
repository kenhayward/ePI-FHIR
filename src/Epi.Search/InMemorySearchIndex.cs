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
    private readonly Dictionary<VersionRef, Entry> _entries = [];
    private readonly Lock _gate = new();

    /// <param name="LastTouched">
    /// When this version was last written or moved. What the results are ordered by (ADR-045).
    /// </param>
    private sealed record Entry(
        SearchableContent Content, string State, DateTimeOffset LastTouched);

    public Task ProjectAsync(
        EpiDocument document, string state, DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);

        var content = SearchableContent.Of(document.Bundle, authority);
        var key = new VersionRef(document.Identity.Value, document.Version);

        lock (_gate)
        {
            // Keyed by version, so replaying what produced the projection converges rather than
            // accumulating. That is what makes a rebuild safe to run.
            _entries[key] = new Entry(content, state, at);
        }

        return Task.CompletedTask;
    }

    public Task ProjectStateAsync(
        VersionRef version, string state, DateTimeOffset at,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);

        lock (_gate)
        {
            // A version the projection has never seen is ignored rather than invented: a state
            // record carries no affiliate or market, and a hit with no scope is a hit every
            // caller matches.
            if (_entries.TryGetValue(version, out var entry))
            {
                // Moving a version is touching it: an author who has just submitted something
                // finds it where they left it rather than where it was created (ADR-045).
                _entries[version] = entry with { State = state, LastTouched = at };
            }
        }

        return Task.CompletedTask;
    }

    public Task<SearchResults> SearchAsync(
        ScopedSearchQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        var criteria = query.Criteria;

        // The predicate, not a filter applied afterwards. An empty permitted set matches
        // nothing, which is why this is a set membership test rather than an absent condition
        // (ADR-022 decision 3).
        var permitted = query.PermittedScopes.ToHashSet();

        List<KeyValuePair<VersionRef, Entry>> matched;
        lock (_gate)
        {
            matched =
            [
                .. _entries
                    .Where(e => permitted.Contains(e.Value.Content.Scope))
                    .Where(e => Matches(e.Key, e.Value, criteria))
                    // Most recently touched first, then a total tie-break so a caller paging
                    // through sees every version once (ADR-045). Ordering used to be identifier
                    // ascending, which is deterministic and useless: identifiers are
                    // time-ordered, so the oldest label in the corpus led every page.
                    .OrderByDescending(e => e.Value.LastTouched)
                    .ThenBy(e => e.Key.DocumentIdentifier, StringComparer.Ordinal)
                    .ThenByDescending(e => e.Key.Version),
            ];
        }

        var page = matched
            .Skip((criteria.Page - 1) * criteria.PageSize)
            .Take(criteria.PageSize)
            .Select(e => new SearchHit(
                e.Key.DocumentIdentifier,
                e.Key.Version,
                e.Value.Content.Title,
                e.Value.Content.Scope,
                e.Value.State,
                e.Value.Content.Language,
                e.Value.Content.Product,
                e.Value.Content.ProductIdentifier,
                e.Value.Content.DocumentType))
            .ToList();

        return Task.FromResult(new SearchResults(
            page, matched.Count, criteria.Page, criteria.PageSize));
    }

    private static bool Matches(VersionRef version, Entry entry, SearchCriteria criteria) =>
        Exact(criteria.Identifier, version.DocumentIdentifier)
        && Exact(criteria.Market, entry.Content.Scope.Market)
        && Exact(criteria.Language, entry.Content.Language)
        && Exact(criteria.State, entry.State)
        && Contains(criteria.Product, entry.Content.Product)

        // Exact, unlike the name beside it. An identifier that matched loosely would answer
        // PROD-1 for a query about PROD-10.
        && Exact(criteria.ProductIdentifier, entry.Content.ProductIdentifier)
        && Contains(criteria.Text, entry.Content.Text);

    /// <summary>An absent criterion narrows nothing; a present one must match exactly.</summary>
    private static bool Exact(string? criterion, string? value) =>
        string.IsNullOrWhiteSpace(criterion)
        || string.Equals(criterion, value, StringComparison.Ordinal);

    /// <summary>
    /// Free text and product are substring matches, case-insensitively. This is where a real
    /// index earns its place: no stemming, no tokenisation, no relevance ordering
    /// (CAP-SCH-003).
    /// </summary>
    private static bool Contains(string? criterion, string? value) =>
        string.IsNullOrWhiteSpace(criterion)
        || (value is not null && value.Contains(criterion, StringComparison.OrdinalIgnoreCase));
}
