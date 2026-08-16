using Hl7.Fhir.Model;
using Xunit;

namespace Epi.ContentCore.Tests;

// A section-shaped view of a version, and the patch back (FN-CC-010).
//   CAP-TPL-005 Template-driven guided authoring that shields authors from raw FHIR
//   CAP-SCM-009 Expose the content model to authoring, validation and rendering
//
// ADR-038. The dangerous half is the write: a projection carries what an author may change and a
// Bundle carries a great deal more, so rebuilding one from the other would silently discard
// regulated content because a web form had no field for it.
public sealed class SectionProjectionTests
{
    private static Bundle Document(params Composition.SectionComponent[] sections)
    {
        var bundle = new Bundle
        {
            Type = Bundle.BundleType.Document,
            Entry =
            [
                new Bundle.EntryComponent
                {
                    Resource = new Composition
                    {
                        Title = "SYNTHETIC TEST LABEL - Examplinum 10 mg tablets",
                        Language = "en-GB",
                        Section = [.. sections],
                    },
                },
            ],
        };

        SectionIdentity.AssignMissing(bundle);
        return bundle;
    }

    private static Composition.SectionComponent Section(string title, string words, string? id = null) =>
        new()
        {
            ElementId = id,
            Title = title,
            Text = new Narrative
            {
                Status = Narrative.NarrativeStatus.Generated,
                Div = $"<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>{words}</p></div>",
            },
        };

    private static Composition CompositionOf(Bundle bundle) =>
        Assert.IsType<Composition>(bundle.Entry[0].Resource);

    [Fact]
    public void FN_CC_010_a_version_projects_to_its_sections()
    {
        var bundle = Document(Section("1. What it is", "A medicine."), Section("2. Before", "Read this."));

        var projected = SectionProjection.Of(bundle);

        Assert.Equal(["1. What it is", "2. Before"], projected.Select(s => s.Title));
        Assert.Contains("A medicine.", projected[0].Narrative);
    }

    [Fact]
    public void FN_CC_010_every_section_carries_the_identity_the_platform_assigned()
    {
        // The join for a save, and never invented by whatever is doing the projecting
        // (ADR-038 decision 4).
        var bundle = Document(Section("1. What it is", "A medicine."));

        var projected = Assert.Single(SectionProjection.Of(bundle));

        Assert.Equal(CompositionOf(bundle).Section[0].ElementId, projected.Identity);
        Assert.False(string.IsNullOrWhiteSpace(projected.Identity));
    }

    [Fact]
    public void FN_CC_010_nested_sections_are_projected_too()
    {
        // A leaflet's sections nest, and a projection that showed only the top level would show
        // an author two thirds of a label without saying so.
        var nested = Section("2. Before", "Read this.");
        nested.Section = [Section("2.1 Warnings", "Do not exceed.")];
        var bundle = Document(Section("1. What it is", "A medicine."), nested);

        var projected = SectionProjection.Of(bundle);

        Assert.Equal(["1. What it is", "2. Before", "2.1 Warnings"], projected.Select(s => s.Title));
    }

    [Fact]
    public void FN_CC_010_a_section_with_no_narrative_projects_as_empty_rather_than_missing()
    {
        // A section an author has not written yet is normal. Omitting it would be a section
        // they assume does not exist, and its absence from a save would delete it.
        var bundle = Document(new Composition.SectionComponent { Title = "3. Dosage" });

        var projected = Assert.Single(SectionProjection.Of(bundle));

        Assert.Equal("3. Dosage", projected.Title);
        Assert.Equal(string.Empty, projected.Narrative);
    }

    [Fact]
    public void FN_CC_010_a_saved_section_changes_only_what_the_author_changed()
    {
        var bundle = Document(Section("1. What it is", "A medicine."), Section("2. Before", "Read this."));
        var identity = CompositionOf(bundle).Section[0].ElementId!;

        var patched = SectionProjection.Apply(
            bundle,
            [new ProjectedSection(identity, "1. What it is", NarrativeOf("A medicine for adults."))]);

        Assert.Contains("A medicine for adults.", CompositionOf(patched).Section[0].Text!.Div);
        Assert.Contains("Read this.", CompositionOf(patched).Section[1].Text!.Div);
    }

