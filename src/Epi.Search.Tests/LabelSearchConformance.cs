using Epi.ContentCore;
using Epi.Lifecycle;
using Xunit;

namespace Epi.Search.Tests;

/// <summary>
/// The behaviour every search implementation must exhibit, whatever backs it (FN-SCH-001).
/// </summary>
/// <remarks>
/// Shared source, so the dedicated index that replaces the in-memory one later has to answer
/// the same questions the same way. The scoping assertions are the ones worth having twice: a
/// search engine has its own query language, its own defaults, and its own way of turning an
/// empty filter into a match-all, and none of that is visible from the port.
/// </remarks>
public abstract class LabelSearchConformance
{
    /// <summary>A projection and the search that reads it, both empty.</summary>
    protected abstract Task<(ISearchProjection Projection, ILabelSearch Search)> CreateAsync();

    /// <summary>The moment everything is projected at unless a case cares when.</summary>
    private static readonly DateTimeOffset Moment = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Projecting at a moment, which every implementation now needs (FN-SCH-005): results come
    /// back most-recently-touched first, so when is part of what is projected rather than
    /// something the index decides for itself.
    /// </summary>
    private static Task Project(
        ISearchProjection projection, EpiDocument document, string state,
        DateTimeOffset? at = null) =>
        projection.ProjectAsync(document, state, at ?? Moment);

    private static Task ProjectState(
        ISearchProjection projection, VersionRef version, string state,
        DateTimeOffset? at = null) =>
        projection.ProjectStateAsync(version, state, at ?? Moment);

    private static ScopedSearchQuery Query(
        SearchCriteria criteria, params DocumentScope[] permitted) => new(criteria, permitted);

    private static ScopedSearchQuery Everything(params DocumentScope[] permitted) =>
        Query(new SearchCriteria(), permitted);

    [Fact]
    public async Task FN_SCH_001_a_query_returns_the_versions_within_the_permitted_scopes()
    {
        var (projection, search) = await CreateAsync();
        await Project(projection, SearchFixtures.Document("doc-1", 1, SearchFixtures.Uk), "draft");

        var results = await search.SearchAsync(Everything(SearchFixtures.Uk));

        var hit = Assert.Single(results.Hits);
        Assert.Equal("doc-1", hit.DocumentIdentifier);
        Assert.Equal(1, hit.Version);
        Assert.Equal("draft", hit.State);
        Assert.Equal(SearchFixtures.Uk, hit.Scope);
    }

    [Fact]
    public async Task CAP_SCH_004_content_outside_the_permitted_scopes_is_invisible()
    {
        var (projection, search) = await CreateAsync();
        await Project(projection, SearchFixtures.Document("doc-1", 1, SearchFixtures.Uk), "draft");
        await Project(projection, SearchFixtures.Document("doc-2", 1, SearchFixtures.Eu), "draft");

        var results = await search.SearchAsync(Everything(SearchFixtures.Uk));

        Assert.Equal("doc-1", Assert.Single(results.Hits).DocumentIdentifier);

        // The count as well as the page. A total taken before scoping is the same disclosure
        // as returning the document, spelled as a number (ADR-022 decision 1).
        Assert.Equal(1, results.Total);
    }

    [Fact]
    public async Task CAP_SCH_004_an_empty_permitted_scope_set_returns_nothing_not_everything()
    {
        // The way this class of code fails: an empty collection rendered into a query becomes
        // an absent predicate, and an absent predicate matches the corpus (ADR-022 decision 3).
        var (projection, search) = await CreateAsync();
        await Project(projection, SearchFixtures.Document("doc-1", 1, SearchFixtures.Uk), "draft");
        await Project(projection, SearchFixtures.Document("doc-2", 1, SearchFixtures.Eu), "draft");

        var results = await search.SearchAsync(Everything());

        Assert.Empty(results.Hits);
        Assert.Equal(0, results.Total);
    }

