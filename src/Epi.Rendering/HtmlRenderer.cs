using System.Text;
using Epi.ContentCore;
using Hl7.Fhir.Model;

namespace Epi.Rendering;

/// <summary>
/// A render template: what the output looks like, versioned and approved as content (ADR-033
/// decision 2).
/// </summary>
/// <param name="Stylesheet">
/// CSS carried with the template rather than linked, so a render depends on nothing that could
/// change underneath it.
/// </param>
public sealed record RenderTemplate(
    string Identifier, int Version, string Name, string Stylesheet = "");

/// <summary>
/// Renders a label version to accessible, structured HTML (CAP-RND-001, CAP-RND-007).
/// </summary>
/// <remarks>
/// A pure function of the content and the template. Nothing time-varying is embedded - no
/// generation timestamp, no build number, no environment - because that is what makes byte
/// identity a property of the output rather than of a comparison that excludes some of it
/// (ADR-033 decisions 1 and 4).
/// <para>
/// The date that belongs on a leaflet is the date of the content: the version's approval and
/// effective dates, which are facts about the label rather than about the run that produced the
/// file.
/// </para>
/// </remarks>
public static class HtmlRenderer
{
    public static RenderedDocument Render(
        EpiDocument document, RenderTemplate template, bool draft = false)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(template);

        var composition = document.Bundle.Entry.Count > 0
            ? document.Bundle.Entry[0].Resource as Composition
            : null;

        var html = new StringBuilder();
        html.Append("<!DOCTYPE html>\n");

        // The document's own language, so a screen reader announces it correctly and so a
        // translation renders as what it is (CAP-RND-005, ADR-032).
        var language = string.IsNullOrWhiteSpace(composition?.Language) ? "en" : composition.Language;
        html.Append($"<html lang=\"{Escape(language)}\">\n<head>\n");
        html.Append("<meta charset=\"utf-8\">\n");
        html.Append($"<title>{Escape(composition?.Title ?? string.Empty)}</title>\n");

        // Which versions produced this, on the artefact itself, so the question is answerable
        // from the file rather than from a log (ADR-033 decision 3).
        html.Append($"<meta name=\"epi-label\" content=\"{Escape(document.Identity.Value)}\">\n");
        html.Append($"<meta name=\"epi-label-version\" content=\"{document.Version}\">\n");
        html.Append($"<meta name=\"epi-render-template\" content=\"{Escape(template.Identifier)}\">\n");
        html.Append($"<meta name=\"epi-render-template-version\" content=\"{template.Version}\">\n");

        if (draft)
        {
            html.Append("<meta name=\"epi-draft\" content=\"true\">\n");
        }

        if (!string.IsNullOrWhiteSpace(template.Stylesheet))
        {
            html.Append($"<style>{template.Stylesheet}</style>\n");
        }

        html.Append("</head>\n<body>\n");

        if (draft)
        {
            html.Append("<p class=\"epi-draft-notice\">DRAFT - not an approved version</p>\n");
        }

        html.Append($"<h1>{Escape(composition?.Title ?? string.Empty)}</h1>\n");

        foreach (var section in composition?.Section ?? [])
        {
            Append(html, section, depth: 2);
        }

        html.Append("</body>\n</html>\n");

        return new RenderedDocument(
            "text/html; charset=utf-8",
            Encoding.UTF8.GetBytes(html.ToString()),
            new DocumentIdentityRef(document.Identity.System, document.Identity.Value),
            document.Version,
            template.Identifier,
            template.Version,
            draft);
    }

    private static void Append(StringBuilder html, Composition.SectionComponent section, int depth)
    {
        // Heading level follows the section's depth in the document, so the output has the
        // outline a structured leaflet has. Capped at six, which is all HTML defines.
        var level = Math.Min(depth, 6);

        html.Append($"<section id=\"{Escape(section.ElementId ?? string.Empty)}\">\n");

        if (!string.IsNullOrWhiteSpace(section.Title))
        {
            html.Append($"<h{level}>{Escape(section.Title)}</h{level}>\n");
        }

        if (section.Text?.Div is { } narrative)
        {
            // Narrative is already a constrained XHTML fragment, so it is emitted as it stands:
            // re-encoding it would change the bytes of content that was approved as it is.
            html.Append(narrative).Append('\n');
        }

        foreach (var nested in section.Section)
        {
            Append(html, nested, depth + 1);
        }

        html.Append("</section>\n");
    }

    private static string Escape(string value) => value
        .Replace("&", "&amp;", StringComparison.Ordinal)
        .Replace("<", "&lt;", StringComparison.Ordinal)
        .Replace(">", "&gt;", StringComparison.Ordinal)
        .Replace("\"", "&quot;", StringComparison.Ordinal);
}
