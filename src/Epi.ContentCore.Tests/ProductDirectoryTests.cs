using Xunit;

namespace Epi.ContentCore.Tests;

// The master-data binding point (FN-MDM-001).
//   CAP-MDM-008 Expose an identifier resolution and association API
//   CAP-MDM-004 Resolve and validate identifiers used in content
//
// Capabilities 5 and 6 have been deferred through three iterations, which makes them the oldest
// debt in the plan. What is built here is the seam and its reference implementation, not a
// master-data system: the point is that the source is replaceable and that nothing which uses a
// product has to know where products come from (ADR-036 decision 1).
public sealed class ProductDirectoryTests
{
    private const string Configured = """
        {"products": [
          {"identifier": "PROD-0001", "name": "SYNTHETIC - Examplinum 10 mg tablets",
           "marketingAuthorisationHolder": "SYNTHETIC - Example Pharmaceuticals Ltd",
           "markets": ["GB", "DE"]},
          {"identifier": "PROD-0002", "name": "SYNTHETIC - Examplinum 20 mg tablets"}
        ]}
        """;

    [Fact]
    public async Task FN_MDM_001_a_product_reference_resolves_to_what_master_data_holds()
    {
        var directory = ConfiguredProductDirectory.Parse(Configured);

        var product = await directory.FindAsync("PROD-0001");

        Assert.NotNull(product);
        Assert.Equal("SYNTHETIC - Examplinum 10 mg tablets", product!.Name);
        Assert.Equal(["GB", "DE"], product.Markets);
    }

    [Fact]
    public async Task FN_MDM_001_a_reference_to_no_product_resolves_to_nothing()
    {
        // Null rather than an empty product. Something shaped like a product, for a reference
        // that names none, is worse than nothing: it would be indexed, displayed and printed.
        var directory = ConfiguredProductDirectory.Parse(Configured);

        Assert.Null(await directory.FindAsync("PROD-9999"));
    }

    [Fact]
    public async Task FN_MDM_001_products_can_be_searched_for_rather_than_typed()
    {
        // The operation the authoring surface needs. A product typed as free text is the string
        // the platform has today, which is exactly what a master-data binding is for.
        var directory = ConfiguredProductDirectory.Parse(Configured);

        var found = await directory.SearchAsync("examplinum");

        Assert.Equal(["PROD-0001", "PROD-0002"], found.Select(p => p.Identifier));
    }

    [Fact]
    public async Task FN_MDM_001_a_product_with_no_markets_recorded_has_none_rather_than_null()
    {
        var directory = ConfiguredProductDirectory.Parse(Configured);

        Assert.Empty((await directory.FindAsync("PROD-0002"))!.Markets);
    }

    [Fact]
    public void FN_MDM_001_a_product_appearing_twice_is_refused()
    {
        // A label resolving to two different products depending on read order is worse than one
        // resolving to none.
        Assert.Throws<MasterDataConfigurationException>(() => ConfiguredProductDirectory.Parse("""
            {"products": [
              {"identifier": "PROD-0001", "name": "One"},
              {"identifier": "PROD-0001", "name": "Another"}
            ]}
            """));
    }

    [Fact]
    public void FN_MDM_001_a_product_with_no_name_is_refused()
    {
        Assert.Throws<MasterDataConfigurationException>(() => ConfiguredProductDirectory.Parse(
            """{"products": [{"identifier": "PROD-0001"}]}"""));
    }

    [Fact]
    public void FN_MDM_001_a_missing_configuration_file_is_refused_rather_than_assumed()
    {
        // A directory that answered nothing would be indistinguishable from one whose products
        // had all been withdrawn.
        Assert.Throws<MasterDataConfigurationException>(() => ConfiguredProductDirectory.LoadFrom(
            Path.Combine(Path.GetTempPath(), $"no-such-products-{Guid.NewGuid():n}.json")));
    }

    [Fact]
    public void FN_MDM_001_the_shipped_product_set_loads_and_is_synthetic()
    {
        // Test data in this domain must be synthetic, and a product set is exactly the sort of
        // file a real name would find its way into.
        var directory = ConfiguredProductDirectory.LoadFrom(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "config", "master-data", "products.json")));

        Assert.NotEmpty(directory.Products);
        Assert.All(directory.Products, product => Assert.StartsWith(
            "SYNTHETIC", product.Name, StringComparison.Ordinal));
    }
}
