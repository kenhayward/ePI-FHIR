using Epi.ContentCore;
using Hl7.Fhir.Model;
using Xunit;

namespace Epi.Search.Tests;

// FN-SCH-001 What of a document is searchable, read from the content itself
public sealed class SearchableContentTests
{
    [Fact]
    public void FN_SCH_001_the_searchable_metadata_comes_from_the_content()
    {
        var document = SearchFixtures.Document("doc-1", 1, SearchFixtures.Uk);

        var searchable = SearchableContent.Of(document.Bundle);

        Assert.Equal("SYNTHETIC TEST LABEL - Examplinum 10 mg tablets", searchable.Title);
        Assert.Equal(SearchFixtures.Uk, searchable.Scope);
        Assert.Equal("en-GB", searchable.Language);
        Assert.Equal("Examplinum 10 mg tablets", searchable.Product);
        Assert.Equal("package-leaflet", searchable.DocumentType);
    }

    [Fact]
    public void CAP_SCH_003_the_searchable_text_includes_the_section_narrative_without_its_markup()
    {
        // Matching on markup would let a query for "div" return the corpus, and a query for a
        // word split across two elements return nothing.
        var document = SearchFixtures.Document(
            "doc-1", 1, SearchFixtures.Uk, narrative: "Contains invented-lactose.");

        var text = SearchableContent.Of(document.Bundle).Text;

        Assert.Contains("invented-lactose", text, StringComparison.Ordinal);
        Assert.DoesNotContain("<p>", text, StringComparison.Ordinal);
        Assert.DoesNotContain("xhtml", text, StringComparison.Ordinal);
    }

    [Fact]
    public void CAP_SCH_004_content_carrying_no_scope_cannot_be_made_searchable()
    {
        // An indexed document with no scope matches every caller's predicate, which is the
        // query-side failure ADR-022 decision 3 guards against, arriving from the write side.
        var unscoped = new Bundle
        {
            Type = Bundle.BundleType.Document,
            Entry = [new Bundle.EntryComponent { Resource = new Composition { Title = "SYNTHETIC" } }],
        };

        var error = Assert.Throws<ArgumentException>(() => SearchableContent.Of(unscoped));

        Assert.Contains("scope", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FN_SCH_001_the_scope_is_read_from_the_configured_identifier_authority()
    {
        // Tags are minted into the deployment's own namespaces (ADR-017); reading them from
        // anywhere else would find nothing and index the document as unscoped.
        var authority = IdentifierAuthority.Demonstration with
        {
            AffiliateTagSystem = "https://labels.example.net/tag/affiliate",
            MarketTagSystem = "https://labels.example.net/tag/market",
        };

        var bundle = ContentScope.Stamp(
            SearchFixtures.Document("doc-1", 1, SearchFixtures.Uk).Bundle,
            new DocumentScope("other-affiliate", "IE"),
            authority);

        Assert.Equal(new DocumentScope("other-affiliate", "IE"),
            SearchableContent.Of(bundle, authority).Scope);
    }

    [Fact]
    public void FN_SCH_001_a_document_with_no_product_or_language_is_still_searchable()
    {
        // Product binds properly to master data (capability 5), which does not exist yet.
        // Content that does not name one is indexed without it rather than refused.
        var document = SearchFixtures.Document(
            "doc-1", 1, SearchFixtures.Uk, language: null, product: null);

        var searchable = SearchableContent.Of(document.Bundle);

        Assert.Null(searchable.Language);
        Assert.Null(searchable.Product);
        Assert.Equal("SYNTHETIC TEST LABEL - Examplinum 10 mg tablets", searchable.Title);
    }
}
