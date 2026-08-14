using Epi.ContentCore;
using Epi.Governance.Configuration;
using Xunit;

namespace Epi.Governance.Tests;

// The identifier authority as configuration (ADR-017). What this protects against is an
// adopting organisation half-setting it, or believing they have set it when they have not.
public sealed class IdentifierAuthorityConfigurationTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("epi-identifiers-").FullName;

    private string Write(string content)
    {
        var path = Path.Combine(_directory, "identifiers.json");
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void Loads_every_system_from_configuration()
    {
        var path = Write("""
            {
              "documentSystem": "https://id.example-pharma.test/epi/document",
              "versionTagSystem": "https://id.example-pharma.test/epi/version",
              "affiliateTagSystem": "https://id.example-pharma.test/epi/affiliate",
              "marketTagSystem": "https://id.example-pharma.test/epi/market",
              "templateSystem": "https://id.example-pharma.test/epi/template",
              "templateVersionTagSystem": "https://id.example-pharma.test/epi/template-version",
              "unitSystem": "https://id.example-pharma.test/epi/reusable-unit",
              "unitReferenceExtension": "https://id.example-pharma.test/epi/unit-reference"
            }
            """);

        var authority = IdentifierAuthorityConfiguration.LoadFrom(path);

        Assert.Equal("https://id.example-pharma.test/epi/document", authority.DocumentSystem);
        Assert.Equal("https://id.example-pharma.test/epi/market", authority.MarketTagSystem);
        Assert.False(authority.IsDemonstration);
    }

    [Fact]
    public void A_configured_authority_is_what_content_is_minted_and_stamped_into()
    {
        // The point of the exercise: configuration reaches the identifiers, so an adopter
        // changes one file rather than the codebase.
        var authority = IdentifierAuthorityConfiguration.LoadFrom(Write("""
            {
              "documentSystem": "urn:oid:2.16.840.1.113883.3.9999",
              "versionTagSystem": "https://id.example-pharma.test/epi/version",
              "affiliateTagSystem": "https://id.example-pharma.test/epi/affiliate",
              "marketTagSystem": "https://id.example-pharma.test/epi/market",
              "templateSystem": "https://id.example-pharma.test/epi/template",
              "templateVersionTagSystem": "https://id.example-pharma.test/epi/template-version",
              "unitSystem": "https://id.example-pharma.test/epi/reusable-unit",
              "unitReferenceExtension": "https://id.example-pharma.test/epi/unit-reference"
            }
            """));

        var identity = ContentIdentity.Mint(authority);

        Assert.Equal("urn:oid:2.16.840.1.113883.3.9999", identity.System);
    }

    [Fact]
    public void A_partly_configured_authority_is_refused()
    {
        // Worse than either alone: some identifiers would be minted into the adopter's
        // namespace and others into the demonstration's.
        var path = Write("""
            {
              "documentSystem": "https://id.example-pharma.test/epi/document",
              "versionTagSystem": "",
              "affiliateTagSystem": "https://id.example-pharma.test/epi/affiliate",
              "marketTagSystem": "https://id.example-pharma.test/epi/market",
              "templateSystem": "https://id.example-pharma.test/epi/template",
              "templateVersionTagSystem": "https://id.example-pharma.test/epi/template-version",
              "unitSystem": "https://id.example-pharma.test/epi/reusable-unit",
              "unitReferenceExtension": "https://id.example-pharma.test/epi/unit-reference"
            }
            """);

        var error = Assert.Throws<MarketConfigurationException>(
            () => IdentifierAuthorityConfiguration.LoadFrom(path));

        Assert.Contains(error.Problems, p => p.Contains("versionTagSystem"));
    }

    [Fact]
    public void A_system_that_is_not_an_absolute_uri_is_refused()
    {
        // A relative or bare string names no authority at all, which defeats the purpose of
        // the system element.
        var path = Write("""
            {
              "documentSystem": "our-company",
              "versionTagSystem": "https://id.example-pharma.test/epi/version",
              "affiliateTagSystem": "https://id.example-pharma.test/epi/affiliate",
              "marketTagSystem": "https://id.example-pharma.test/epi/market",
              "templateSystem": "https://id.example-pharma.test/epi/template",
              "templateVersionTagSystem": "https://id.example-pharma.test/epi/template-version",
              "unitSystem": "https://id.example-pharma.test/epi/reusable-unit",
              "unitReferenceExtension": "https://id.example-pharma.test/epi/unit-reference"
            }
            """);

        var error = Assert.Throws<MarketConfigurationException>(
            () => IdentifierAuthorityConfiguration.LoadFrom(path));

        Assert.Contains(error.Problems, p => p.Contains("absolute URI"));
    }

    [Fact]
    public void A_missing_file_is_refused_rather_than_silently_falling_back()
    {
        // Falling back to the demonstration authority would be the worst outcome: content
        // minted into a namespace nobody owns, with nothing to indicate it happened.
        var error = Assert.Throws<MarketConfigurationException>(
            () => IdentifierAuthorityConfiguration.LoadFrom(Path.Combine(_directory, "absent.json")));

        Assert.Contains(error.Problems, p => p.Contains("absent.json"));
    }

    [Fact]
    public void The_shipped_configuration_loads_and_is_recognisably_the_demonstration_one()
    {
        // The repository ships a placeholder on purpose (ADR-017). If this ever stops being
        // the demonstration authority, someone has set a real one here rather than in their
        // own deployment, and every adopter would inherit it.
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "EpiPlatform.sln")))
        {
            repository = repository.Parent;
        }

        var authority = IdentifierAuthorityConfiguration.LoadFrom(
            Path.Combine(repository!.FullName, "config", "identifiers.json"));

        Assert.True(authority.IsDemonstration,
            "config/identifiers.json should ship the demonstration authority, not a real one.");
    }
}
