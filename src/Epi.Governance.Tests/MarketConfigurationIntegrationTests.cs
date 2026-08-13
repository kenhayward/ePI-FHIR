using Epi.Governance.Configuration;
using Xunit;

namespace Epi.Governance.Tests;

// Integration tests: the loader against real files on disk, including the configuration
// actually shipped in this repository.
//   IT-004 A second market is served by adding configuration alone, with no code change
//   IT-009 An invalid market definition is rejected before activation
public sealed class MarketConfigurationIntegrationTests
{
    // The shipped configuration is the thing under test, so resolve the real repository
    // directory rather than a copy in the build output.
    private static string ShippedMarketsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EpiPlatform.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);
        return Path.Combine(directory!.FullName, "config", "markets");
    }

    [Fact]
    public void IT_004_the_shipped_market_configuration_loads_and_validates()
    {
        var catalogue = MarketCatalogue.LoadFrom(ShippedMarketsDirectory());

        Assert.NotEmpty(catalogue.Markets);
        Assert.All(catalogue.Markets, market =>
        {
            Assert.NotEmpty(market.Code);
            Assert.NotEmpty(market.Languages);
            Assert.NotEmpty(market.Affiliates);
            Assert.NotEmpty(market.Profile.Package);
            Assert.NotEmpty(market.Profile.Version);
        });
    }

    [Fact]
    public void IT_004_a_new_market_is_added_by_configuration_alone()
    {
        // Copy the shipped configuration, add one file, and reload. No code changes, no
        // recompilation, no registration step: the new market is simply there (CAP-CFG-004).
        var directory = Directory.CreateTempSubdirectory("epi-markets-it004-").FullName;
        try
        {
            foreach (var file in Directory.GetFiles(ShippedMarketsDirectory(), "*.json"))
            {
                File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
            }

            var before = MarketCatalogue.LoadFrom(directory);
            Assert.Null(before.Find("CA"));

            File.WriteAllText(Path.Combine(directory, "ca.json"), """
                {
                  "code": "CA",
                  "name": "Canada",
                  "regulator": "Health Canada",
                  "languages": ["en-CA", "fr-CA"],
                  "affiliates": ["ca-affiliate"],
                  "profile": {"package": "hl7.fhir.uv.emedicinal-product-info", "version": "1.0.0"}
                }
                """);

            var after = MarketCatalogue.LoadFrom(directory);

            var canada = after.Find("CA");
            Assert.NotNull(canada);
            Assert.Equal("Health Canada", canada!.Regulator);
            Assert.Equal(["en-CA", "fr-CA"], canada.Languages);
            Assert.Equal(before.Count + 1, after.Count);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void IT_009_an_invalid_market_definition_is_rejected_before_activation()
    {
        // Activation is all-or-nothing: one bad file means no catalogue, not a partial one.
        var directory = Directory.CreateTempSubdirectory("epi-markets-it009-").FullName;
        try
        {
            foreach (var file in Directory.GetFiles(ShippedMarketsDirectory(), "*.json"))
            {
                File.Copy(file, Path.Combine(directory, Path.GetFileName(file)));
            }

            File.WriteAllText(Path.Combine(directory, "broken.json"),
                """{"code": "XX", "name": "Broken", "regulator": "R", "languages": [], "affiliates": []}""");

            var error = Assert.Throws<MarketConfigurationException>(() => MarketCatalogue.LoadFrom(directory));

            Assert.Contains(error.Problems, p => p.Contains("broken.json"));
            Assert.Contains("broken.json", error.Message);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
