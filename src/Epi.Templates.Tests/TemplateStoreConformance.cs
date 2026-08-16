using Xunit;

namespace Epi.Templates.Tests;

/// <summary>
/// The behaviour every template store must exhibit, whatever backs it (FN-TPL-003).
/// </summary>
/// <remarks>
/// Shared source, so a durable store has to answer the same questions the same way. A template
/// determines what a patient reads (ADR-021, ADR-033 decision 2), so the rules here are the
/// rules content already has: a version is immutable, a change is a new version, and nothing
/// silently replaces anything.
/// </remarks>
public abstract class TemplateStoreConformance
{
    protected abstract Task<ITemplateStore> CreateStoreAsync();

    private static RenderTemplateDefinition Render(
        string identifier = "qrd-leaflet",
        string name = "EU QRD package leaflet",
        string stylesheet = "body { font-family: sans-serif; }") =>
        new(identifier, name, stylesheet);

    [Fact]
    public async Task FN_TPL_003_a_template_comes_back_as_it_went_in()
    {
        var store = await CreateStoreAsync();

        var stored = await store.CreateAsync(Render());

        var read = await store.GetAsync(stored.Identifier, stored.Version);
        Assert.NotNull(read);
        Assert.Equal("EU QRD package leaflet", read!.Name);
        Assert.Equal("body { font-family: sans-serif; }", read.Stylesheet);
    }

    [Fact]
    public async Task FN_TPL_003_the_first_version_of_a_template_is_version_one()
    {
        var store = await CreateStoreAsync();

        Assert.Equal(1, (await store.CreateAsync(Render())).Version);
    }

    [Fact]
    public async Task FN_TPL_003_a_change_is_a_new_version_rather_than_an_edit()
    {
        // The rule content already has. A render keyed to template version 2 must mean the same
        // thing in five years as it did when it was filed (ADR-033 decision 1).
        var store = await CreateStoreAsync();
        var first = await store.CreateAsync(Render(stylesheet: "body { color: black; }"));

        var second = await store.CreateVersionAsync(
            first.Identifier, Render(stylesheet: "body { color: navy; }"));

        Assert.Equal(2, second.Version);
        Assert.Equal(
            "body { color: black; }",
            (await store.GetAsync(first.Identifier, 1))!.Stylesheet);
    }

    [Fact]
    public async Task FN_TPL_003_every_version_of_a_template_is_listed_in_order()
    {
        var store = await CreateStoreAsync();
        var first = await store.CreateAsync(Render());
        await store.CreateVersionAsync(first.Identifier, Render(name: "Revised"));

        Assert.Equal([1, 2], await store.VersionsAsync(first.Identifier));
    }

    [Fact]
    public async Task FN_TPL_003_a_template_nobody_created_is_not_there()
    {
        var store = await CreateStoreAsync();

        Assert.Null(await store.GetAsync("no-such-template", 1));
        Assert.Empty(await store.VersionsAsync("no-such-template"));
    }

    [Fact]
    public async Task FN_TPL_003_creating_a_template_that_exists_is_refused()
    {
        // Silently becoming version 2 would let a second author replace what a first one
        // registered, and the refusal names which template it is.
        var store = await CreateStoreAsync();
        await store.CreateAsync(Render());

        var refusal = await Assert.ThrowsAsync<TemplateConflictException>(
            () => store.CreateAsync(Render()));

        Assert.Contains("qrd-leaflet", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FN_TPL_003_versioning_a_template_that_does_not_exist_is_refused()
    {
        // A new version of nothing is a template created by a route that skips whatever
        // creating one is supposed to do.
        var store = await CreateStoreAsync();

        await Assert.ThrowsAsync<TemplateConflictException>(
            () => store.CreateVersionAsync("no-such-template", Render()));
    }

    [Fact]
    public async Task FN_TPL_003_templates_are_listed_so_one_can_be_chosen()
    {
        // Nobody types a template identifier, for the same reason nobody types a section or a
        // product one (ADR-037 decision 3).
        var store = await CreateStoreAsync();
        await store.CreateAsync(Render("qrd-leaflet", "EU QRD package leaflet"));
        await store.CreateAsync(Render("qrd-smpc", "EU QRD summary of product characteristics"));

        var known = await store.ListAsync();

        Assert.Equal(["qrd-leaflet", "qrd-smpc"], known.Select(t => t.Identifier).Order());
    }

    [Fact]
    public async Task FN_TPL_003_a_template_with_no_stylesheet_at_all_is_refused()
    {
        // A render template that styles nothing produces a leaflet that looks like unstyled
        // markup, and somebody would have approved it without seeing that.
        var store = await CreateStoreAsync();

        await Assert.ThrowsAsync<ArgumentException>(
            () => store.CreateAsync(Render(stylesheet: "   ")));
    }

    [Fact]
    public async Task FN_TPL_003_a_template_with_no_name_is_refused()
    {
        // The name is what an approver reads when deciding whether to sign for it.
        var store = await CreateStoreAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => store.CreateAsync(Render(name: " ")));
    }
}

/// <summary>The in-memory store, held to the contract every template store must meet.</summary>
public sealed class InMemoryTemplateStoreConformanceTests : TemplateStoreConformance
{
    protected override Task<ITemplateStore> CreateStoreAsync() =>
        Task.FromResult<ITemplateStore>(new InMemoryTemplateStore());
}
