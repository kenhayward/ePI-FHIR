using System.Text;
using Epi.ContentCore;
using Hl7.Fhir.Model;
using Xunit;

namespace Epi.Rendering.Tests;

// PDF rendering and the normalisation determinism needs (ADR-033).
//   CAP-RND-002 Render to PDF, the rendered-PDF lineage
//   CAP-RND-007 Deterministic, reproducible renders
public sealed class PdfRendererTests
{
    private static readonly DateTimeOffset ContentDate =
        new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static readonly RenderTemplate Template = new("qrd-leaflet", 2, "EU QRD package leaflet");

    private static EpiDocument Document() => new(
        new DocumentIdentity(
            IdentifierAuthority.Demonstration.DocumentSystem,
            "01a00000-0000-7000-8000-00000000000a"),
        3,
        new Bundle
        {
            Type = Bundle.BundleType.Document,
            Entry = [new Bundle.EntryComponent
            {
                Resource = new Composition { Title = "SYNTHETIC TEST LABEL", Language = "en-GB" },
            }],
        });

    /// <summary>
    /// A print engine that behaves as the measured one does: identical output except for the
    /// two dates it writes for itself (ADR-033, the measured section).
    /// </summary>
    private sealed class DatingEngine(params DateTimeOffset[] stamps) : IPrintEngine
    {
        private int _call;

        public Task<byte[]> ToPdfAsync(string html, CancellationToken cancellationToken = default)
        {
            var stamp = stamps[Math.Min(_call++, stamps.Length - 1)]
                .ToString("yyyyMMddHHmmss", System.Globalization.CultureInfo.InvariantCulture);

            return Task.FromResult(Encoding.ASCII.GetBytes(
                "%PDF-1.4\n1 0 obj\n<</Producer (Skia/PDF m151)\n"
                + $"/CreationDate (D:{stamp}+00'00')\n/ModDate (D:{stamp}+00'00')>>\nendobj\n"
                + $"% content of {html.Length} bytes\n%%EOF\n"));
        }
    }

    [Fact]
    public async Task CAP_RND_007_two_renders_of_one_version_are_byte_identical()
    {
        // The engine stamps a different second each time, as the real one does. The output must
        // not, because a render that differs between runs is a convenience and never evidence.
        var engine = new DatingEngine(
            new DateTimeOffset(2026, 8, 15, 16, 25, 26, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 15, 16, 25, 28, TimeSpan.Zero));

        var first = await PdfRenderer.RenderAsync(Document(), Template, engine, ContentDate);
        var second = await PdfRenderer.RenderAsync(Document(), Template, engine, ContentDate);

        Assert.Equal(first.Content, second.Content);
    }

    [Fact]
    public async Task CAP_RND_007_the_date_in_the_output_is_the_content_date_the_caller_gave()
    {
        // ADR-033 decision 4 applied to the one field the engine insists on writing for itself:
        // the date that belongs on the artefact is the date of the content.
        var engine = new DatingEngine(new DateTimeOffset(2026, 8, 15, 16, 25, 26, TimeSpan.Zero));

        var rendered = await PdfRenderer.RenderAsync(Document(), Template, engine, ContentDate);

        var pdf = Encoding.ASCII.GetString(rendered.Content);
        Assert.Contains("/CreationDate (D:20260301090000", pdf, StringComparison.Ordinal);
        Assert.Contains("/ModDate (D:20260301090000", pdf, StringComparison.Ordinal);
        Assert.DoesNotContain("20260815", pdf, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CAP_RND_002_a_rendered_pdf_records_both_versions_and_its_media_type()
    {
        var engine = new DatingEngine(ContentDate);

        var rendered = await PdfRenderer.RenderAsync(Document(), Template, engine, ContentDate);

        Assert.Equal("application/pdf", rendered.MediaType);
        Assert.Equal(3, rendered.LabelVersion);
        Assert.Equal("qrd-leaflet", rendered.RenderTemplate);
        Assert.Equal(2, rendered.RenderTemplateVersion);
    }

    [Fact]
    public void CAP_RND_007_normalising_replaces_the_dates_without_moving_a_single_byte()
    {
        // A PDF's cross-reference table holds byte offsets, so a replacement that changed length
        // would move every object after it and produce a file that opens in some readers and
        // not others. The format is fixed-width, which is what makes this safe rather than
        // lucky - so the length is asserted rather than assumed.
        var original = Encoding.ASCII.GetBytes(
            "%PDF-1.4\n/CreationDate (D:20260815162526+00'00')\n/ModDate (D:20260815162526+00'00')\n%%EOF\n");

        var normalised = PdfRenderer.Normalise(original, ContentDate);

        Assert.Equal(original.Length, normalised.Length);
        Assert.Contains("D:20260301090000", Encoding.ASCII.GetString(normalised), StringComparison.Ordinal);
    }

    [Fact]
    public void CAP_RND_007_normalising_leaves_everything_that_is_not_a_date_alone()
    {
        var original = Encoding.ASCII.GetBytes(
            "%PDF-1.4\n/Producer (Skia/PDF m151)\n/CreationDate (D:20260815162526+00'00')\n"
            + "stream content 20260815 here\n%%EOF\n");

        var text = Encoding.ASCII.GetString(PdfRenderer.Normalise(original, ContentDate));

        // The producer string and the stream survive, including a date-like run inside content.
        Assert.Contains("/Producer (Skia/PDF m151)", text, StringComparison.Ordinal);
        Assert.Contains("stream content 20260815 here", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CAP_RND_007_a_document_with_no_dates_to_normalise_is_returned_unchanged()
    {
        var original = Encoding.ASCII.GetBytes("%PDF-1.4\n/Producer (Something else)\n%%EOF\n");

        Assert.Equal(original, PdfRenderer.Normalise(original, ContentDate));
    }

    [Fact]
    public void CAP_RND_007_a_date_field_in_a_shape_this_code_has_not_seen_is_left_alone()
    {
        // Rewriting blind is how a file gets corrupted. If the engine ever writes something
        // other than fourteen digits there, this does nothing rather than something.
        var original = Encoding.ASCII.GetBytes("%PDF-1.4\n/CreationDate (D:not-a-date)\n%%EOF\n");

        Assert.Equal(original, PdfRenderer.Normalise(original, ContentDate));
    }

    [Fact]
    public void CAP_RND_007_normalising_does_not_alter_what_it_was_given()
    {
        var original = Encoding.ASCII.GetBytes("%PDF-1.4\n/CreationDate (D:20260815162526+00'00')\n%%EOF\n");
        var before = (byte[])original.Clone();

        _ = PdfRenderer.Normalise(original, ContentDate);

        Assert.Equal(before, original);
    }
}
