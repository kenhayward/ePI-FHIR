using Xunit;

namespace Epi.Templates.Tests;

// Standard templates a deployment starts from (FN-TPL-004).
//   CAP-TPL-001 A versioned library of templates
//   CAP-TPL-012 Configurable policy on instantiation from non-approved templates
//
// ADR-042 decision 7. An adopting organisation gets QRD-shaped templates to work from rather
// than a blank page, and every one of them arrives as a draft - because seeding an approved
// template would be asserting a signature nobody gave.
//
// Most of what matters here is what seeding will not do: it does not approve, it does not
// overwrite, and it does not touch a template somebody has already taken responsibility for.
public sealed class TemplateSeedingTests
{
    private static string SeedDirectory() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", "..",
        "config", "templates", "seed"));

    [Fact]
    public async Task FN_TPL_004_a_fresh_deployment_starts_with_templates_to_work_from()
    {
        var store = new InMemoryTemplateStore();

        var seeded = (await TemplateSeeding.ApplyAsync(store, SeedDirectory())).Created;

        Assert.NotEmpty(seeded);
        Assert.Contains("qrd-package-leaflet", seeded);
        Assert.NotNull(await store.GetAsync("qrd-package-leaflet", 1));
    }

    [Fact]
    public async Task FN_TPL_004_seeding_names_what_was_already_there_as_well_as_what_it_made()
    {
        // Because a caller has to be able to tell the difference between "nothing to do" and
        // "nothing there". Start-up registers each seeded template with the lifecycle engine,
        // and a second start that was told only about creations could not tell a template it
        // had already registered from one whose registration never landed (ADR-043).
        var store = new InMemoryTemplateStore();
        await TemplateSeeding.ApplyAsync(store, SeedDirectory());

        var second = await TemplateSeeding.ApplyAsync(store, SeedDirectory());

        Assert.Empty(second.Created);
        Assert.Contains("qrd-package-leaflet", second.Seeded);
    }

    [Fact]
    public async Task FN_TPL_004_what_it_made_is_also_among_what_it_seeded()
    {
        // One list is a subset of the other rather than a partition of it, so a caller that
        // wants "every standard template" reads one field and never has to concatenate.
        var outcome = await TemplateSeeding.ApplyAsync(
            new InMemoryTemplateStore(), SeedDirectory());

        Assert.NotEmpty(outcome.Created);
        Assert.All(outcome.Created, created => Assert.Contains(created, outcome.Seeded));
    }

    [Fact]
    public async Task FN_TPL_004_every_seeded_template_says_it_is_not_approved()
    {
        // The name is what an approver reads, and what everybody else reads while it is waiting
        // to be approved. A seed that looked finished would be used as though it were.
        var store = new InMemoryTemplateStore();
        await TemplateSeeding.ApplyAsync(store, SeedDirectory());

        Assert.All(
            await store.ListAsync(),
            template => Assert.Contains("not approved", template.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FN_TPL_004_seeding_twice_does_not_make_a_second_version()
    {
        // A deployment restarts. If that minted a version each time, a template's history would
        // record changes nobody made.
        var store = new InMemoryTemplateStore();
        await TemplateSeeding.ApplyAsync(store, SeedDirectory());

        var second = await TemplateSeeding.ApplyAsync(store, SeedDirectory());

        Assert.Empty(second.Created);
        Assert.Equal([1], await store.VersionsAsync("qrd-package-leaflet"));
    }

    [Fact]
    public async Task FN_TPL_004_seeding_never_rewrites_a_template_somebody_changed()
    {
        // The one that matters most. A template already in the store belongs to whoever put it
        // there, and a seed reaching in to correct it would be changing what a patient reads
        // without anybody deciding to.
        var store = new InMemoryTemplateStore();
        await store.CreateAsync(new RenderTemplateDefinition(
            "qrd-package-leaflet", "Ours, approved last year", "body { color: black; }"));

        await TemplateSeeding.ApplyAsync(store, SeedDirectory());

        var kept = await store.GetAsync("qrd-package-leaflet", 1);
        Assert.Equal("Ours, approved last year", kept!.Name);
        Assert.Equal([1], await store.VersionsAsync("qrd-package-leaflet"));
    }

    [Fact]
    public async Task FN_TPL_004_a_seed_that_cannot_be_read_stops_the_seeding()
    {
        // Rather than seeding whichever files happened to parse. A deployment that started with
        // two of three standard templates would look complete and be missing one.
        var broken = Directory.CreateTempSubdirectory("epi-seed-").FullName;

        try
        {
            File.WriteAllText(Path.Combine(broken, "not-a-template.json"), "{ nonsense");

            await Assert.ThrowsAsync<InvalidTemplateException>(
                () => TemplateSeeding.ApplyAsync(new InMemoryTemplateStore(), broken));
        }
        finally
        {
            Directory.Delete(broken, recursive: true);
        }
    }

    [Fact]
    public async Task FN_TPL_004_a_deployment_that_wants_no_seeds_gets_none()
    {
        // An empty directory is a deployment that has chosen to author its own, which is a
        // legitimate thing to have chosen.
        var empty = Directory.CreateTempSubdirectory("epi-seed-empty-").FullName;

        try
        {
            Assert.Empty(
                (await TemplateSeeding.ApplyAsync(new InMemoryTemplateStore(), empty)).Created);
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    [Fact]
    public async Task FN_TPL_004_a_seed_directory_that_is_not_there_is_refused()
    {
        // Distinct from an empty one. A path that does not exist is a deployment configured
        // wrongly, and this platform has been bitten by that class three times.
        await Assert.ThrowsAsync<InvalidTemplateException>(
            () => TemplateSeeding.ApplyAsync(
                new InMemoryTemplateStore(), Path.Combine(Path.GetTempPath(), "no-such-seeds")));
    }

    [Fact]
    public async Task FN_TPL_004_the_shipped_seeds_are_all_usable()
    {
        // The one that fails if a seed is added with no stylesheet or no name - which the store
        // refuses, and which would otherwise only show up when somebody deployed.
        var store = new InMemoryTemplateStore();

        await TemplateSeeding.ApplyAsync(store, SeedDirectory());

        Assert.All(await store.ListAsync(), template =>
        {
            Assert.False(string.IsNullOrWhiteSpace(template.Stylesheet));
            Assert.False(string.IsNullOrWhiteSpace(template.Name));
        });
    }
}
