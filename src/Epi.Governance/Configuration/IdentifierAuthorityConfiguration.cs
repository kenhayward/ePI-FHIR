using System.Text.Json;
using System.Text.Json.Serialization;
using Epi.ContentCore;

namespace Epi.Governance.Configuration;

/// <summary>
/// Loads the identifier authority from configuration data (ADR-017), so adopting the platform
/// is one edit to one file rather than a code change and a release.
/// </summary>
public static class IdentifierAuthorityConfiguration
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>Reads the authority from a JSON file.</summary>
    /// <exception cref="MarketConfigurationException">
    /// If the file is missing, unreadable, or does not define all four systems as absolute
    /// URIs. A half-configured authority would mint some identifiers into the adopter's
    /// namespace and others into the demonstration's, which is worse than either alone.
    /// </exception>
    public static IdentifierAuthority LoadFrom(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new MarketConfigurationException([$"{path}: identifier configuration not found."]);
        }

        var file = Path.GetFileName(path);
        AuthorityFile? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<AuthorityFile>(File.ReadAllText(path), ReadOptions);
        }
        catch (JsonException error)
        {
            throw new MarketConfigurationException([$"{file}: not valid identifier configuration - {error.Message}"]);
        }

        var problems = new List<string>();
        var document = Absolute(problems, file, "documentSystem", parsed?.DocumentSystem);
        var version = Absolute(problems, file, "versionTagSystem", parsed?.VersionTagSystem);
        var affiliate = Absolute(problems, file, "affiliateTagSystem", parsed?.AffiliateTagSystem);
        var market = Absolute(problems, file, "marketTagSystem", parsed?.MarketTagSystem);
        var template = Absolute(problems, file, "templateSystem", parsed?.TemplateSystem);
        var templateVersion = Absolute(
            problems, file, "templateVersionTagSystem", parsed?.TemplateVersionTagSystem);

        return problems.Count > 0
            ? throw new MarketConfigurationException(problems)
            : new IdentifierAuthority(
                document, version, affiliate, market, template, templateVersion);
    }

    private static string Absolute(List<string> problems, string file, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add($"{file}: '{field}' is required (ADR-017).");
            return string.Empty;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out _))
        {
            problems.Add($"{file}: '{field}' must be an absolute URI naming the authority, not '{value}'.");
            return string.Empty;
        }

        return value.Trim();
    }

    /// <summary>The on-disk shape. "_comment" carries the guidance an adopter needs.</summary>
    private sealed record AuthorityFile(
        [property: JsonPropertyName("_comment")] string? Comment,
        string? DocumentSystem,
        string? VersionTagSystem,
        string? AffiliateTagSystem,
        string? MarketTagSystem,
        string? TemplateSystem,
        string? TemplateVersionTagSystem);
}
