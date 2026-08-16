using System.Text.Json;
using System.Text.Json.Serialization;

namespace Epi.ContentCore;

/// <summary>
/// A product directory over configuration (ADR-036 decision 5).
/// </summary>
/// <remarks>
/// The reference implementation, in the same way the in-memory stores stand behind their durable
/// counterparts: the platform runs end to end with no external dependency, and a deployment with
/// a master-data system points at that instead. Nothing that uses <see cref="IProductDirectory"/>
/// changes when it does.
/// </remarks>
public sealed class ConfiguredProductDirectory : IProductDirectory
{
    private readonly IReadOnlyList<Product> _products;

    private ConfiguredProductDirectory(IReadOnlyList<Product> products) => _products = products;

    public IReadOnlyList<Product> Products => _products;

    public Task<Product?> FindAsync(
        string identifier, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        return Task.FromResult(_products.FirstOrDefault(
            product => string.Equals(product.Identifier, identifier, StringComparison.Ordinal)));
    }

    public Task<IReadOnlyList<Product>> SearchAsync(
        string text, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return Task.FromResult<IReadOnlyList<Product>>(
        [
            .. _products
                .Where(product => product.Name.Contains(text, StringComparison.OrdinalIgnoreCase))
                .OrderBy(product => product.Identifier, StringComparer.Ordinal),
        ]);
    }

    /// <summary>Reads the product set, refusing anything it cannot make sense of.</summary>
    public static ConfiguredProductDirectory LoadFrom(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new MasterDataConfigurationException(
                [$"{path}: no product configuration. A directory that answered nothing would be "
                 + "indistinguishable from one whose products had all been withdrawn."]);
        }

        return Parse(File.ReadAllText(path), Path.GetFileName(path));
    }

    /// <summary>Parses the product set from its JSON form.</summary>
    public static ConfiguredProductDirectory Parse(string json, string file = "products.json")
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        ProductsFile? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ProductsFile>(json, ReadOptions);
        }
        catch (JsonException error)
        {
            throw new MasterDataConfigurationException(
                [$"{file}: not a valid product set - {error.Message}"]);
        }

        var problems = new List<string>();
        var products = new List<Product>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var entry in parsed?.Products ?? [])
        {
            if (string.IsNullOrWhiteSpace(entry.Identifier) || string.IsNullOrWhiteSpace(entry.Name))
            {
                problems.Add($"{file}: a product needs an identifier and a name.");
                continue;
            }

            // A label resolving to two different products depending on read order is worse than
            // one resolving to none.
            if (!seen.Add(entry.Identifier))
            {
                problems.Add($"{file}: '{entry.Identifier}' appears twice.");
                continue;
            }

            products.Add(new Product(
                entry.Identifier, entry.Name, entry.MarketingAuthorisationHolder, entry.Markets));
        }

        return problems.Count > 0
            ? throw new MasterDataConfigurationException(problems)
            : new ConfiguredProductDirectory(products);
    }

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    private sealed record ProductsFile(
        [property: JsonPropertyName("_comment")] string? Comment,
        [property: JsonPropertyName("_notTheRealSource")] string? SourceNote,
        IReadOnlyList<ProductFile>? Products);

    private sealed record ProductFile(
        string? Identifier,
        string? Name,
        string? MarketingAuthorisationHolder = null,
        IReadOnlyList<string>? Markets = null);
}

/// <summary>Raised when the product configuration is missing or cannot be made sense of.</summary>
public sealed class MasterDataConfigurationException(IReadOnlyList<string> problems)
    : Exception($"Master data configuration is not valid and was not activated:{Environment.NewLine}  "
        + string.Join($"{Environment.NewLine}  ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
