using System.Text;
using Epi.ContentCore;
using Hl7.Fhir.Model;
using Xunit;

namespace Epi.Rendering.Tests;

// Deterministic rendering to HTML (ADR-033).
//   CAP-RND-001 Render FHIR ePI to accessible, structured HTML
//   CAP-RND-003 Apply styling via versioned render templates
//   CAP-RND-007 Deterministic, reproducible renders
public sealed class HtmlRendererTests
{
    private static readonly RenderTemplate Template =
        new("qrd-leaflet", 2, "EU QRD package leaflet", "body { font-family: sans-serif; }");

    private static Composition.SectionComponent Section(
        string id, string title, string narrative = "Synthetic test content.")
    {
        return new Composition.SectionComponent
        {
            ElementId = id,
            Title = title,
            Text = new Narrative
            {
                Status = Narrative.NarrativeStatus.Generated,
                Div = $"<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>{narrative}</p></div>",
            },
        };
    }

    private static EpiDocument Document(
        string language = "en-GB", params Composition.SectionComponent[] sections)
    {
        var composition = new Composition
        {
            Title = "SYNTHETIC TEST LABEL - Examplinum 10 mg tablets",
            Language = language,
            Section = sections.Length > 0
                ? [.. sections]
                : [Section("sec-1", "1. What Examplinum is and what it is used for")],
        };

        return new EpiDocument(
            new DocumentIdentity(
                IdentifierAuthority.Demonstration.DocumentSystem,
                "01a00000-0000-7000-8000-00000000000a"),
            3,
            new Bundle
            {
                Type = Bundle.BundleType.Document,
                Entry = [new Bundle.EntryComponent { Resource = composition }],
            });
    }

    private static string Html(RenderedDocument rendered) =>
        Encoding.UTF8.GetString(rendered.Content);

    [Fact]
    public void CAP_RND_007_the_same_version_and_template_render_byte_identically()
    {
        // Acceptance criterion 8, and the property that makes a render evidence rather than a
        // convenience: nobody can say the file filed with a regulator is the one this content
        // produces unless producing it twice gives the same bytes.
        var first = HtmlRenderer.Render(Document(), Template);
        var second = HtmlRenderer.Render(Document(), Template);

        Assert.Equal(first.Content, second.Content);
    }

