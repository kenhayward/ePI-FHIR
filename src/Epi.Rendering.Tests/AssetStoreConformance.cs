using System.Text;
using Xunit;

namespace Epi.Rendering.Tests;

/// <summary>
/// The behaviour every asset store must exhibit, whatever backs it (CAP-RND-002).
/// </summary>
/// <remarks>
/// Shared source, so the MinIO implementation has to answer the same questions the same way.
/// The write-once cases are the ones worth having twice: an object store gets that from
/// object-lock rather than from a check in application code, and the two can disagree.
/// </remarks>
public abstract class AssetStoreConformance
{
    protected abstract Task<IAssetStore> CreateStoreAsync();

    private static RenderedDocument Rendered(
        int labelVersion = 3, int templateVersion = 2, bool draft = false,
        string mediaType = "application/pdf", string body = "%PDF-1.4 synthetic") =>
        new(mediaType,
            Encoding.ASCII.GetBytes(body),
            new DocumentIdentityRef(
                "https://epi.example.org/identifier/document",
                "01a00000-0000-7000-8000-00000000000a"),
            labelVersion,
            "qrd-leaflet",
            templateVersion,
            draft);

    private static ArtworkDocument Artwork(string reference = "JOB-2026-0114") =>
        new("application/pdf", Encoding.ASCII.GetBytes("%PDF-1.4 artwork"), "Agency Ltd", reference);

    [Fact]
    public async Task CAP_RND_002_an_artefact_comes_back_as_it_went_in()
    {
        var store = await CreateStoreAsync();
        var rendered = Rendered();

        await store.PutAsync(AssetKey.For(rendered), rendered);

        var read = await store.GetAsync(AssetKey.For(rendered));
        Assert.NotNull(read);
        Assert.Equal(rendered.Content, read!.Content);
        Assert.Equal("application/pdf", read.MediaType);
    }

    [Fact]
    public async Task CAP_RND_002_nothing_is_at_a_key_nothing_was_stored_under()
    {
        var store = await CreateStoreAsync();

        Assert.Null(await store.GetAsync(AssetKey.For(Rendered())));
    }

    [Fact]
    public async Task CAP_RND_002_the_store_is_write_once()
    {
        // An artefact that could be replaced is one nobody can cite: a render that changed
        // after being filed no longer matches what was filed against it.
        var store = await CreateStoreAsync();
        var first = Rendered(body: "%PDF-1.4 the one that was filed");
        await store.PutAsync(AssetKey.For(first), first);

        await Assert.ThrowsAsync<AssetAlreadyStoredException>(
            () => store.PutAsync(AssetKey.For(first), Rendered(body: "%PDF-1.4 something else")));

        var read = await store.GetAsync(AssetKey.For(first));
        Assert.Equal(first.Content, read!.Content);
    }

    [Fact]
    public async Task CAP_RND_002_a_render_is_keyed_by_both_versions_that_produced_it()
    {
        // Both are inputs to the bytes, so a key naming only the label version would collide
        // the moment a template was revised (ADR-033 decision 1).
        var store = await CreateStoreAsync();
        var withTemplate2 = Rendered(templateVersion: 2, body: "%PDF-1.4 template two");
        var withTemplate3 = Rendered(templateVersion: 3, body: "%PDF-1.4 template three");

        await store.PutAsync(AssetKey.For(withTemplate2), withTemplate2);
        await store.PutAsync(AssetKey.For(withTemplate3), withTemplate3);

        Assert.Equal(
            withTemplate2.Content, (await store.GetAsync(AssetKey.For(withTemplate2)))!.Content);
        Assert.Equal(
            withTemplate3.Content, (await store.GetAsync(AssetKey.For(withTemplate3)))!.Content);
    }

    [Fact]
    public async Task CAP_RND_002_a_draft_render_does_not_displace_the_official_one()
    {
        var store = await CreateStoreAsync();
        var official = Rendered(draft: false, body: "%PDF-1.4 approved");
        var preview = Rendered(draft: true, body: "%PDF-1.4 preview");

        await store.PutAsync(AssetKey.For(official), official);
        await store.PutAsync(AssetKey.For(preview), preview);

        Assert.Equal(official.Content, (await store.GetAsync(AssetKey.For(official)))!.Content);
    }

    [Fact]
    public async Task CAP_RND_002_html_and_pdf_of_one_version_are_kept_apart()
    {
        var store = await CreateStoreAsync();
        var html = Rendered(mediaType: "text/html; charset=utf-8", body: "<html></html>");
        var pdf = Rendered(body: "%PDF-1.4 synthetic");

        await store.PutAsync(AssetKey.For(html), html);
        await store.PutAsync(AssetKey.For(pdf), pdf);

        Assert.Equal(html.Content, (await store.GetAsync(AssetKey.For(html)))!.Content);
        Assert.Equal(pdf.Content, (await store.GetAsync(AssetKey.For(pdf)))!.Content);
    }

    [Fact]
    public async Task CAP_RND_002_a_listing_of_one_lineage_never_returns_the_other()
    {
        // Acceptance criterion 9 in the store rather than only in the type system. Anything
        // asking for renders must not be handed artwork, whatever it does with the answer.
        var store = await CreateStoreAsync();
        var rendered = Rendered();
        var artwork = Artwork();

        await store.PutAsync(AssetKey.For(rendered), rendered);
        await store.PutAsync(AssetKey.For(artwork), artwork);

        var renders = await store.ListAsync(AssetKey.RenderedLineage);
        var artworks = await store.ListAsync(AssetKey.ArtworkLineage);

        Assert.Equal(AssetKey.For(rendered), Assert.Single(renders));
        Assert.Equal(AssetKey.For(artwork), Assert.Single(artworks));
    }

    [Fact]
    public async Task CAP_RND_002_artwork_is_keyed_by_who_produced_it_and_their_reference()
    {
        // It has no label version and no template because nothing here produced it, and its
        // identity is the agency's - which is what makes it a lineage rather than a flag.
        var store = await CreateStoreAsync();
        var first = Artwork("JOB-2026-0114");
        var second = Artwork("JOB-2026-0115");

        await store.PutAsync(AssetKey.For(first), first);
        await store.PutAsync(AssetKey.For(second), second);

        Assert.Equal(2, (await store.ListAsync(AssetKey.ArtworkLineage)).Count);
    }

    [Fact]
    public async Task CAP_RND_002_a_lineage_holding_nothing_lists_nothing()
    {
        var store = await CreateStoreAsync();

        Assert.Empty(await store.ListAsync(AssetKey.RenderedLineage));
    }
}
