using Hl7.Fhir.Model;
using Xunit;

namespace Epi.ContentCore.Tests;

// Identifiers content arrived with, kept but never acted on (ADR-027).
//   CAP-SCM-007 Immutable versions with a stable identity of the platform's own
public sealed class SecondaryIdentifiersTests
{
    private static readonly SecondaryIdentifier Legacy =
        new("https://legacy.example.test/labels", "SPL-000123");

    private static Bundle Document() => new()
    {
        Type = Bundle.BundleType.Document,
        Entry = [new Bundle.EntryComponent
        {
            Resource = new Composition { Title = "SYNTHETIC TEST LABEL" },
        }],
    };

    [Fact]
    public void CAP_SCM_007_an_identifier_the_content_arrived_with_is_kept()
    {
        // A migrated label whose legacy identifier is lost cannot be reconciled against the
        // system it came from, which is the check a migration has to pass to be trusted.
        var bundle = SecondaryIdentifiers.Add(Document(), Legacy);

        Assert.Equal(Legacy, Assert.Single(SecondaryIdentifiers.Of(bundle)));
    }

    [Fact]
    public void CAP_SCM_007_a_secondary_identifier_never_touches_the_platforms_own_slot()
    {
        // Bundle.identifier is the platform's, always and only (ADR-015).
        var bundle = SecondaryIdentifiers.Add(Document(), Legacy);

        Assert.Null(bundle.Identifier);
    }

    [Fact]
    public void CAP_SCM_007_a_secondary_identifier_in_the_platforms_own_system_is_refused()
    {
        // It would be indistinguishable from minted identity everywhere downstream, which is
        // the one thing identity has to be safe from.
        var claiming = new SecondaryIdentifier(
            IdentifierAuthority.Demonstration.DocumentSystem, "01a00000-0000-7000-8000-00000000000a");

        Assert.Throws<InvalidEpiBundleException>(
            () => SecondaryIdentifiers.Add(Document(), claiming));
    }

    [Fact]
    public void CAP_SCM_007_the_same_identifier_recorded_twice_is_kept_once()
    {
        var bundle = SecondaryIdentifiers.Add(SecondaryIdentifiers.Add(Document(), Legacy), Legacy);

        Assert.Single(SecondaryIdentifiers.Of(bundle));
    }

    [Fact]
    public void CAP_SCM_007_content_may_carry_identifiers_from_several_systems()
    {
        var submitter = new SecondaryIdentifier("https://affiliate.example.test/refs", "GB-2026-114");

        var bundle = SecondaryIdentifiers.Add(SecondaryIdentifiers.Add(Document(), Legacy), submitter);

        Assert.Equal([Legacy, submitter], SecondaryIdentifiers.Of(bundle));
    }

    [Fact]
    public void CAP_SCM_007_a_platform_identifier_already_in_the_content_is_not_read_back_as_secondary()
    {
        // Content restored from a backup taken before this rule existed must not start reading
        // as though it had submitted its own identity.
        var bundle = Document();
        ((Composition)bundle.Entry[0].Resource!).Identifier.Add(
            new Identifier(IdentifierAuthority.Demonstration.DocumentSystem, "smuggled"));

        Assert.Empty(SecondaryIdentifiers.Of(bundle));
    }

    [Fact]
    public void CAP_SCM_007_two_documents_may_carry_the_same_secondary_identifier()
    {
        // A legacy system that reused an identifier is a fact to record, not an error to
        // reject: nothing resolves by a secondary identifier, so nothing is made ambiguous.
        var first = SecondaryIdentifiers.Add(Document(), Legacy);
        var second = SecondaryIdentifiers.Add(Document(), Legacy);

        Assert.Equal(SecondaryIdentifiers.Of(first), SecondaryIdentifiers.Of(second));
    }

    [Fact]
    public void CAP_SCM_007_content_with_no_anchoring_composition_cannot_carry_one()
    {
        var empty = new Bundle { Type = Bundle.BundleType.Document };

        Assert.Throws<InvalidEpiBundleException>(() => SecondaryIdentifiers.Add(empty, Legacy));
    }

    [Fact]
    public void CAP_SCM_010_secondary_identifiers_survive_a_round_trip()
    {
        // They are content the platform deliberately does not act on, and round-trip fidelity
        // is exactly the guarantee that such content is not quietly dropped.
        var bundle = SecondaryIdentifiers.Add(Document(), Legacy);

        var read = EpiBundleReader.Read(EpiBundleReader.Write(bundle));

        Assert.Equal(Legacy, Assert.Single(SecondaryIdentifiers.Of(read)));
    }
}