    [Fact]
    public void CAP_RND_007_nothing_time_varying_is_embedded()
    {
        // The decision that makes byte identity achievable rather than aspirational. Asserted
        // directly, because a timestamp is the thing that gets added later by somebody trying
        // to be helpful (ADR-033 decision 4).
        var html = Html(HtmlRenderer.Render(Document(), Template));

        Assert.DoesNotContain(DateTimeOffset.UtcNow.Year.ToString(), html, StringComparison.Ordinal);
        Assert.DoesNotContain("generated", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void CAP_RND_003_a_different_template_version_produces_different_output()
    {
        // Otherwise the template version on the artefact would be a label rather than a fact.
        var first = HtmlRenderer.Render(Document(), Template);
        var second = HtmlRenderer.Render(Document(), Template with { Version = 3, Stylesheet = "body { color: navy; }" });

        Assert.NotEqual(first.Content, second.Content);
        Assert.Equal(2, first.RenderTemplateVersion);
        Assert.Equal(3, second.RenderTemplateVersion);
    }

    [Fact]
    public void CAP_RND_003_the_render_records_both_versions_it_came_from()
    {
        // On the artefact, so "which template produced this" is answerable from the file rather
        // than from a log (ADR-033 decision 3).
        var rendered = HtmlRenderer.Render(Document(), Template);
        var html = Html(rendered);

        Assert.Equal(3, rendered.LabelVersion);
        Assert.Equal("qrd-leaflet", rendered.RenderTemplate);
        Assert.Contains("epi-label-version\" content=\"3\"", html, StringComparison.Ordinal);
        Assert.Contains("epi-render-template-version\" content=\"2\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public void CAP_RND_001_the_output_carries_the_documents_own_language()
    {
        // So a screen reader announces it correctly, and so a translation renders as what it is.
        Assert.Contains("<html lang=\"fr\">", Html(HtmlRenderer.Render(Document("fr"), Template)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void CAP_RND_001_sections_keep_their_identity_and_their_outline()
    {
        // Section identity in the output is what makes a cross-reference anchor resolve in the
        // rendered document as it does in the content (ADR-028).
        var nested = Section("sec-44-1", "4.4.1 Paediatric population");
        var parent = Section("sec-44", "4.4 Special warnings");
        parent.Section = [nested];

        var html = Html(HtmlRenderer.Render(Document("en-GB", parent), Template));

        Assert.Contains("id=\"sec-44\"", html, StringComparison.Ordinal);
        Assert.Contains("id=\"sec-44-1\"", html, StringComparison.Ordinal);
        Assert.Contains("<h2>4.4 Special warnings</h2>", html, StringComparison.Ordinal);
        Assert.Contains("<h3>4.4.1 Paediatric population</h3>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void CAP_RND_001_narrative_is_emitted_as_it_was_approved()
    {
        // Re-encoding it would change the bytes of content that was approved as it stands.
        var html = Html(HtmlRenderer.Render(
            Document("en-GB", Section("sec-1", "1. Name", "Contains invented-lactose.")), Template));

        Assert.Contains("<p>Contains invented-lactose.</p>", html, StringComparison.Ordinal);
    }

    [Fact]
    public void CAP_RND_001_a_title_containing_markup_characters_is_escaped()
    {
        var document = Document();
        ((Composition)document.Bundle.Entry[0].Resource!).Title = "Examplinum <10 mg> & more";

        var html = Html(HtmlRenderer.Render(document, Template));

        Assert.Contains("Examplinum &lt;10 mg&gt; &amp; more", html, StringComparison.Ordinal);
    }

    [Fact]
    public void CAP_RND_004_a_render_of_an_unapproved_version_says_so()
    {
        // An author preview indistinguishable from an official render is a document that will
        // eventually be sent to somebody.
        var draft = HtmlRenderer.Render(Document(), Template, draft: true);

        Assert.True(draft.Draft);
        Assert.Contains("DRAFT", Html(draft), StringComparison.Ordinal);
        Assert.DoesNotContain("DRAFT", Html(HtmlRenderer.Render(Document(), Template)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void CAP_RND_003_the_stylesheet_travels_with_the_template()
    {
        // Linked rather than carried, a render would depend on something that could change
        // underneath it - and two renders of one version could then differ.
        Assert.Contains("font-family: sans-serif", Html(HtmlRenderer.Render(Document(), Template)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void CAP_RND_002_a_rendered_document_and_artwork_cannot_be_stored_as_each_other()
    {
        // Acceptance criterion 9. Separate types rather than a flag, so this is a thing that
        // does not compile rather than a thing that gets reviewed (ADR-033 decision 6).
        LabelDocument rendered = HtmlRenderer.Render(Document(), Template);
        LabelDocument artwork = new ArtworkDocument(
            "application/pdf", [1, 2, 3], "Agency Ltd", "JOB-2026-0114");

        Assert.IsType<RenderedDocument>(rendered);
        Assert.IsType<ArtworkDocument>(artwork);

        // Artwork has no label version and no render template, because nothing here produced
        // it - which is the distinction, and why there is no honest value to put in a field.
        Assert.IsNotType<RenderedDocument>(artwork);
        Assert.IsNotType<ArtworkDocument>(rendered);
    }

    [Fact]
    public void CAP_RND_002_an_artefact_cannot_be_edited_after_it_is_produced()
    {
        var bytes = new byte[] { 1, 2, 3 };
        var artwork = new ArtworkDocument("application/pdf", bytes, "Agency Ltd", "JOB-2026-0114");

        bytes[0] = 9;

        Assert.Equal(1, artwork.Content[0]);
    }
}
