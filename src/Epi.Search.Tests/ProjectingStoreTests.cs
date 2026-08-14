using Epi.ContentCore;
using Epi.Lifecycle;
using Xunit;

namespace Epi.Search.Tests;

// FN-SCH-001 Keeping the projection fed from the write paths (ADR-022 decision 6).
// A decorator, so no write path can forget: a document nobody can find is not an error anybody
// sees, which makes forgetting the worst kind of defect to rely on review for.
public sealed class ProjectingStoreTests
{
    private static readonly DateTimeOffset Registered =
        new(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task FN_SCH_001_stored_content_is_searchable_without_the_caller_indexing_it()
    {
        var index = new InMemorySearchIndex();
        var store = new ProjectingContentStore(new InMemoryContentStore(), index, "draft");

        var stored = await store.CreateAsync(
            ContentIdentity.Mint(), SearchFixtures.Document("ignored", 1, SearchFixtures.Uk).Bundle);

        var results = await index.SearchAsync(
            new ScopedSearchQuery(new SearchCriteria(), [SearchFixtures.Uk]));

        var hit = Assert.Single(results.Hits);
        Assert.Equal(stored.Identity.Value, hit.DocumentIdentifier);
        Assert.Equal("draft", hit.State);
    }

    [Fact]
    public async Task FN_SCH_001_a_new_version_is_searchable_alongside_the_one_it_follows()
    {
        var index = new InMemorySearchIndex();
        var store = new ProjectingContentStore(new InMemoryContentStore(), index, "draft");

        var first = await store.CreateAsync(
            ContentIdentity.Mint(), SearchFixtures.Document("ignored", 1, SearchFixtures.Uk).Bundle);
        await store.CreateVersionAsync(
            first.Identity, 2, SearchFixtures.Document("ignored", 2, SearchFixtures.Uk).Bundle);

        var results = await index.SearchAsync(
            new ScopedSearchQuery(new SearchCriteria(), [SearchFixtures.Uk]));

        Assert.Equal(2, results.Total);
    }

    [Fact]
    public async Task FN_SCH_001_a_registration_puts_the_version_in_its_initial_state()
    {
        var index = new InMemorySearchIndex();
        await index.ProjectAsync(SearchFixtures.Document("doc-1", 1, SearchFixtures.Uk), "unknown");
        var store = new ProjectingLifecycleStore(new InMemoryLifecycleStore(), index);

        await store.RegisterAsync(new VersionRef("doc-1", 1), "user-anna", "draft", Registered);

        Assert.Single((await index.SearchAsync(new ScopedSearchQuery(
            new SearchCriteria(State: "draft"), [SearchFixtures.Uk]))).Hits);
    }

    [Fact]
    public async Task FN_SCH_001_a_recorded_transition_reaches_the_projection()
    {
        var index = new InMemorySearchIndex();
        await index.ProjectAsync(SearchFixtures.Document("doc-1", 1, SearchFixtures.Uk), "draft");
        var inner = new InMemoryLifecycleStore();
        var store = new ProjectingLifecycleStore(inner, index);
        await store.RegisterAsync(new VersionRef("doc-1", 1), "user-anna", "draft", Registered);

        await store.AppendAsync(new StateTransition(
            new VersionRef("doc-1", 1), "draft", "in-review", "submit", "user-anna",
            new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero)));

        Assert.Single((await index.SearchAsync(new ScopedSearchQuery(
            new SearchCriteria(State: "in-review"), [SearchFixtures.Uk]))).Hits);

        // The decorated store is still the store: the record it exists to keep is unaffected.
        Assert.Equal("in-review", await store.CurrentStateAsync(new VersionRef("doc-1", 1)));
        Assert.Equal("user-anna", await inner.AuthorOfAsync(new VersionRef("doc-1", 1)));
    }
}
