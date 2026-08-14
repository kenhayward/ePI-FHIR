using Hl7.Fhir.Model;
using Xunit;

namespace Epi.ContentCore.Tests;

// Borrowed text placed into the label that borrows it, once, on the way in (ADR-026).
//   CAP-SCM-004 A content-reuse mechanism: referenceable shared content units
public sealed class MaterialisingContentStoreTests
{
    private static Narrative Says(string text) => new()
    {
        Status = Narrative.NarrativeStatus.Generated,
        Div = $"<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>{text}</p></div>",
    };

    private static Bundle Unit(string text) => ReusableUnits.MarkAsUnit(new Bundle
    {
        Type = Bundle.BundleType.Document,
        Entry = [new Bundle.EntryComponent
        {
            Resource = new Composition
            {
                Title = "SYNTHETIC - class warning",
                Section = [new Composition.SectionComponent { Title = "Warning", Text = Says(text) }],
            },
        }],
    });

    private static Bundle Label(params Composition.SectionComponent[] sections) => new()
    {
        Type = Bundle.BundleType.Document,
        Entry = [new Bundle.EntryComponent
        {
            Resource = new Composition { Title = "SYNTHETIC TEST LABEL", Section = [.. sections] },
        }],
    };

    private static Composition.SectionComponent Borrowing(
        DocumentIdentity unit, int version, UnitResolution resolution = UnitResolution.Pinned) =>
        ReusableUnits.Borrow(
            new Composition.SectionComponent { Title = "4.4 Special warnings" },
            new UnitReference(unit, version, resolution));

    private static string TextOf(EpiDocument document, int section = 0) =>
        ((Composition)document.Bundle.Entry[0].Resource!).Section[section].Text?.Div ?? string.Empty;

    /// <summary>A store whose units and labels live side by side, as ADR-026 decision 1 has it.</summary>
    private static (MaterialisingContentStore Store, InMemoryContentStore Backing) Build()
    {
        var backing = new InMemoryContentStore();
        return (new MaterialisingContentStore(backing, backing), backing);
    }

