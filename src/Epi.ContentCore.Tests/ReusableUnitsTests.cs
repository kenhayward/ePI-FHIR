using Hl7.Fhir.Model;
using Xunit;

namespace Epi.ContentCore.Tests;

// Reusable content units and the references that use them (ADR-026).
//   CAP-SCM-004 A content-reuse mechanism: referenceable shared content units
public sealed class ReusableUnitsTests
{
    private static readonly DocumentIdentity Warning =
        new("https://epi.example.org/identifier/document", "01a00000-0000-7000-8000-00000000000a");

    private static Composition.SectionComponent Section(string title = "4.4 Special warnings") =>
        new() { Title = title };

    private static Bundle Document(params Composition.SectionComponent[] sections) => new()
    {
        Type = Bundle.BundleType.Document,
        Entry = [new Bundle.EntryComponent
        {
            Resource = new Composition { Title = "SYNTHETIC TEST LABEL", Section = [.. sections] },
        }],
    };

    [Fact]
    public void CAP_SCM_004_a_unit_says_what_it_is_and_a_label_does_not()
    {
        // Units go through every gate a label does; the only thing that differs is what they
        // are for, so a tag is what distinguishes them rather than a separate store.
        Assert.True(ReusableUnits.IsUnit(ReusableUnits.MarkAsUnit(Document())));
        Assert.False(ReusableUnits.IsUnit(Document()));
    }

    [Fact]
    public void CAP_SCM_004_marking_a_unit_twice_leaves_one_mark()
    {
        var unit = ReusableUnits.MarkAsUnit(ReusableUnits.MarkAsUnit(Document()));

        Assert.Single(unit.Meta!.Tag, t => t.Code == "reusable-unit");
    }

    [Fact]
    public void CAP_SCM_004_a_section_records_the_unit_and_version_it_borrows()
    {
        var section = ReusableUnits.Borrow(Section(), new UnitReference(Warning, 3));

        var borrowed = ReusableUnits.BorrowedBy(section);

        Assert.NotNull(borrowed);
        Assert.Equal(Warning, borrowed!.Unit);
        Assert.Equal(3, borrowed.Version);
        Assert.Equal(UnitResolution.Pinned, borrowed.Resolution);
    }

    [Fact]
    public void CAP_SCM_004_pinned_is_the_default_and_track_latest_is_stated()
    {
        // ADR-007's policy, in the data: a reference that says nothing is pinned. The opposite
        // default would make every reference follow a unit nobody asked it to follow.
        Assert.Equal(UnitResolution.Pinned,
            ReusableUnits.BorrowedBy(ReusableUnits.Borrow(Section(), new UnitReference(Warning, 3)))!.Resolution);

        Assert.Equal(UnitResolution.TrackLatest,
            ReusableUnits.BorrowedBy(ReusableUnits.Borrow(
                Section(), new UnitReference(Warning, 3, UnitResolution.TrackLatest)))!.Resolution);
    }

    [Fact]
    public void CAP_SCM_004_a_reference_without_a_version_is_refused()
    {
        // A reference with no version points at whatever the unit says today, which is the
        // opposite of pinned (ADR-026 decision 2).
        Assert.Throws<ArgumentException>(
            () => ReusableUnits.Borrow(Section(), new UnitReference(Warning, 0)));
    }

    [Fact]
    public void CAP_SCM_004_a_reference_the_platform_cannot_read_as_a_pin_is_not_one()
    {
        // Reading a malformed reference as "the latest" would turn every one of them into a
        // track-latest reference silently, which is the wrong default in the wrong direction.
        var section = Section();
        section.Entry = [new ResourceReference { Identifier = new Identifier(Warning.System, Warning.Value) }];

        Assert.Null(ReusableUnits.BorrowedBy(section));
    }

    [Fact]
    public void CAP_SCM_004_borrowing_again_replaces_the_reference_rather_than_adding_one()
    {
        // Two references on one section would make "which version does this use" ambiguous,
        // and something downstream would pick one.
        var section = ReusableUnits.Borrow(Section(), new UnitReference(Warning, 3));

        ReusableUnits.Borrow(section, new UnitReference(Warning, 4));

        Assert.Equal(4, ReusableUnits.BorrowedBy(section)!.Version);
        Assert.Single(section.Entry);
    }

    [Fact]
    public void CAP_SCM_004_a_section_that_borrows_nothing_says_so()
    {
        Assert.Null(ReusableUnits.BorrowedBy(Section()));
    }

    [Fact]
    public void CAP_SCM_004_every_reference_in_a_document_is_found_including_nested_sections()
    {
        // What this label depends on. Answering it from the top level only would miss exactly
        // the sections a structured label puts its warnings in.
        var nested = ReusableUnits.Borrow(Section("4.4.1 Paediatric"), new UnitReference(Warning, 7));
        var parent = Section("4.4 Special warnings");
        parent.Section = [nested];

        var borrowed = ReusableUnits.BorrowedIn(Document(
            ReusableUnits.Borrow(Section("2. Composition"), new UnitReference(Warning, 3)),
            parent));

        Assert.Equal([3, 7], borrowed.Select(b => b.Version));
    }

    [Fact]
    public void CAP_SCM_004_references_are_written_into_the_deployments_own_namespaces()
    {
        // ADR-017: identifiers and extensions belong to the adopting organisation, and content
        // written into someone else's namespace is content nobody can interpret.
        var authority = IdentifierAuthority.Demonstration with
        {
            UnitSystem = "https://labels.example.net/tag/unit",
            UnitReferenceExtension = "https://labels.example.net/extension/unit-reference",
        };

        var section = ReusableUnits.Borrow(Section(), new UnitReference(Warning, 3), authority);

        Assert.NotNull(ReusableUnits.BorrowedBy(section, authority));
        Assert.Null(ReusableUnits.BorrowedBy(section));
    }

    [Fact]
    public void CAP_SCM_004_a_deployment_that_names_no_unit_namespaces_cannot_record_a_reference()
    {
        // ADR-017 refuses partial configuration: writing into an empty namespace is worse than
        // refusing, because it looks like it worked.
        var unconfigured = IdentifierAuthority.Demonstration with
        {
            UnitSystem = string.Empty,
            UnitReferenceExtension = string.Empty,
        };

        Assert.Throws<InvalidOperationException>(
            () => ReusableUnits.Borrow(Section(), new UnitReference(Warning, 3), unconfigured));
    }
}
