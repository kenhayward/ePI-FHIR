using Hl7.Fhir.Model;
using Xunit;

namespace Epi.ContentCore.Tests;

// Translation as a variant, linked to a source version (ADR-032).
//   CAP-LOC-001 Market variants (country x language x regulator) linked to a source label
//   CAP-LOC-005 Translations flagged stale when the source moves
public sealed class VariantsTests
{
    private static readonly DocumentIdentity English =
        new("https://epi.example.org/identifier/document", "01a00000-0000-7000-8000-00000000000a");

    private static Bundle Document() => new()
    {
        Type = Bundle.BundleType.Document,
        Entry = [new Bundle.EntryComponent
        {
            Resource = new Composition { Title = "SYNTHETIC TEST LABEL" },
        }],
    };

    [Fact]
    public void CAP_LOC_001_a_variant_records_the_source_version_it_was_translated_from()
    {
        var bundle = Variants.MarkAsVariant(Document(), new VariantOf(English, 3, "fr"));

        var variant = Variants.Of(bundle);

        Assert.NotNull(variant);
        Assert.Equal(English, variant!.Source);
        Assert.Equal(3, variant.SourceVersion);
        Assert.Equal("fr", variant.Language);
    }

    [Fact]
    public void CAP_LOC_001_a_variant_may_name_its_country_and_regulator()
    {
        // Language is what makes a variant a translation; country and regulator are what make
        // it a market variant, and a variant in the same language for a different regulator is
        // the same shape rather than a special case.
        var bundle = Variants.MarkAsVariant(
            Document(), new VariantOf(English, 3, "fr", "BE", "FAMHP"));

        var variant = Variants.Of(bundle);

        Assert.Equal("BE", variant!.Country);
        Assert.Equal("FAMHP", variant.Regulator);
    }

    [Fact]
    public void CAP_LOC_001_the_language_is_on_the_content_and_not_only_in_a_tag()
    {
        // A consumer reading the document without knowing this platform's namespaces still has
        // to be able to tell what language it is in.
        var bundle = Variants.MarkAsVariant(Document(), new VariantOf(English, 3, "fr"));

        Assert.Equal("fr", ((Composition)bundle.Entry[0].Resource!).Language);
    }

    [Fact]
    public void CAP_LOC_001_content_that_is_a_source_in_its_own_right_says_so()
    {
        Assert.Null(Variants.Of(Document()));
    }

    [Fact]
    public void CAP_LOC_001_a_variant_must_name_a_source_version()
    {
        // A link to "the English label" without a version points at whatever that label says
        // today, and a translation is a translation of something specific.
        Assert.Throws<ArgumentException>(
            () => Variants.MarkAsVariant(Document(), new VariantOf(English, 0, "fr")));
    }

    [Fact]
    public void CAP_LOC_001_marking_a_variant_twice_leaves_one_link()
    {
        var bundle = Variants.MarkAsVariant(
            Variants.MarkAsVariant(Document(), new VariantOf(English, 3, "fr")),
            new VariantOf(English, 4, "fr"));

        Assert.Equal(4, Variants.Of(bundle)!.SourceVersion);
        Assert.Single(bundle.Meta!.Tag, t => t.System!.EndsWith("variant-source", StringComparison.Ordinal));
    }

    [Fact]
    public void CAP_LOC_001_a_link_the_platform_cannot_read_as_a_version_is_not_a_link()
    {
        // Reading it as "the latest" would silently turn every malformed link into a moving
        // target, which is the opposite of what pinning a source version is for.
        var bundle = Document();
        bundle.Meta = new Meta
        {
            Tag =
            [
                new Coding(IdentifierAuthority.Demonstration.VariantSourceTagSystem, "no-version-here"),
                new Coding(IdentifierAuthority.Demonstration.VariantScopeTagSystem, "fr||"),
            ],
        };

        Assert.Null(Variants.Of(bundle));
    }

    [Fact]
    public void CAP_LOC_005_a_variant_is_stale_exactly_when_its_source_has_moved_on()
    {
        // Derived by comparison when asked, never written onto the variant: a flag would modify
        // approved content to record a fact about a different document (ADR-032 decision 5).
        var variant = new VariantOf(English, 3, "fr");

        Assert.False(Variants.IsStale(variant, 3));
        Assert.True(Variants.IsStale(variant, 4));
    }

    [Fact]
    public void CAP_LOC_005_asking_whether_a_variant_is_stale_does_not_change_it()
    {
        var bundle = Variants.MarkAsVariant(Document(), new VariantOf(English, 3, "fr"));
        var before = EpiBundleReader.Write(bundle);

        _ = Variants.IsStale(Variants.Of(bundle)!, 9);

        Assert.Equal(before, EpiBundleReader.Write(bundle));
    }

    [Fact]
    public void CAP_LOC_001_variants_are_recorded_in_the_deployments_own_namespaces()
    {
        var authority = IdentifierAuthority.Demonstration with
        {
            VariantSourceTagSystem = "https://labels.example.net/tag/variant-source",
            VariantScopeTagSystem = "https://labels.example.net/tag/variant-scope",
        };

        var bundle = Variants.MarkAsVariant(
            Document(), new VariantOf(English, 3, "fr"), authority);

        Assert.NotNull(Variants.Of(bundle, authority));
        Assert.Null(Variants.Of(bundle));
    }

    [Fact]
    public void CAP_LOC_001_a_deployment_naming_no_variant_namespaces_cannot_record_one()
    {
        var unconfigured = IdentifierAuthority.Demonstration with
        {
            VariantSourceTagSystem = string.Empty,
            VariantScopeTagSystem = string.Empty,
        };

        Assert.Throws<InvalidOperationException>(
            () => Variants.MarkAsVariant(Document(), new VariantOf(English, 3, "fr"), unconfigured));
    }

    [Fact]
    public void CAP_SCM_007_section_identity_is_carried_over_from_the_source_unchanged()
    {
        // What makes section-level comparison possible at all, and why a cross-reference copied
        // into a translation resolves there (ADR-015 decision 6, ADR-028).
        var source = Document();
        ((Composition)source.Entry[0].Resource!).Section =
            [new Composition.SectionComponent { ElementId = "sec-44", Title = "4.4 Warnings" }];

        var translated = (Bundle)source.DeepCopy();
        ((Composition)translated.Entry[0].Resource!).Section[0].Title = "4.4 Mises en garde";
        Variants.MarkAsVariant(translated, new VariantOf(English, 1, "fr"));

        Assert.Equal(
            "sec-44", ((Composition)translated.Entry[0].Resource!).Section[0].ElementId);
    }
}