    [Fact]
    public void FN_CC_010_everything_the_projection_does_not_model_survives_a_save()
    {
        // The case ADR-038 decision 2 exists for. A Bundle carries a great deal the projection
        // does not, and rebuilding one from the other would discard regulated content because a
        // web form had no field for it.
        var bundle = Document(Section("1. What it is", "A medicine."));
        var composition = CompositionOf(bundle);
        composition.Subject = [new ResourceReference { Display = "SYNTHETIC - Examplinum" }];
        composition.Type = new CodeableConcept("http://example.org/synthetic", "package-leaflet");
        var identity = composition.Section[0].ElementId!;

        var patched = SectionProjection.Apply(
            bundle, [new ProjectedSection(identity, "1. What it is", NarrativeOf("Changed."))]);

        var after = CompositionOf(patched);
        Assert.Equal("SYNTHETIC - Examplinum", after.Subject[0].Display);
        Assert.Equal("package-leaflet", after.Type.Coding[0].Code);
        Assert.Equal("en-GB", after.Language);
    }

    [Fact]
    public void FN_CC_010_a_save_leaves_the_version_it_was_read_from_untouched()
    {
        // Immutability is the platform's, and a patch that mutated its input would make the
        // version in memory disagree with the one in the store.
        var bundle = Document(Section("1. What it is", "A medicine."));
        var identity = CompositionOf(bundle).Section[0].ElementId!;

        SectionProjection.Apply(
            bundle, [new ProjectedSection(identity, "1. What it is", NarrativeOf("Changed."))]);

        Assert.Contains("A medicine.", CompositionOf(bundle).Section[0].Text!.Div);
    }

    [Fact]
    public void FN_CC_010_a_save_naming_a_section_that_is_not_there_is_refused()
    {
        // Adding a section is a different operation from editing one, with different rules.
        // Letting a save do it by accident is how a label acquires a section nobody approved
        // (ADR-038 decision 4).
        var bundle = Document(Section("1. What it is", "A medicine."));

        var refusal = Assert.Throws<ArgumentException>(() => SectionProjection.Apply(
            bundle, [new ProjectedSection("sec-not-here", "Invented", NarrativeOf("..."))]));

        Assert.Contains("sec-not-here", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void FN_CC_010_a_nested_section_can_be_saved_as_readily_as_a_top_level_one()
    {
        var nested = Section("2. Before", "Read this.");
        nested.Section = [Section("2.1 Warnings", "Do not exceed.")];
        var bundle = Document(nested);
        var identity = CompositionOf(bundle).Section[0].Section[0].ElementId!;

        var patched = SectionProjection.Apply(
            bundle, [new ProjectedSection(identity, "2.1 Warnings", NarrativeOf("Do not exceed two."))]);

        Assert.Contains(
            "Do not exceed two.", CompositionOf(patched).Section[0].Section[0].Text!.Div);
    }

    [Fact]
    public void FN_CC_010_a_title_the_author_changed_is_saved_with_the_narrative()
    {
        var bundle = Document(Section("1. What it is", "A medicine."));
        var identity = CompositionOf(bundle).Section[0].ElementId!;

        var patched = SectionProjection.Apply(
            bundle, [new ProjectedSection(identity, "1. What Examplinum is", NarrativeOf("A medicine."))]);

        Assert.Equal("1. What Examplinum is", CompositionOf(patched).Section[0].Title);
    }

    [Fact]
    public void FN_CC_010_saving_nothing_changes_nothing()
    {
        var bundle = Document(Section("1. What it is", "A medicine."));

        var patched = SectionProjection.Apply(bundle, []);

        Assert.Contains("A medicine.", CompositionOf(patched).Section[0].Text!.Div);
    }

    private static string NarrativeOf(string words) =>
        $"<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>{words}</p></div>";
}
