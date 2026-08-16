using System.Text.Json;

namespace Epi.Rendering;

/// <summary>
/// How long a stored artefact is retained under object-lock, per lineage (ADR-034 decision 2).
/// </summary>
/// <remarks>
/// Configuration rather than a constant, because how long a render must be kept is a regulatory
/// question with different answers per market and per artefact class (ADR-012). Nothing here
/// knows why a period was chosen, and nothing here deletes anything when one expires: retention
/// is how long the object store refuses to destroy what was written, not a schedule.
/// </remarks>
public sealed class AssetRetention
{
    private readonly Dictionary<string, TimeSpan> _byLineage;

    private AssetRetention(Dictionary<string, TimeSpan> byLineage) => _byLineage = byLineage;

    /// <summary>How long an artefact of this lineage is retained.</summary>
    /// <exception cref="AssetRetentionException">
    /// If the lineage has no configured period. Defaulting would be worse than failing: an
    /// artefact written without retention is indistinguishable from one written with it until
    /// somebody tries to destroy it, and by then the answer matters.
    /// </exception>
    public TimeSpan For(string lineage)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineage);

        if (!_byLineage.TryGetValue(lineage, out var period))
        {
            throw new AssetRetentionException(
                $"No retention period is configured for the '{lineage}' lineage. Add one to "
                + "config/assets/retention.json; an artefact stored without retention is not "
                + "protected by object-lock and looks exactly like one that is.");
        }

        return period;
    }

    /// <summary>Reads the retention configuration, refusing anything it cannot make sense of.</summary>
    public static AssetRetention Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new AssetRetentionException(
                $"The asset retention configuration was not found at '{path}'. A service that "
                + "started without it would store artefacts nothing protects.");
        }

        return Parse(File.ReadAllText(path));
    }

    /// <summary>Parses the retention configuration from its JSON form.</summary>
    public static AssetRetention Parse(string json)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(json);

        using var document = JsonDocument.Parse(json);

        if (!document.RootElement.TryGetProperty("lineages", out var lineages)
            || lineages.ValueKind != JsonValueKind.Array)
        {
            throw new AssetRetentionException(
                "The asset retention configuration has no 'lineages' array.");
        }

        var byLineage = new Dictionary<string, TimeSpan>(StringComparer.Ordinal);

        foreach (var entry in lineages.EnumerateArray())
        {
            var lineage = entry.TryGetProperty("lineage", out var name)
                ? name.GetString()
                : null;

            if (string.IsNullOrWhiteSpace(lineage))
            {
                throw new AssetRetentionException(
                    "A retention entry has no 'lineage'.");
            }

            if (!entry.TryGetProperty("retentionDays", out var days)
                || !days.TryGetInt32(out var value)
                || value <= 0)
            {
                throw new AssetRetentionException(
                    $"The '{lineage}' retention entry has no positive 'retentionDays'. A period "
                    + "of zero enables object-lock and then does not use it.");
            }

            if (!byLineage.TryAdd(lineage, TimeSpan.FromDays(value)))
            {
                throw new AssetRetentionException(
                    $"The '{lineage}' lineage is configured twice, and which entry wins would "
                    + "depend on the order of the file.");
            }
        }

        return new AssetRetention(byLineage);
    }
}

/// <summary>Raised when the retention configuration is missing or cannot be made sense of.</summary>
public sealed class AssetRetentionException(string message) : Exception(message);
