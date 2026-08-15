using System.Globalization;
using System.Text;
using Epi.ContentCore;

namespace Epi.Rendering;

/// <summary>
/// Turns HTML into PDF. The print engine, as the platform sees it (ADR-010).
/// </summary>
/// <remarks>
/// A port, so that everything about a render except the conversion itself is testable without a
/// browser, a container or a network.
/// </remarks>
public interface IPrintEngine
{
    Task<byte[]> ToPdfAsync(string html, CancellationToken cancellationToken = default);
}

/// <summary>
/// Renders a label version to PDF, deterministically (CAP-RND-002, CAP-RND-007).
/// </summary>
/// <remarks>
/// The print engine writes its own <c>/CreationDate</c> and <c>/ModDate</c>, and they are the
/// only bytes that vary between two conversions of the same HTML - measured, and recorded in
/// ADR-033. Everything else the engine produces is already identical.
/// <para>
/// So those two fields are rewritten to a date the caller supplies from the content, which is
/// ADR-033 decision 4 applied to the one field the engine insists on writing for itself. The
/// renderer never reads a clock; if the caller passes the content's date, the output is a
/// function of the content.
/// </para>
/// </remarks>
public static class PdfRenderer
{
    /// <summary>The PDF date format, which is fixed-width and therefore safe to rewrite.</summary>
    private const string Format = "yyyyMMddHHmmss";

    public static async Task<RenderedDocument> RenderAsync(
        EpiDocument document,
        RenderTemplate template,
        IPrintEngine engine,
        DateTimeOffset contentDate,
        bool draft = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(engine);

        var html = HtmlRenderer.Render(document, template, draft);
        var pdf = await engine.ToPdfAsync(Encoding.UTF8.GetString(html.Content), cancellationToken);

        return new RenderedDocument(
            "application/pdf",
            Normalise(pdf, contentDate),
            html.Label,
            html.LabelVersion,
            html.RenderTemplate,
            html.RenderTemplateVersion,
            draft);
    }

    /// <summary>
    /// Rewrites the dates the print engine wrote for itself, leaving every other byte alone.
    /// </summary>
    /// <remarks>
    /// A byte-for-byte replacement, never a shorter or longer one: a PDF's cross-reference table
    /// holds byte offsets, so changing the length of anything before it moves every object and
    /// produces a file that opens in some readers and not others. The PDF date format is
    /// fixed-width, which is what makes this safe rather than lucky - and the length is asserted
    /// rather than assumed.
    /// </remarks>
    public static byte[] Normalise(byte[] pdf, DateTimeOffset date)
    {
        ArgumentNullException.ThrowIfNull(pdf);

        var stamp = date.ToUniversalTime().ToString(Format, CultureInfo.InvariantCulture);
        var normalised = (byte[])pdf.Clone();

        foreach (var field in new[] { "/CreationDate (D:", "/ModDate (D:" })
        {
            var marker = Encoding.ASCII.GetBytes(field);
            var at = IndexOf(normalised, marker);
            if (at < 0)
            {
                continue;
            }

            var start = at + marker.Length;
            var replacement = Encoding.ASCII.GetBytes(stamp);

            // Only where the engine wrote exactly the width this replaces. Anything else is a
            // format this code has not seen, and rewriting it blind would corrupt the file.
            if (start + replacement.Length > normalised.Length
                || !normalised.Skip(start).Take(replacement.Length).All(b => b is >= (byte)'0' and <= (byte)'9'))
            {
                continue;
            }

            replacement.CopyTo(normalised, start);
        }

        return normalised;
    }

    private static int IndexOf(byte[] haystack, byte[] needle)
    {
        for (var i = 0; i <= haystack.Length - needle.Length; i++)
        {
            var found = true;
            for (var j = 0; j < needle.Length; j++)
            {
                if (haystack[i + j] != needle[j])
                {
                    found = false;
                    break;
                }
            }

            if (found)
            {
                return i;
            }
        }

        return -1;
    }
}