    [Fact]
    public async Task CAP_SCH_004_a_market_filter_cannot_reach_outside_the_permitted_scopes()
    {
        // Asking for a market the caller may not see returns nothing, rather than the market's
        // content. A filter is a narrowing of what is permitted, never a way of naming
        // something else.
        var (projection, search) = await CreateAsync();
        await Project(projection, SearchFixtures.Document("doc-2", 1, SearchFixtures.Eu), "draft");

        var results = await search.SearchAsync(
            Query(new SearchCriteria(Market: "EU"), SearchFixtures.Uk));

        Assert.Empty(results.Hits);
        Assert.Equal(0, results.Total);
    }

    [Fact]
    public async Task FN_SCH_001_results_can_be_narrowed_by_market_within_scope()
    {
        var (projection, search) = await CreateAsync();
        await Project(projection, SearchFixtures.Document("doc-1", 1, SearchFixtures.Uk), "draft");
        await Project(projection, SearchFixtures.Document("doc-2", 1, SearchFixtures.Eu), "draft");

        var results = await search.SearchAsync(
            Query(new SearchCriteria(Market: "EU"), SearchFixtures.Uk, SearchFixtures.Eu));

        Assert.Equal("doc-2", Assert.Single(results.Hits).DocumentIdentifier);
    }

    [Fact]
    public async Task FN_SCH_001_results_can_be_narrowed_by_state()
    {
        // "Which labels are awaiting approval in my market" is the first question anyone asks
        // of a system like this (iteration-2 Section 4.3).
        var (projection, search) = await CreateAsync();
        await Project(projection, SearchFixtures.Document("doc-1", 1, SearchFixtures.Uk), "draft");
        await Project(projection, SearchFixtures.Document("doc-2", 1, SearchFixtures.Uk), "in-review");

        var results = await search.SearchAsync(
            Query(new SearchCriteria(State: "in-review"), SearchFixtures.Uk));

        Assert.Equal("doc-2", Assert.Single(results.Hits).DocumentIdentifier);
    }

    [Fact]
    public async Task FN_SCH_001_results_can_be_narrowed_by_language_product_and_identifier()
    {
        var (projection, search) = await CreateAsync();
        await Project(projection, 
            SearchFixtures.Document("doc-1", 1, SearchFixtures.Uk, language: "en-GB"), "draft");
        await Project(projection, 
            SearchFixtures.Document("doc-2", 1, SearchFixtures.Uk, language: "cy-GB",
                product: "Placebolol 5 mg capsules"), "draft");

        Assert.Equal("doc-2", Assert.Single(
            (await search.SearchAsync(Query(new SearchCriteria(Language: "cy-GB"), SearchFixtures.Uk))).Hits)
            .DocumentIdentifier);

        Assert.Equal("doc-2", Assert.Single(
            (await search.SearchAsync(Query(new SearchCriteria(Product: "Placebolol"), SearchFixtures.Uk))).Hits)
            .DocumentIdentifier);

        Assert.Equal("doc-1", Assert.Single(
            (await search.SearchAsync(Query(new SearchCriteria(Identifier: "doc-1"), SearchFixtures.Uk))).Hits)
            .DocumentIdentifier);
    }

    [Fact]
    public async Task CAP_SCH_003_free_text_matches_the_title_and_the_section_narrative()
    {
        var (projection, search) = await CreateAsync();
        await Project(projection, 
            SearchFixtures.Document("doc-1", 1, SearchFixtures.Uk, title: "SYNTHETIC - Examplinum"), "draft");
        await Project(projection, 
            SearchFixtures.Document("doc-2", 1, SearchFixtures.Uk, title: "SYNTHETIC - Placebolol",
                narrative: "Contains a synthetic excipient called invented-lactose."), "draft");

        Assert.Equal("doc-2", Assert.Single(
            (await search.SearchAsync(Query(new SearchCriteria(Text: "Placebolol"), SearchFixtures.Uk))).Hits)
            .DocumentIdentifier);

        Assert.Equal("doc-2", Assert.Single(
            (await search.SearchAsync(
                Query(new SearchCriteria(Text: "invented-lactose"), SearchFixtures.Uk))).Hits)
            .DocumentIdentifier);
    }

