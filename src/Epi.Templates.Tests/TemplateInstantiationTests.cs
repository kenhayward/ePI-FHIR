using Epi.ContentCore;
using Hl7.Fhir.Model;
using Xunit;

namespace Epi.Templates.Tests;

// Instantiating a label from a template (ADR-021).
//   CAP-TPL-004 Instantiate a new label from a template, producing a conformant draft
//   CAP-TPL-007 Record which template, and which template version, a label came from
public sealed class TemplateInstantiationTests
{
    private static LabelTemplate Template(params TemplateSection[] sections) => new(
        "smpc-gb",
        3,
        "Summary of Product Characteristics (Great Britain)",
        "smpc",
        new ProfileTarget("hl7.fhir.uv.emedicinal-product-info", "1.0.0"),
        sections.Length > 0
            ? sections
            : [
                new TemplateSection("name", "1", "http://example.org/epi-sections",
                    "Name of the medicinal product"),
                new TemplateSection("composition", "2", "http://example.org/epi-sections",
                    "Qualitative and quantitative composition",
                    Boilerplate: "<div xmlns=\"http://www.w3.org/1999/xhtml\">To be completed.</div>"),
                new TemplateSection("pharmaceutical-form", "3", "http://example.org/epi-sections",
                    "Pharmaceutical form", Mandatory: false),
            ]);

    private static Composition CompositionOf(Bundle bundle) =>
        Assert.IsType<Composition>(bundle.Entry[0].Resource);

    [Fact]
    public void CAP_TPL_004_instantiation_produces_a_document_bundle_anchored_by_a_composition()
    {
        // The same shape every other write path produces, so the write gate is the only gate:
        // a template that cannot make a conformant draft is a broken template, and the
        // existing validator is what says so (ADR-021 decision 3).
        var bundle = TemplateInstantiation.Instantiate(Template(), "Examplinum 10 mg tablets");

        Assert.Equal(Bundle.BundleType.Document, bundle.Type);
        Assert.Equal("Examplinum 10 mg tablets", CompositionOf(bundle).Title);
    }

    [Fact]
    public void CAP_TPL_004_the_sections_arrive_in_the_order_the_template_gives_them()
    {
        // Order is part of what a template defines (CAP-TPL-002), and a label whose sections
        // arrive shuffled is one an author has to reorder by hand every time.
        var sections = CompositionOf(TemplateInstantiation.Instantiate(Template(), "A label")).Section;

        Assert.Equal(
            ["Name of the medicinal product", "Qualitative and quantitative composition",
             "Pharmaceutical form"],
            sections.Select(s => s.Title));
    }

    [Fact]
    public void CAP_TPL_004_optional_sections_are_scaffolded_too()
    {
        // An author removing a section is a decision; an author never seeing one is an
        // accident. Scaffolding both and letting the author delete is the safer default.
        var sections = CompositionOf(TemplateInstantiation.Instantiate(Template(), "A label")).Section;

        Assert.Equal(3, sections.Count);
        Assert.Contains(sections, s => s.Title == "Pharmaceutical form");
    }

    [Fact]
    public void CAP_TPL_004_each_section_carries_the_code_the_template_binds_it_to()
    {
        var section = CompositionOf(TemplateInstantiation.Instantiate(Template(), "A label")).Section[0];

        var coding = Assert.Single(section.Code!.Coding);
        Assert.Equal("1", coding.Code);
        Assert.Equal("http://example.org/epi-sections", coding.System);
    }

    [Fact]
    public void CAP_TPL_004_boilerplate_becomes_the_section_narrative()
    {
        var sections = CompositionOf(TemplateInstantiation.Instantiate(Template(), "A label")).Section;

        Assert.Contains("To be completed", sections[1].Text!.Div, StringComparison.Ordinal);
        Assert.Equal(Narrative.NarrativeStatus.Additional, sections[1].Text!.Status);
    }

    [Fact]
    public void CAP_TPL_004_a_section_with_no_boilerplate_is_left_empty_rather_than_invented()
    {
        // An empty section is honest. Inventing placeholder narrative would put words into a
        // regulated document that nobody wrote and a reviewer might not notice.
        var sections = CompositionOf(TemplateInstantiation.Instantiate(Template(), "A label")).Section;

        Assert.Null(sections[0].Text);
    }

    [Fact]
    public void CAP_TPL_004_nested_sections_are_scaffolded_in_place()
    {
        var template = Template(new TemplateSection(
            "clinical", "4", "http://example.org/epi-sections", "Clinical particulars",
            Sections: [
                new TemplateSection("indications", "4.1", "http://example.org/epi-sections",
                    "Therapeutic indications"),
            ]));

        var parent = Assert.Single(CompositionOf(
            TemplateInstantiation.Instantiate(template, "A label")).Section);

        var child = Assert.Single(parent.Section);
        Assert.Equal("Therapeutic indications", child.Title);
    }

    [Fact]
    public void CAP_TPL_007_the_content_records_the_template_and_version_it_came_from()
    {
        // On the content itself, so provenance survives independently of any registry the
        // platform keeps - and answers "which template made this" from the document alone.
        var bundle = TemplateInstantiation.Instantiate(Template(), "A label");

        Assert.Equal("smpc-gb", TemplateInstantiation.TemplateOf(bundle));
        Assert.Equal(3, TemplateInstantiation.TemplateVersionOf(bundle));
    }

    [Fact]
    public void CAP_TPL_007_content_from_no_template_says_so_rather_than_guessing()
    {
        var handAuthored = new Bundle { Type = Bundle.BundleType.Document };

        Assert.Null(TemplateInstantiation.TemplateOf(handAuthored));
        Assert.Null(TemplateInstantiation.TemplateVersionOf(handAuthored));
    }

    [Fact]
    public void CAP_TPL_007_the_recording_honours_the_configured_identifier_authority()
    {
        // Provenance tags are minted into the deployment's own namespaces like every other
        // identifier the platform writes (ADR-017).
        var authority = IdentifierAuthority.Demonstration with
        {
            TemplateSystem = "https://labels.example.net/tag/template",
            TemplateVersionTagSystem = "https://labels.example.net/tag/template-version",
        };

        var bundle = TemplateInstantiation.Instantiate(Template(), "A label", authority);

        Assert.Equal("smpc-gb", TemplateInstantiation.TemplateOf(bundle, authority));
        Assert.Null(TemplateInstantiation.TemplateOf(bundle));
    }

    [Fact]
    public void CAP_TPL_004_a_template_with_no_sections_cannot_scaffold_anything()
    {
        var empty = new LabelTemplate("empty", 1, "Empty", "smpc",
            new ProfileTarget("hl7.fhir.uv.emedicinal-product-info", "1.0.0"), []);

        var error = Assert.Throws<InvalidTemplateException>(
            () => TemplateInstantiation.Instantiate(empty, "A label"));

        Assert.Contains(error.Problems, p => p.Contains("section"));
    }

    [Fact]
    public void CAP_TPL_004_a_label_needs_a_title_of_its_own()
    {
        // The template names the kind of label; only the author can name the product.
        Assert.Throws<ArgumentException>(
            () => TemplateInstantiation.Instantiate(Template(), "   "));
    }
}
