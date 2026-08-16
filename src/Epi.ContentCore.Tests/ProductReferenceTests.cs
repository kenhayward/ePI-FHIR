using Hl7.Fhir.Model;
using Xunit;

namespace Epi.ContentCore.Tests;

// What a label says about the product it is about (FN-CC-011).
//   CAP-SCM-011 Associate a label document with its product identity
//   CAP-MDM-003 Maintain the product to label association model
//
// ADR-040, paying the debt ADR-036 recorded against itself: the directory can answer and nothing
// on the write path asks it. What was there was a string somebody typed - unresolvable,
// incomparable across labels, and no use for "which labels are about this product".
public sealed class ProductReferenceTests
{
    private static readonly IdentifierAuthority Authority = IdentifierAuthority.Demonstration;

    private static Bundle Document() => new()
    {
        Type = Bundle.BundleType.Document,
        Entry = [new Bundle.EntryComponent { Resource = new Composition { Title = "A leaflet" } }],
    };

    private static Composition CompositionOf(Bundle bundle) =>
        Assert.IsType<Composition>(bundle.Entry[0].Resource);

    [Fact]
    public void FN_CC_011_a_label_records_which_product_it_is_about()
    {
        var stamped = ProductReference.Stamp(
            Document(), new ProductReference("PROD-0001", "SYNTHETIC - Examplinum 10 mg"), Authority);

        var read = ProductReference.Of(stamped, Authority);

        Assert.NotNull(read);
        Assert.Equal("PROD-0001", read!.Identifier);
        Assert.Equal("SYNTHETIC - Examplinum 10 mg", read.Display);
    }

    [Fact]
    public void FN_CC_011_the_identifier_is_written_in_the_configured_product_system()
    {
        // Never a bare string. An identifier with no system is an identifier nobody can resolve
        // against anything (ADR-017).
        var stamped = ProductReference.Stamp(
            Document(), new ProductReference("PROD-0001", "A product"), Authority);

        var subject = Assert.Single(CompositionOf(stamped).Subject);
        Assert.Equal(Authority.ProductSystem, subject.Identifier!.System);
        Assert.Equal("PROD-0001", subject.Identifier.Value);
    }

    [Fact]
    public void FN_CC_011_a_label_about_no_product_says_nothing_rather_than_nothing_useful()
    {
        // A template instantiated before anybody chose a product is a normal state of affairs,
        // and an absent subject is absent rather than an error (ADR-040 decision 5).
        Assert.Null(ProductReference.Of(Document(), Authority));
    }

    [Fact]
    public void FN_CC_011_a_display_with_no_identifier_beside_it_does_not_resolve_to_a_product()
    {
        // Content written before ADR-040 has exactly this shape. It is readable and it is not
        // resolvable, and pretending otherwise would make an unresolvable label look resolved.
        var legacy = Document();
        CompositionOf(legacy).Subject = [new ResourceReference { Display = "Typed by somebody" }];

        Assert.Null(ProductReference.Of(legacy, Authority));
    }

    [Fact]
    public void FN_CC_011_the_display_is_carried_for_a_reader_and_is_not_what_resolves()
    {
        // A copy of what the directory said when the reference was written, and copies go stale.
        // Reading by identifier is what makes a stale display harmless.
        var stamped = ProductReference.Stamp(
            Document(), new ProductReference("PROD-0001", "The name at the time"), Authority);

        CompositionOf(stamped).Subject[0].Display = "A different name entirely";

        Assert.Equal("PROD-0001", ProductReference.Of(stamped, Authority)!.Identifier);
    }

    [Fact]
    public void FN_CC_011_stamping_a_product_replaces_the_one_that_was_there()
    {
        // A label is about one product. Two subjects would make "which labels are about this
        // product" answer twice for one label, and neither answer would be wrong.
        var stamped = ProductReference.Stamp(
            Document(), new ProductReference("PROD-0001", "First"), Authority);

        var restamped = ProductReference.Stamp(
            stamped, new ProductReference("PROD-0002", "Second"), Authority);

        Assert.Single(CompositionOf(restamped).Subject);
        Assert.Equal("PROD-0002", ProductReference.Of(restamped, Authority)!.Identifier);
    }

    [Fact]
    public void FN_CC_011_stamping_leaves_the_document_it_was_given_alone()
    {
        var original = Document();

        ProductReference.Stamp(original, new ProductReference("PROD-0001", "A product"), Authority);

        Assert.Empty(CompositionOf(original).Subject);
    }

    [Fact]
    public void FN_CC_011_a_reference_with_no_identifier_is_refused()
    {
        // The identifier is what makes it a reference. A display alone is the thing this exists
        // to replace.
        Assert.Throws<ArgumentException>(() => new ProductReference(" ", "A product"));
    }

    [Fact]
    public void FN_CC_011_a_deployment_with_no_product_system_configured_is_refused()
    {
        // Rather than writing an identifier into a namespace nobody owns.
        var unset = Authority with { ProductSystem = "" };

        Assert.Throws<ArgumentException>(() => ProductReference.Stamp(
            Document(), new ProductReference("PROD-0001", "A product"), unset));
    }
}