    [Fact]
    public async Task FN_SCH_001_every_version_is_searchable_not_only_the_latest()
    {
        // A regulated corpus is asked about its history at least as often as about its present,
        // and a version that cannot be found cannot be produced on request (CAP-LCM-006).
        var (projection, search) = await CreateAsync();
        await Project(projection, SearchFixtures.Document("doc-1", 1, SearchFixtures.Uk), "superseded");
        await Project(projection, SearchFixtures.Document("doc-1", 2, SearchFixtures.Uk), "approved");

        var results = await search.SearchAsync(
            Query(new SearchCriteria(Identifier: "doc-1"), SearchFixtures.Uk));

        Assert.Equal(2, results.Total);
        Assert.Equal([2, 1], results.Hits.Select(h => h.Version));
    }

    [Fact]
    public async Task FN_SCH_001_a_recorded_transition_changes_what_a_state_query_returns()
    {
        var (projection, search) = await CreateAsync();
        await Project(projection, SearchFixtures.Document("doc-1", 1, SearchFixtures.Uk), "draft");

        await ProjectState(projection, new VersionRef("doc-1", 1), "in-review");

        Assert.Empty((await search.SearchAsync(
            Query(new SearchCriteria(State: "draft"), SearchFixtures.Uk))).Hits);
        Assert.Single((await search.SearchAsync(
            Query(new SearchCriteria(State: "in-review"), SearchFixtures.Uk))).Hits);
    }

    [Fact]
    public async Task FN_SCH_001_a_state_change_for_unknown_content_creates_no_hit()
    {
        // A state record carries no affiliate or market of its own. Inventing a hit from one
        // would put a result in the index with no scope to filter it by, and a hit with no
        // scope is a hit every caller matches.
        var (projection, search) = await CreateAsync();

        await ProjectState(projection, new VersionRef("never-stored", 1), "approved");

        Assert.Empty((await search.SearchAsync(Everything(SearchFixtures.Uk))).Hits);
    }

    [Fact]
    public async Task CAP_SCH_006_results_are_paged_with_a_true_total_and_a_stable_order()
    {
        var (projection, search) = await CreateAsync();
        for (var version = 1; version <= 5; version++)
        {
            await Project(projection, 
                SearchFixtures.Document("doc-1", version, SearchFixtures.Uk), "draft");
        }

        var first = await search.SearchAsync(
            Query(new SearchCriteria(PageSize: 2), SearchFixtures.Uk));
        var second = await search.SearchAsync(
            Query(new SearchCriteria(Page: 2, PageSize: 2), SearchFixtures.Uk));
        var third = await search.SearchAsync(
            Query(new SearchCriteria(Page: 3, PageSize: 2), SearchFixtures.Uk));

        Assert.All([first, second, third], page => Assert.Equal(5, page.Total));
        Assert.Equal([5, 4], first.Hits.Select(h => h.Version));
        Assert.Equal([3, 2], second.Hits.Select(h => h.Version));
        Assert.Equal([1], third.Hits.Select(h => h.Version));
    }

    [Fact]
    public async Task CAP_SCH_006_a_page_beyond_the_last_is_empty_rather_than_wrapping()
    {
        var (projection, search) = await CreateAsync();
        await Project(projection, SearchFixtures.Document("doc-1", 1, SearchFixtures.Uk), "draft");

        var results = await search.SearchAsync(
            Query(new SearchCriteria(Page: 9), SearchFixtures.Uk));

        Assert.Empty(results.Hits);
        Assert.Equal(1, results.Total);
    }

    [Fact]
    public async Task FN_SCH_005_the_most_recently_touched_version_comes_first()
    {
        // Ordering was identifier-ascending, which is deterministic and useless: identifiers are
        // time-ordered UUIDs, so the oldest label in the corpus led every page and the one an
        // author had just saved was on the last one. A picker shows the first twenty.
        var (projection, search) = await CreateAsync();
        await Project(projection, SearchFixtures.Document("doc-1", 1, SearchFixtures.Uk), "draft",
            Moment);
        await Project(projection, SearchFixtures.Document("doc-2", 1, SearchFixtures.Uk), "draft",
            Moment.AddHours(2));

        var results = await search.SearchAsync(Everything(SearchFixtures.Uk));

        Assert.Equal(["doc-2", "doc-1"], results.Hits.Select(h => h.DocumentIdentifier));
    }

