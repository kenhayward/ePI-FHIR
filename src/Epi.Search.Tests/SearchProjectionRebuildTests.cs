using Epi.ContentCore;
using Epi.Lifecycle;
using Xunit;

namespace Epi.Search.Tests;

// Rebuilding the search projection from the canonical stores (FN-SCH-004).
//   CAP-SCH-001 Search labels by market, status and identifier, scoped to the caller
//
// ADR-022 decision 6 says the projection is derived and never a source of truth, and its
// consequences said a rebuild path was owed. What that debt cost was measured only when the
// walkthrough began restarting the service: the index is in memory, so a restart empties it,
// and content still sitting in the FHIR server becomes unfindable - permanently, because
// nothing reprojects. Seventy-nine documents, zero results, and every surface that reaches
// content through search showing an empty platform.
//
// Derived means derivable. Everything here comes from the lifecycle store (which versions
// exist) and the content store (what they say), and nothing is invented for a version whose
// content is not there.
public sealed class SearchProjectionRebuildTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 9, 0, 0, TimeSpan.Zero);

    private static readonly IReadOnlyList<DocumentScope> Everywhere =
        [SearchFixtures.Uk, SearchFixtures.Eu];

    private sealed record Subject(
        SearchProjectionRebuild Rebuild,
        InMemoryLifecycleStore Lifecycle,
        InMemoryContentStore Content,
        InMemorySearchIndex Index);

    private static Subject Fresh()
    {
        var lifecycle = new InMemoryLifecycleStore();
        var content = new InMemoryContentStore();
        var index = new InMemorySearchIndex(IdentifierAuthority.Demonstration);

        return new Subject(
            new SearchProjectionRebuild(
                lifecycle, content, index, IdentifierAuthority.Demonstration),
            lifecycle, content, index);
    }

    /// <summary>A document written and registered, as an ordinary save would leave things.</summary>
    private static async Task WrittenAsync(
        Subject subject, string identifier, int version = 1, string? state = null)
    {
        var document = SearchFixtures.Document(identifier, version, SearchFixtures.Uk);
        var identity = new DocumentIdentity(
            IdentifierAuthority.Demonstration.DocumentSystem, identifier);

        if (version == 1)
        {
            await subject.Content.CreateAsync(identity, document.Bundle);
        }
        else
        {
            await subject.Content.CreateVersionAsync(identity, version, document.Bundle);
        }

        await subject.Lifecycle.RegisterAsync(
            new VersionRef(identifier, version), "user-anna", "draft", Now);

        if (state is not null)
        {
            await subject.Lifecycle.AppendAsync(new StateTransition(
                new VersionRef(identifier, version), "draft", state, "submit", "user-anna", Now));
        }
    }

    private static async Task<SearchResults> FindAsync(Subject subject, string identifier) =>
        await subject.Index.SearchAsync(new ScopedSearchQuery(
            new SearchCriteria(Identifier: identifier), Everywhere));

    [Fact]
    public async Task FN_SCH_004_a_version_written_before_the_projection_existed_is_findable()
    {
        // The restart, in miniature: content and lifecycle records that survived, an index that
        // did not.
        var subject = Fresh();
        await WrittenAsync(subject, "01a00000-0000-7000-8000-00000000a001");

        Assert.Equal(0, (await FindAsync(subject, "01a00000-0000-7000-8000-00000000a001")).Total);

        await subject.Rebuild.RunAsync();

        Assert.Equal(1, (await FindAsync(subject, "01a00000-0000-7000-8000-00000000a001")).Total);
    }

    [Fact]
    public async Task FN_SCH_004_a_rebuilt_version_carries_the_state_it_actually_reached()
    {
        // Not the state it started in. A version approved before the restart must come back
        // approved, or a search for what is approved answers with what was approved before the
        // last time anybody restarted the service.
        var subject = Fresh();
        await WrittenAsync(subject, "01a00000-0000-7000-8000-00000000a002", state: "in-review");

        await subject.Rebuild.RunAsync();

        var found = await subject.Index.SearchAsync(new ScopedSearchQuery(
            new SearchCriteria(State: "in-review"), Everywhere));

        Assert.Equal(1, found.Total);
    }

    [Fact]
    public async Task FN_SCH_004_every_version_of_a_document_is_rebuilt_not_only_the_latest()
    {
        // Search answers about versions, so a rebuild that projected only the current one would
        // quietly narrow what the platform can be asked.
        var subject = Fresh();
        await WrittenAsync(subject, "01a00000-0000-7000-8000-00000000a003");
        await WrittenAsync(subject, "01a00000-0000-7000-8000-00000000a003", version: 2);

        await subject.Rebuild.RunAsync();

        Assert.Equal(2, (await FindAsync(subject, "01a00000-0000-7000-8000-00000000a003")).Total);
    }

    [Fact]
    public async Task FN_SCH_004_a_registration_with_no_content_behind_it_is_not_invented()
    {
        // The inert registration ADR-025 accepts and FN-LCM-008 reports. There is nothing to
        // project: no title, no scope, no language. A hit with no scope is worse than no hit,
        // because scope is what keeps a result away from somebody who may not see it.
        var subject = Fresh();
        await subject.Lifecycle.RegisterAsync(
            new VersionRef("01a00000-0000-7000-8000-00000000a004", 1), "user-anna", "draft", Now);

        var report = await subject.Rebuild.RunAsync();

        Assert.Equal(0, (await FindAsync(subject, "01a00000-0000-7000-8000-00000000a004")).Total);
        Assert.Equal(1, report.WithoutContent);
    }

    [Fact]
    public async Task FN_SCH_004_a_template_is_not_projected_into_a_search_for_labels()
    {
        // The lifecycle engine manages render templates too (ADR-042 decision 3), and a
        // template is not a label. It has no scope to bound it, so a search that returned one
        // would be returning a result no permission decision could be made about.
        var subject = Fresh();
        await subject.Lifecycle.RegisterAsync(
            new VersionRef("qrd-package-leaflet", 1), "platform:template-seed", "draft", Now,
            RegisteredArtefact.Template);

        var report = await subject.Rebuild.RunAsync();

        Assert.Equal(0, (await FindAsync(subject, "qrd-package-leaflet")).Total);
        Assert.Equal(0, report.WithoutContent);
        Assert.Equal(0, report.Projected);
    }

    [Fact]
    public async Task FN_SCH_004_rebuilding_twice_leaves_one_of_each()
    {
        // It runs at every start-up, so running it against an index that is already correct has
        // to be a no-op rather than a doubling.
        var subject = Fresh();
        await WrittenAsync(subject, "01a00000-0000-7000-8000-00000000a005");

        await subject.Rebuild.RunAsync();
        await subject.Rebuild.RunAsync();

        Assert.Equal(1, (await FindAsync(subject, "01a00000-0000-7000-8000-00000000a005")).Total);
    }

    [Fact]
    public async Task FN_SCH_004_the_rebuild_says_what_it_did()
    {
        // An operator has to be able to tell "nothing to rebuild" from "rebuilt nothing", and
        // the second one is a broken deployment.
        var subject = Fresh();
        await WrittenAsync(subject, "01a00000-0000-7000-8000-00000000a006");
        await WrittenAsync(subject, "01a00000-0000-7000-8000-00000000a007");

        var report = await subject.Rebuild.RunAsync();

        Assert.Equal(2, report.Projected);
        Assert.Equal(0, report.WithoutContent);
    }

    [Fact]
    public async Task FN_SCH_004_a_rebuild_over_nothing_is_not_an_error()
    {
        // A deployment on its first start has nothing to rebuild, and that is not a problem to
        // report.
        var report = await Fresh().Rebuild.RunAsync();

        Assert.Equal(0, report.Projected);
        Assert.Equal(0, report.WithoutContent);
    }

    [Fact]
    public async Task FN_SCH_004_the_rebuild_writes_nothing_to_the_canonical_stores()
    {
        // Derived means derived. A rebuild that touched content or lifecycle records would be
        // a projection deciding something, which is the one thing ADR-022 decision 6 forbids.
        var subject = Fresh();
        await WrittenAsync(subject, "01a00000-0000-7000-8000-00000000a008");

        await subject.Rebuild.RunAsync();

        var identity = new DocumentIdentity(
            IdentifierAuthority.Demonstration.DocumentSystem,
            "01a00000-0000-7000-8000-00000000a008");

        Assert.Equal([1], await subject.Content.VersionsAsync(identity));
        Assert.Equal(
            "draft",
            await subject.Lifecycle.CurrentStateAsync(
                new VersionRef("01a00000-0000-7000-8000-00000000a008", 1)));
    }
}
