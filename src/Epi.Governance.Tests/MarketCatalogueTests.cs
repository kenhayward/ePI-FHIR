using Epi.Governance.Configuration;
using Xunit;

namespace Epi.Governance.Tests;

// Unit tests for the market configuration loader.
//   FN-CFG-001 Load market definitions from configuration data
//   FN-CFG-003 Reject a market definition that fails schema validation
public sealed class MarketCatalogueTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("epi-markets-").FullName;

    private const string ValidGb = """
        {
          "code": "GB",
          "name": "United Kingdom",
          "regulator": "MHRA",
          "languages": ["en-GB"],
          "affiliates": ["uk-affiliate"],
          "profile": {"package": "hl7.fhir.uv.emedicinal-product-info", "version": "1.0.0"}
        }
        """;

    private const string Profile =
        """, "profile": {"package": "hl7.fhir.uv.emedicinal-product-info", "version": "1.0.0"}""";

    private void Write(string fileName, string content) =>
        File.WriteAllText(Path.Combine(_directory, fileName), content);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void FN_CFG_001_loads_every_market_definition_in_the_directory()
    {
        Write("gb.json", ValidGb);
        Write("ie.json", ValidGb.Replace("\"GB\"", "\"IE\"").Replace("United Kingdom", "Ireland"));

        var catalogue = MarketCatalogue.LoadFrom(_directory);

        Assert.Equal(2, catalogue.Count);
        Assert.Equal(["GB", "IE"], catalogue.Markets.Select(m => m.Code).Order());
    }

    [Fact]
    public void FN_CFG_001_exposes_a_market_by_its_code_regardless_of_casing()
    {
        Write("gb.json", ValidGb);

        var catalogue = MarketCatalogue.LoadFrom(_directory);

        var market = catalogue.Find("gb");
        Assert.NotNull(market);
        Assert.Equal("MHRA", market!.Regulator);
        Assert.Equal(["en-GB"], market.Languages);
        Assert.Equal(["uk-affiliate"], market.Affiliates);
    }

    [Fact]
    public void FN_CFG_001_an_empty_directory_yields_an_empty_catalogue()
    {
        var catalogue = MarketCatalogue.LoadFrom(_directory);

        Assert.Equal(0, catalogue.Count);
    }

    [Fact]
    public void FN_CFG_003_rejects_a_market_with_a_missing_required_field()
    {
        Write("bad.json", $$"""{"code": "GB", "name": "", "regulator": "MHRA", "languages": ["en-GB"], "affiliates": ["uk"]{{Profile}}}""");

        var error = Assert.Throws<MarketConfigurationException>(() => MarketCatalogue.LoadFrom(_directory));

        Assert.Contains(error.Problems, p => p.Contains("bad.json") && p.Contains("name"));
    }

    [Fact]
    public void FN_CFG_003_rejects_a_market_with_no_languages()
    {
        Write("bad.json", $$"""{"code": "GB", "name": "United Kingdom", "regulator": "MHRA", "languages": [], "affiliates": ["uk"]{{Profile}}}""");

        var error = Assert.Throws<MarketConfigurationException>(() => MarketCatalogue.LoadFrom(_directory));

        Assert.Contains(error.Problems, p => p.Contains("languages"));
    }

    [Fact]
    public void FN_CFG_003_rejects_two_markets_claiming_the_same_code()
    {
        Write("gb.json", ValidGb);
        Write("gb-again.json", ValidGb);

        var error = Assert.Throws<MarketConfigurationException>(() => MarketCatalogue.LoadFrom(_directory));

        Assert.Contains(error.Problems, p => p.Contains("GB", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FN_CFG_003_rejects_an_unknown_property_rather_than_ignoring_it()
    {
        // A typo in a key must not be silently dropped: config-as-data is only trustworthy
        // if what the file says and what the platform reads are the same thing.
        Write("typo.json", """
            {"code": "GB", "name": "United Kingdom", "regulator": "MHRA",
             "languges": ["en-GB"], "languages": ["en-GB"], "affiliates": ["uk"],
             "profile": {"package": "p", "version": "1.0.0"}}
            """);

        var error = Assert.Throws<MarketConfigurationException>(() => MarketCatalogue.LoadFrom(_directory));

        Assert.Contains(error.Problems, p => p.Contains("languges"));
    }

    [Fact]
    public void FN_CFG_003_rejects_malformed_json_naming_the_file()
    {
        Write("broken.json", "{ this is not json");

        var error = Assert.Throws<MarketConfigurationException>(() => MarketCatalogue.LoadFrom(_directory));

        Assert.Contains(error.Problems, p => p.Contains("broken.json"));
    }

    [Fact]
    public void FN_CFG_003_reports_every_problem_not_only_the_first()
    {
        Write("one.json", $$"""{"code": "", "name": "One", "regulator": "R", "languages": ["en"], "affiliates": ["a"]{{Profile}}}""");
        Write("two.json", $$"""{"code": "IE", "name": "Two", "regulator": "", "languages": ["en"], "affiliates": ["a"]{{Profile}}}""");

        var error = Assert.Throws<MarketConfigurationException>(() => MarketCatalogue.LoadFrom(_directory));

        Assert.Contains(error.Problems, p => p.Contains("one.json"));
        Assert.Contains(error.Problems, p => p.Contains("two.json"));
    }

    [Fact]
    public void FN_CFG_002_resolves_the_active_profile_for_a_market()
    {
        Write("gb.json", ValidGb);

        var catalogue = MarketCatalogue.LoadFrom(_directory);

        var profile = catalogue.ActiveProfileFor("GB");
        Assert.NotNull(profile);
        Assert.Equal("hl7.fhir.uv.emedicinal-product-info", profile!.Package);
        Assert.Equal("1.0.0", profile.Version);
    }

    [Fact]
    public void FN_CFG_002_an_unknown_market_resolves_to_no_profile()
    {
        Write("gb.json", ValidGb);

        Assert.Null(MarketCatalogue.LoadFrom(_directory).ActiveProfileFor("ZZ"));
    }

    [Fact]
    public void FN_CFG_003_rejects_a_market_that_names_no_conformance_profile()
    {
        // A market without a profile has no yardstick to validate against (ADR-016), so it
        // must not load rather than silently defaulting to one.
        Write("bad.json", """{"code": "GB", "name": "UK", "regulator": "MHRA", "languages": ["en"], "affiliates": ["uk"]}""");

        var error = Assert.Throws<MarketConfigurationException>(() => MarketCatalogue.LoadFrom(_directory));

        Assert.Contains(error.Problems, p => p.Contains("profile"));
    }

    [Fact]
    public void FN_CFG_003_rejects_a_missing_directory_rather_than_starting_empty()
    {
        var missing = Path.Combine(_directory, "does-not-exist");

        var error = Assert.Throws<MarketConfigurationException>(() => MarketCatalogue.LoadFrom(missing));

        Assert.Contains(error.Problems, p => p.Contains("does-not-exist"));
    }
}