    [Fact]
    public async Task FN_SCH_005_a_state_change_moves_a_version_to_the_front()
    {
        // Submitting a label is touching it. An author who has just sent something for review
        // should find it where they last left it rather than where it was created.
        var (projection, search) = await CreateAsync();
        await Project(projection, SearchFixtures.Document("doc-1", 1, SearchFixtures.Uk), "draft",
            Moment.AddHours(2));
        await Project(projection, SearchFixtures.Document("doc-2", 1, SearchFixtures.Uk), "draft",
            Moment);

        // doc-2 rather than doc-1, so that "moved most recently" and "first by identifier"
        // disagree. Asserting an order that both rules produce proves neither.
        await ProjectState(projection, new VersionRef("doc-2", 1), "in-review",
            Moment.AddHours(5));

        var results = await search.SearchAsync(Everything(SearchFixtures.Uk));

        Assert.Equal(["doc-2", "doc-1"], results.Hits.Select(h => h.DocumentIdentifier));
    }

    [Fact]
    public async Task FN_SCH_005_versions_touched_at_the_same_moment_still_have_an_order()
    {
        // A rebuild projects a whole corpus, and content written in one batch shares a moment.
        // Without a total order the page boundary is whatever the store felt like, so a caller
        // paging through can see one version twice and another never.
        var (projection, search) = await CreateAsync();
        await Project(projection, SearchFixtures.Document("doc-2", 1, SearchFixtures.Uk), "draft");
        await Project(projection, SearchFixtures.Document("doc-1", 2, SearchFixtures.Uk), "draft");
        await Project(projection, SearchFixtures.Document("doc-1", 1, SearchFixtures.Uk), "draft");

        var results = await search.SearchAsync(Everything(SearchFixtures.Uk));

        Assert.Equal(
            [("doc-1", 2), ("doc-1", 1), ("doc-2", 1)],
            results.Hits.Select(h => (h.DocumentIdentifier, h.Version)));
    }

    [Fact]
    public async Task CAP_SCH_006_paging_through_sees_every_version_once()
    {
        // The consequence of the order being total, asserted as a caller experiences it.
        var (projection, search) = await CreateAsync();
        for (var n = 1; n <= 5; n++)
        {
            await Project(projection, SearchFixtures.Document($"doc-{n}", 1, SearchFixtures.Uk),
                "draft", Moment.AddMinutes(n));
        }

        var first = await search.SearchAsync(
            Query(new SearchCriteria(Page: 1, PageSize: 2), SearchFixtures.Uk));
        var second = await search.SearchAsync(
            Query(new SearchCriteria(Page: 2, PageSize: 2), SearchFixtures.Uk));
        var third = await search.SearchAsync(
            Query(new SearchCriteria(Page: 3, PageSize: 2), SearchFixtures.Uk));

        var seen = first.Hits.Concat(second.Hits).Concat(third.Hits)
            .Select(h => h.DocumentIdentifier).ToList();

        Assert.Equal(["doc-5", "doc-4", "doc-3", "doc-2", "doc-1"], seen);
    }

    [Fact]
    public async Task FN_SCH_001_a_reprojected_version_is_recorded_once_not_twice()
    {
        // The projection is derived, so replaying what produced it must converge rather than
        // accumulate - which is what makes a rebuild safe to run (ADR-022 decision 6).
        var (projection, search) = await CreateAsync();
        await Project(projection, SearchFixtures.Document("doc-1", 1, SearchFixtures.Uk), "draft");
        await Project(projection, SearchFixtures.Document("doc-1", 1, SearchFixtures.Uk), "draft");

        Assert.Equal(1, (await search.SearchAsync(Everything(SearchFixtures.Uk))).Total);
    }
}
