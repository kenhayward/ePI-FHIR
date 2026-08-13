using Hl7.Fhir.Model;
using Xunit;

namespace Epi.ContentCore.Tests;

// FN-CC-008 Assign a stable identifier to every section at creation
// FN-CC-009 Preserve section identifiers across versions
//
// ADR-015 decision 6: section identifiers are assigned on creation and preserved thereafter,
// through editing, through new versions, and through translation. Change impact (CAP-CHG-006)
// and cross-references (CAP-SCM-005) both address sections, and neither can work if a section's
// identity moves when the document is revised.
public sealed class SectionIdentityTests
{
    private static Bundle Document() => EpiBundleReader.Read(
        File.ReadAllText(TestFixtures.Path("epi", "minimal-epi-document.json")));

    private static Composition CompositionOf(Bundle bundle) =>
        Assert.IsType<Composition>(bundle.Entry[0].Resource);

    [Fact]
    public void FN_CC_008_every_section_is_given_an_identifier()
    {
        var bundle = SectionIdentity.AssignMissing(Document());

        Assert.All(CompositionOf(bundle).Section,
            section => Assert.False(string.IsNullOrWhiteSpace(section.ElementId)));
    }

    [Fact]
    public void FN_CC_008_identifiers_are_opaque_and_distinct()
    {
        var bundle = SectionIdentity.AssignMissing(Document());

        var ids = CompositionOf(bundle).Section.Select(s => s.ElementId).ToList();
        Assert.Equal(ids.Count, ids.Distinct().Count());
        Assert.All(ids, id => Assert.True(Guid.TryParse(id, out _),
            "Section identifiers should be opaque, like document identifiers (ADR-015)."));
    }

    [Fact]
    public void FN_CC_008_nested_sections_are_identified_too()
    {
        // A section within a section is still a section: impact analysis addresses it, so it
        // needs identity as much as a top-level one does.
        var bundle = Document();
        CompositionOf(bundle).Section[0].Section.Add(new Composition.SectionComponent
        {
            Title = "1.1 A nested subsection",
        });

        var assigned = SectionIdentity.AssignMissing(bundle);

        Assert.False(string.IsNullOrWhiteSpace(CompositionOf(assigned).Section[0].Section[0].ElementId));
    }

    [Fact]
    public void FN_CC_009_an_identifier_that_is_already_present_is_left_alone()
    {
        var bundle = Document();
        CompositionOf(bundle).Section[0].ElementId = "0195f3a0-0000-7000-8000-00000000beef";

        var assigned = SectionIdentity.AssignMissing(bundle);

        Assert.Equal("0195f3a0-0000-7000-8000-00000000beef", CompositionOf(assigned).Section[0].ElementId);
    }

    [Fact]
    public void FN_CC_009_assigning_twice_changes_nothing_the_second_time()
    {
        // The operation runs on every write, so it has to be idempotent: a section identifier
        // that moved on the second save would be worse than none at all.
        var first = SectionIdentity.AssignMissing(Document());
        var before = CompositionOf(first).Section.Select(s => s.ElementId).ToList();

        var second = SectionIdentity.AssignMissing(first);

        Assert.Equal(before, CompositionOf(second).Section.Select(s => s.ElementId));
    }

    [Fact]
    public void FN_CC_009_a_new_section_added_later_is_identified_without_disturbing_the_others()
    {
        var bundle = SectionIdentity.AssignMissing(Document());
        var existing = CompositionOf(bundle).Section[0].ElementId;
        CompositionOf(bundle).Section.Add(new Composition.SectionComponent { Title = "3. Added later" });

        var assigned = SectionIdentity.AssignMissing(bundle);

        Assert.Equal(existing, CompositionOf(assigned).Section[0].ElementId);
        Assert.False(string.IsNullOrWhiteSpace(CompositionOf(assigned).Section[^1].ElementId));
    }
}
