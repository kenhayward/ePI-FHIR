using System.Text.Json;
using System.Text.Json.Serialization;

namespace Epi.Governance.Configuration;

/// <summary>
/// The set of markets the platform is configured to operate in (capability 21).
/// Loaded from configuration data so that onboarding a market is a configuration change
/// rather than a code release (CAP-CFG-004, ADR-012).
/// </summary>
public sealed class MarketCatalogue
{
    private readonly Dictionary<string, MarketDefinition> _byCode;

    private MarketCatalogue(Dictionary<string, MarketDefinition> byCode) => _byCode = byCode;

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        // A mistyped key must fail rather than be silently dropped: configuration is only
        // trustworthy if what the file says and what the platform reads are the same thing.
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowTrailingCommas = false,
    };

    /// <summary>Loads and validates every market definition in a directory.</summary>
    /// <exception cref="MarketConfigurationException">
    /// If the directory is missing, or any definition is invalid. Loading is all or nothing:
    /// an invalid definition means no catalogue rather than a partial one (CAP-CFG-006).
    /// </exception>
    public static MarketCatalogue LoadFrom(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            throw new MarketConfigurationException(
                [$"{directory}: market configuration directory not found."]);
        }

        var problems = new List<string>();
        var markets = new Dictionary<string, MarketDefinition>(StringComparer.OrdinalIgnoreCase);
        var definedIn = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        // Ordered so that the same configuration always produces the same problem list, which
        // matters when a build log is the evidence (D3 Section 10.3).
        foreach (var path in Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            var file = Path.GetFileName(path);

            MarketFile? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<MarketFile>(File.ReadAllText(path), ReadOptions);
            }
            catch (JsonException error)
            {
                problems.Add($"{file}: not valid market configuration - {error.Message}");
                continue;
            }

            if (parsed is null)
            {
                problems.Add($"{file}: empty market definition.");
                continue;
            }

            var before = problems.Count;
            var code = Required(problems, file, "code", parsed.Code);
            var name = Required(problems, file, "name", parsed.Name);
            var regulator = Required(problems, file, "regulator", parsed.Regulator);
            var languages = RequiredList(problems, file, "languages", parsed.Languages);
            var affiliates = RequiredList(problems, file, "affiliates", parsed.Affiliates);

            if (problems.Count != before)
            {
                continue;
            }

            if (definedIn.TryGetValue(code, out var other))
            {
                problems.Add($"{file}: market code '{code}' is already defined in '{other}'.");
                continue;
            }

            definedIn[code] = file;
            markets[code] = new MarketDefinition(code, name, regulator, languages, affiliates);
        }

        if (problems.Count > 0)
        {
            throw new MarketConfigurationException(problems);
        }

        return new MarketCatalogue(markets);
    }

    public IReadOnlyCollection<MarketDefinition> Markets => _byCode.Values;

    public int Count => _byCode.Count;

    /// <summary>The market with this code, or null. Codes are matched case-insensitively.</summary>
    public MarketDefinition? Find(string code) =>
        string.IsNullOrWhiteSpace(code) ? null : _byCode.GetValueOrDefault(code.Trim());

    private static string Required(List<string> problems, string file, string field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            problems.Add($"{file}: '{field}' is required and must not be empty.");
            return string.Empty;
        }

        return value.Trim();
    }

    private static IReadOnlyList<string> RequiredList(
        List<string> problems, string file, string field, IReadOnlyList<string>? values)
    {
        if (values is null || values.Count == 0)
        {
            problems.Add($"{file}: '{field}' is required and must list at least one entry.");
            return [];
        }

        if (values.Any(string.IsNullOrWhiteSpace))
        {
            problems.Add($"{file}: '{field}' must not contain empty entries.");
            return [];
        }

        return [.. values.Select(v => v.Trim())];
    }

    /// <summary>The on-disk shape. Nullable throughout so that a missing field is reported as a
    /// configuration problem rather than surfacing as a deserialisation failure.</summary>
    private sealed record MarketFile(
        string? Code,
        string? Name,
        string? Regulator,
        IReadOnlyList<string>? Languages,
        IReadOnlyList<string>? Affiliates);
}
