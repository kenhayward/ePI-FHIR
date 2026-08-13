using Epi.ContentCore;
using Hl7.Fhir.Model;
using Xunit;

namespace Epi.ContentCore.Tests;

// Unit tests for reading and writing the canonical document.
//   FN-CC-001 Parse an ePI document Bundle anchored by a Composition
//   FN-CC-006 Serialise and deserialise a Bundle without content loss
public sealed class EpiBundleReaderTests
{
    private static string MinimalDocumentJson() =>
        File.ReadAllText(TestFixtures.Path("epi", "minimal-epi-document.json"));

    /// <summary>The anchoring Composition, asserted present rather than assumed.</summary>
    private static Composition CompositionOf(Bundle bundle) =>
        Assert.IsType<Composition>(bundle.Entry[0].Resource);

    /// <summary>The narrative of a section, asserted present rather than assumed.</summary>
    private static string NarrativeOf(Bundle bundle, int section)
    {
        var narrative = CompositionOf(bundle).Section[section].Text;
        Assert.NotNull(narrative);
        Assert.NotNull(narrative.Div);
        return narrative.Div;
    }

    [Fact]
    public void FN_CC_001_reads_a_document_bundle_anchored_by_a_composition()
    {
        var bundle = EpiBundleReader.Read(MinimalDocumentJson());

        Assert.Equal(Bundle.BundleType.Document, bundle.Type);
        var composition = Assert.IsType<Composition>(bundle.Entry[0].Resource);
        Assert.Equal("SYNTHETIC TEST LABEL - Examplinum 10 mg film-coated tablets", composition.Title);
        Assert.Equal(2, composition.Section.Count);
    }

    [Fact]
    public void FN_CC_001_rejects_a_bundle_that_is_not_of_type_document()
    {
        var json = MinimalDocumentJson().Replace("\"type\": \"document\"", "\"type\": \"collection\"");

        var error = Assert.Throws<InvalidEpiBundleException>(() => EpiBundleReader.Read(json));

        Assert.Contains(error.Problems, p => p.Contains("document", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FN_CC_001_rejects_a_document_bundle_whose_first_entry_is_not_a_composition()
    {
        // A parseable resource in the anchor position, so the structural check is what
        // rejects it rather than the parser.
        var json = """
            {
              "resourceType": "Bundle",
              "type": "document",
              "entry": [ { "resource": { "resourceType": "Patient", "active": true } } ]
            }
            """;

        var error = Assert.Throws<InvalidEpiBundleException>(() => EpiBundleReader.Read(json));

        Assert.Contains(error.Problems, p => p.Contains("Composition", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FN_CC_001_rejects_content_carrying_elements_that_are_not_in_the_model()
    {
        // Strict parsing: an element the model does not define is content we could store but
        // not faithfully reproduce, which is exactly what CAP-SCM-010 forbids.
        var json = MinimalDocumentJson().Replace("\"title\":", "\"titel\":");

        var error = Assert.Throws<InvalidEpiBundleException>(() => EpiBundleReader.Read(json));

        Assert.Contains(error.Problems, p => p.Contains("titel"));
    }

    [Fact]
    public void FN_CC_001_rejects_a_bundle_with_no_entries()
    {
        var error = Assert.Throws<InvalidEpiBundleException>(
            () => EpiBundleReader.Read("""{"resourceType": "Bundle", "type": "document"}"""));

        Assert.NotEmpty(error.Problems);
    }

    [Fact]
    public void FN_CC_001_rejects_content_that_is_not_a_bundle()
    {
        var error = Assert.Throws<InvalidEpiBundleException>(
            () => EpiBundleReader.Read("""{"resourceType": "Patient", "active": true}"""));

        Assert.Contains(error.Problems, p => p.Contains("Bundle", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FN_CC_001_rejects_malformed_json_without_leaking_a_parser_stack_trace()
    {
        var error = Assert.Throws<InvalidEpiBundleException>(() => EpiBundleReader.Read("{ not json"));

        Assert.NotEmpty(error.Problems);
    }

    [Fact]
    public void FN_CC_006_serialises_and_reparses_without_content_loss()
    {
        // CAP-SCM-010: a conformant ePI can be represented and re-serialised without content
        // loss. Compared structurally rather than as text, because JSON key order and
        // whitespace are not content.
        var original = EpiBundleReader.Read(MinimalDocumentJson());

        var reparsed = EpiBundleReader.Read(EpiBundleReader.Write(original));

        Assert.True(original.IsExactly(reparsed),
            "The re-parsed bundle is not structurally identical to the original.");
    }

    [Fact]
    public void FN_CC_006_preserves_narrative_markup_exactly()
    {
        // Narrative is regulated content: an xhtml div that survives "almost" intact is a
        // content-loss defect, not a formatting detail.
        var original = EpiBundleReader.Read(MinimalDocumentJson());
        var originalDiv = NarrativeOf(original, 0);

        var reparsed = EpiBundleReader.Read(EpiBundleReader.Write(original));

        Assert.Equal(originalDiv, NarrativeOf(reparsed, 0));
    }
}
