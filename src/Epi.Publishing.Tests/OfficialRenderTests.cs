using Epi.ContentCore;
using Epi.Lifecycle;
using Epi.Rendering;
using Epi.Templates;
using Hl7.Fhir.Model;
using Xunit;

namespace Epi.Publishing.Tests;

// The artefact of record (FN-RND-004).
//   CAP-RND-001 Render a label version to HTML and PDF from FHIR content
//   CAP-RND-002 Store rendered output as immutable assets
//   CAP-RND-004 Distinguish an author preview from an official render
//
// ADR-033 decision 2 said only an approved template may produce an official render, and nothing
// could satisfy it: there was no template store, so every render was a preview that said so.
// ADR-042 and ADR-043 built the store and its lifecycle. This is the thing they were for.
//
// Two approvals, not one. The content has to be approved because an official render of a draft
// is a document somebody will eventually send; the template has to be approved because a
// template determines what a patient reads. Either one missing makes the artefact a preview,
// whatever else is true of it.
public sealed class OfficialRenderTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 17, 10, 0, 0, TimeSpan.Zero);

    private static readonly IdentifierAuthority Authority = IdentifierAuthority.Demonstration;

    private const string Label = "01a00000-0000-7000-8000-0000000000b1";

    private const string Template = "qrd-package-leaflet";

    private static DocumentIdentity Identity(string value = Label) =>
        new(Authority.DocumentSystem, value);

    private sealed record Subject(
        OfficialRender Render,
        InMemoryContentStore Content,
        InMemoryLifecycleStore Lifecycle,
        InMemoryTemplateStore Templates,
        InMemoryAssetStore Assets);

    private static Bundle Document(string title = "SYNTHETIC TEST LABEL - Examplinum 10 mg tablets")
    {
        var composition = new Composition
        {
            Title = title,
            Language = "en-GB",
            Section =
            [
                new Composition.SectionComponent
                {
                    Title = "1. What Examplinum is and what it is used for",
                    Text = new Narrative
                    {
                        Status = Narrative.NarrativeStatus.Generated,
                        Div = "<div xmlns=\"http://www.w3.org/1999/xhtml\">"
                              + "<p>Synthetic test content.</p></div>",
                    },
                },
            ],
        };

        return new Bundle
        {
            Type = Bundle.BundleType.Document,
            Entry = [new Bundle.EntryComponent { Resource = composition }],
        };
    }

    private static Subject Fresh()
    {
        var content = new InMemoryContentStore();
        var lifecycle = new InMemoryLifecycleStore();
        var templates = new InMemoryTemplateStore();
        var assets = new InMemoryAssetStore();

        return new Subject(
            new OfficialRender(content, lifecycle, templates, assets, "approved", "approved"),
            content,
            lifecycle,
            templates,
            assets);
    }

    /// <summary>A label version in the given state, written and registered as a save leaves it.</summary>
    private static async Task LabelInAsync(Subject subject, string state, string identifier = Label)
    {
        await subject.Content.CreateAsync(Identity(identifier), Document());
        await subject.Lifecycle.RegisterAsync(
            new VersionRef(identifier, 1), "user-anna", "draft", Now);

        if (!string.Equals(state, "draft", StringComparison.Ordinal))
        {
            await subject.Lifecycle.AppendAsync(new StateTransition(
                new VersionRef(identifier, 1), "draft", state, "approve", "user-ben", Now));
        }
    }

    /// <summary>A template in the given state.</summary>
    private static async Task TemplateInAsync(
        Subject subject, string state, string identifier = Template)
    {
        await subject.Templates.CreateAsync(
            new RenderTemplateDefinition(identifier, "EU QRD package leaflet", "body { }"));
        await subject.Lifecycle.RegisterAsync(
            new VersionRef(identifier, 1), "platform:template-seed", "draft", Now,
            RegisteredArtefact.Template);

        if (!string.Equals(state, "draft", StringComparison.Ordinal))
        {
            await subject.Lifecycle.AppendAsync(new StateTransition(
                new VersionRef(identifier, 1), "draft", state, "approve", "user-ben", Now));
        }
    }

    private static async Task<Subject> ReadyAsync()
    {
        var subject = Fresh();
        await LabelInAsync(subject, "approved");
        await TemplateInAsync(subject, "approved");
        return subject;
    }

    [Fact]
    public async Task FN_RND_004_an_approved_version_and_an_approved_template_produce_a_render()
    {
        var subject = await ReadyAsync();

        var outcome = await subject.Render.ProduceAsync(Identity(), 1, Template);

        Assert.NotNull(outcome);
        Assert.False(outcome!.Document.Draft);
        Assert.Equal(Template, outcome.Document.RenderTemplate);
        Assert.Equal(1, outcome.Document.RenderTemplateVersion);
    }

    [Fact]
    public async Task FN_RND_004_the_render_is_filed_in_the_asset_store()
    {
        // The half a preview deliberately does not do. An artefact nobody kept is not the
        // artefact of record (CAP-RND-002).
        var subject = await ReadyAsync();

        var outcome = await subject.Render.ProduceAsync(Identity(), 1, Template);

        Assert.NotNull(await subject.Assets.GetAsync(outcome!.Key));
        Assert.False(outcome.AlreadyFiled);
    }

    [Fact]
    public async Task FN_RND_004_the_key_names_both_versions_that_made_it()
    {
        // Both are inputs to the bytes, so a key naming only the label version would collide the
        // moment a template was revised (ADR-033 decision 1).
        var subject = await ReadyAsync();

        var outcome = await subject.Render.ProduceAsync(Identity(), 1, Template);

        Assert.Contains(Label, outcome!.Key.Path, StringComparison.Ordinal);
        Assert.Contains(Template, outcome.Key.Path, StringComparison.Ordinal);
        Assert.Equal(AssetKey.RenderedLineage, outcome.Key.Lineage);
    }

    [Fact]
    public async Task FN_RND_004_a_draft_version_is_refused()
    {
        // An official render of a draft is a document somebody will eventually send
        // (CAP-RND-004). The preview endpoint is where an author looks at unapproved content.
        var subject = Fresh();
        await LabelInAsync(subject, "draft");
        await TemplateInAsync(subject, "approved");

        var refused = await Assert.ThrowsAsync<RenderRefusedException>(
            () => subject.Render.ProduceAsync(Identity(), 1, Template));

        Assert.Contains("approved", refused.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FN_RND_004_a_template_nobody_approved_is_refused()
    {
        // ADR-033 decision 2 and ADR-042 decision 4. A template determines what a patient reads,
        // so a render made with one nobody signed for is not the artefact filed with a regulator.
        var subject = Fresh();
        await LabelInAsync(subject, "approved");
        await TemplateInAsync(subject, "draft");

        await Assert.ThrowsAsync<RenderRefusedException>(
            () => subject.Render.ProduceAsync(Identity(), 1, Template));
    }

    [Fact]
    public async Task FN_RND_004_a_retired_template_is_refused()
    {
        // Retiring says nothing new may be rendered with it. Renders already filed stay valid
        // and stay reproducible; this is the half that stops new ones (config template-states).
        var subject = Fresh();
        await LabelInAsync(subject, "approved");
        await TemplateInAsync(subject, "retired");

        await Assert.ThrowsAsync<RenderRefusedException>(
            () => subject.Render.ProduceAsync(Identity(), 1, Template));
    }

    [Fact]
    public async Task FN_RND_004_a_template_that_does_not_exist_is_refused()
    {
        var subject = Fresh();
        await LabelInAsync(subject, "approved");

        await Assert.ThrowsAsync<RenderRefusedException>(
            () => subject.Render.ProduceAsync(Identity(), 1, "no-such-template"));
    }

    [Fact]
    public async Task FN_RND_004_a_version_nobody_wrote_has_no_render()
    {
        var subject = Fresh();
        await TemplateInAsync(subject, "approved");

        Assert.Null(await subject.Render.ProduceAsync(Identity(), 1, Template));
    }

    [Fact]
    public async Task FN_RND_004_producing_the_same_render_twice_files_it_once()
    {
        // A render is a pure function of its two versions, so asking again is asking for the
        // same bytes. Refusing on the write-once rule would make an idempotent request look
        // like a conflict, and callers would learn to retry through it.
        var subject = await ReadyAsync();

        var first = await subject.Render.ProduceAsync(Identity(), 1, Template);
        var second = await subject.Render.ProduceAsync(Identity(), 1, Template);

        Assert.False(first!.AlreadyFiled);
        Assert.True(second!.AlreadyFiled);
        Assert.Equal(first.Document.Content, second.Document.Content);
        Assert.Single(await subject.Assets.ListAsync(AssetKey.RenderedLineage));
    }

    [Fact]
    public async Task FN_RND_004_a_filed_render_that_no_longer_matches_the_content_is_raised()
    {
        // The check that makes "reproducible" more than a claim. If what is filed differs from
        // what this content and this template now produce, one of them has changed underneath a
        // regulator's copy - and answering with either version silently would hide it.
        var subject = await ReadyAsync();
        var outcome = await subject.Render.ProduceAsync(Identity(), 1, Template);

        var tampered = new InMemoryAssetStore();
        await tampered.PutAsync(
            outcome!.Key,
            new RenderedDocument(
                outcome.Document.MediaType,
                "<html>not what this produces</html>"u8.ToArray(),
                outcome.Document.Label,
                outcome.Document.LabelVersion,
                outcome.Document.RenderTemplate,
                outcome.Document.RenderTemplateVersion));

        var render = new OfficialRender(
            subject.Content, subject.Lifecycle, subject.Templates, tampered,
            "approved", "approved");

        await Assert.ThrowsAsync<RenderMismatchException>(
            () => render.ProduceAsync(Identity(), 1, Template));
    }

    [Fact]
    public async Task FN_RND_004_the_same_version_renders_to_the_same_bytes_anywhere()
    {
        // Determinism through the whole path rather than through the renderer alone
        // (ADR-033 decision 1): one stored version, two independent producers, nothing filed
        // for the second to copy from.
        //
        // Deliberately the same stored version rather than two documents carrying the same
        // words. Stamping assigns section identifiers (ADR-015 decision 6), so two separately
        // written documents are different content that happens to read alike - and asserting
        // they render identically would be asserting that section identity does not reach the
        // output, which is the opposite of what a stable anchor is for.
        var subject = await ReadyAsync();
        var elsewhere = new OfficialRender(
            subject.Content, subject.Lifecycle, subject.Templates, new InMemoryAssetStore(),
            "approved", "approved");

        Assert.Equal(
            (await subject.Render.ProduceAsync(Identity(), 1, Template))!.Document.Content,
            (await elsewhere.ProduceAsync(Identity(), 1, Template))!.Document.Content);
    }

    [Fact]
    public async Task FN_RND_004_the_render_carries_the_label_s_own_words()
    {
        var subject = await ReadyAsync();

        var outcome = await subject.Render.ProduceAsync(Identity(), 1, Template);

        Assert.Contains(
            "Examplinum",
            System.Text.Encoding.UTF8.GetString(outcome!.Document.Content),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task FN_RND_004_the_render_does_not_say_it_is_a_preview()
    {
        // The distinction CAP-RND-004 exists for, asserted on the artefact rather than on a flag
        // beside it: whoever opens the file has to be able to tell.
        var subject = await ReadyAsync();

        var outcome = await subject.Render.ProduceAsync(Identity(), 1, Template);

        Assert.DoesNotContain(
            "preview",
            System.Text.Encoding.UTF8.GetString(outcome!.Document.Content),
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FN_RND_004_what_has_been_filed_for_a_version_can_be_listed()
    {
        // So a surface can offer what exists rather than asking for it to be made again, and so
        // an inspector can see what was produced from a version.
        var subject = await ReadyAsync();
        await subject.Render.ProduceAsync(Identity(), 1, Template);

        var filed = await subject.Render.FiledAsync(Identity(), 1);

        var only = Assert.Single(filed);
        Assert.Equal(Template, only.RenderTemplate);
        Assert.Equal(1, only.RenderTemplateVersion);
    }

    [Fact]
    public async Task FN_RND_004_a_listing_does_not_cross_into_another_version()
    {
        var subject = await ReadyAsync();
        await LabelInAsync(subject, "approved", "01a00000-0000-7000-8000-0000000000b2");
        await subject.Render.ProduceAsync(Identity(), 1, Template);
        await subject.Render.ProduceAsync(
            Identity("01a00000-0000-7000-8000-0000000000b2"), 1, Template);

        Assert.Single(await subject.Render.FiledAsync(Identity(), 1));
    }
}