    [Fact]
    public async Task CAP_SCM_004_borrowed_text_is_placed_into_the_section_that_borrows_it()
    {
        var (store, backing) = Build();
        var unit = await backing.CreateAsync(ContentIdentity.Mint(), Unit("Do not exceed the stated dose."));

        var label = await store.CreateAsync(
            ContentIdentity.Mint(), Label(Borrowing(unit.Identity, 1)));

        Assert.Contains("Do not exceed the stated dose", TextOf(label), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CAP_SCM_004_a_later_unit_version_does_not_change_a_label_already_written()
    {
        // Acceptance criterion 1, and the whole point of pinning: the bytes were fixed when the
        // label was written, so nothing about a later unit version can reach back into it.
        var (store, backing) = Build();
        var unit = await backing.CreateAsync(ContentIdentity.Mint(), Unit("The original warning."));

        var label = await store.CreateAsync(
            ContentIdentity.Mint(), Label(Borrowing(unit.Identity, 1)));

        await backing.CreateVersionAsync(unit.Identity, 2, Unit("A revised warning."));

        var reread = await store.GetAsync(label.Identity, 1);
        Assert.Contains("The original warning", TextOf(reread!), StringComparison.Ordinal);
        Assert.DoesNotContain("revised", TextOf(reread!), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CAP_SCM_004_track_latest_takes_the_newest_version_and_records_which_one()
    {
        // Acceptance criterion 2: the change happens through a new label version written on
        // purpose, and the stored reference names the version it actually used rather than an
        // intent that could resolve differently later.
        var (store, backing) = Build();
        var unit = await backing.CreateAsync(ContentIdentity.Mint(), Unit("The original warning."));
        await backing.CreateVersionAsync(unit.Identity, 2, Unit("A revised warning."));

        var label = await store.CreateAsync(
            ContentIdentity.Mint(),
            Label(Borrowing(unit.Identity, 1, UnitResolution.TrackLatest)));

        Assert.Contains("A revised warning", TextOf(label), StringComparison.Ordinal);

        var recorded = ReusableUnits.BorrowedIn(label.Bundle);
        Assert.Equal(2, Assert.Single(recorded).Version);
    }

    [Fact]
    public async Task CAP_SCM_004_a_unit_that_cannot_be_seen_is_refused_rather_than_skipped()
    {
        // Missing and out of scope give the same answer, so borrowing cannot be used to learn
        // that a unit exists - and a silently empty section would be a label with a warning
        // nobody wrote in place of one somebody did.
        var (store, _) = Build();
        var absent = ContentIdentity.Mint();

        await Assert.ThrowsAsync<UnitNotAvailableException>(
            () => store.CreateAsync(ContentIdentity.Mint(), Label(Borrowing(absent, 1))));
    }

    [Fact]
    public async Task CAP_SCM_004_borrowing_from_a_label_is_refused()
    {
        // It would take that label's text without any of the relationship reuse exists to
        // record, and nothing afterwards could tell the passage had been borrowed at all.
        var (store, backing) = Build();
        var notAUnit = await backing.CreateAsync(
            ContentIdentity.Mint(), Label(new Composition.SectionComponent { Text = Says("Some text.") }));

        var error = await Assert.ThrowsAsync<UnitNotAvailableException>(
            () => store.CreateAsync(ContentIdentity.Mint(), Label(Borrowing(notAUnit.Identity, 1))));

        Assert.Contains("not a reusable content unit", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CAP_SCM_004_a_unit_holding_more_than_one_section_is_ambiguous_and_refused()
    {
        var (store, backing) = Build();
        var twoSections = ReusableUnits.MarkAsUnit(Label(
            new Composition.SectionComponent { Text = Says("First.") },
            new Composition.SectionComponent { Text = Says("Second.") }));
        var unit = await backing.CreateAsync(ContentIdentity.Mint(), twoSections);

        var error = await Assert.ThrowsAsync<UnitNotAvailableException>(
            () => store.CreateAsync(ContentIdentity.Mint(), Label(Borrowing(unit.Identity, 1))));

        Assert.Contains("exactly one section", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CAP_SCM_004_a_label_that_borrows_nothing_is_stored_unchanged()
    {
        var (store, _) = Build();
        var plain = Label(new Composition.SectionComponent { Title = "1. Name", Text = Says("Examplinum.") });

        var stored = await store.CreateAsync(ContentIdentity.Mint(), plain);

        Assert.Contains("Examplinum", TextOf(stored), StringComparison.Ordinal);
        Assert.Empty(ReusableUnits.BorrowedIn(stored.Bundle));
    }

    [Fact]
    public async Task CAP_SCM_004_a_nested_section_borrows_too()
    {
        var (store, backing) = Build();
        var unit = await backing.CreateAsync(ContentIdentity.Mint(), Unit("Paediatric warning."));

        var parent = new Composition.SectionComponent
        {
            Title = "4.4 Special warnings",
            Section = [Borrowing(unit.Identity, 1)],
        };

        var label = await store.CreateAsync(ContentIdentity.Mint(), Label(parent));

        var nested = ((Composition)label.Bundle.Entry[0].Resource!).Section[0].Section[0];
        Assert.Contains("Paediatric warning", nested.Text!.Div, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CAP_SCM_004_the_reference_survives_alongside_the_text_it_brought_in()
    {
        // Without it the stored label would be indistinguishable from one whose author typed
        // the passage, and change impact would be reduced to text search.
        var (store, backing) = Build();
        var unit = await backing.CreateAsync(ContentIdentity.Mint(), Unit("A borrowed warning."));

        var label = await store.CreateAsync(
            ContentIdentity.Mint(), Label(Borrowing(unit.Identity, 1)));

        var borrowed = Assert.Single(ReusableUnits.BorrowedIn(label.Bundle));
        Assert.Equal(unit.Identity, borrowed.Unit);
        Assert.Equal(1, borrowed.Version);
    }
}
