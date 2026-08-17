using System.Text.Json;
using System.Text.Json.Serialization;

namespace Epi.Templates;

/// <summary>
/// The standard templates a deployment starts from (ADR-042 decision 7, FN-TPL-004).
/// </summary>
/// <remarks>
/// <para>
/// An adopting organisation gets QRD-shaped templates to work from rather than a blank page, and
/// every one arrives as a draft. Seeding an approved template would be asserting a signature
/// nobody gave, and a draft is one nobody may render officially with - so what a seed supplies is
/// a starting point rather than a decision.
/// </para>
/// <para>
/// Most of what this does is refuse. It never approves, it never overwrites, and it never touches
/// a template already in the store: that one belongs to whoever put it there, and a seed reaching
/// in to correct it would be changing what a patient reads without anybody deciding to.
/// </para>
/// </remarks>
public static class TemplateSeeding
{
    /// <summary>
    /// Who a seeded template is recorded as authored by.
    /// </summary>
    /// <remarks>
    /// The platform, because that is what is true: nobody wrote these. Anybody may therefore
    /// approve one, which is right - segregation of duties disqualifies whoever wrote something
    /// from approving it, and no person wrote this. Recording an operator instead would put a
    /// name against work nobody did and disqualify them from approving a template they had
    /// never seen.
    /// </remarks>
    public const string SeedAuthor = "platform:template-seed";

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    /// <summary>
    /// Creates any standard template the store does not already hold, and returns what it created.
    /// </summary>
    /// <exception cref="InvalidTemplateException">
    /// If the directory is missing, or any seed cannot be read. All or nothing, because a
    /// deployment that started with two of three standard templates would look complete and be
    /// missing one.
    /// </exception>
    public static async Task<IReadOnlyList<string>> ApplyAsync(
        ITemplateStore store, string directory, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            // Distinct from an empty directory, which is a deployment that has chosen to author
            // its own. A path that is not there is one configured wrongly, and this platform has
            // been bitten by that class three times.
            throw new InvalidTemplateException(
                [$"{directory}: no template seed directory. A deployment that meant to seed no "
                 + "templates has an empty directory rather than a missing one."]);
        }

        // Read every seed before writing any, so a directory with one bad file seeds nothing
        // rather than some.
        var definitions = new List<RenderTemplateDefinition>();
        var problems = new List<string>();

        foreach (var path in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            var file = Path.GetFileName(path);

            try
            {
                var parsed = JsonSerializer.Deserialize<SeedFile>(
                    await File.ReadAllTextAsync(path, cancellationToken), ReadOptions);

                if (string.IsNullOrWhiteSpace(parsed?.Identifier)
                    || string.IsNullOrWhiteSpace(parsed.Name)
                    || string.IsNullOrWhiteSpace(parsed.Stylesheet))
                {
                    problems.Add(
                        $"{file}: a seeded template needs an identifier, a name and a stylesheet.");
                    continue;
                }

                definitions.Add(
                    new RenderTemplateDefinition(parsed.Identifier, parsed.Name, parsed.Stylesheet));
            }
            catch (JsonException invalid)
            {
                problems.Add($"{file}: not a valid template seed - {invalid.Message}");
            }
        }

        if (problems.Count > 0)
        {
            throw new InvalidTemplateException(problems);
        }

        var created = new List<string>();

        foreach (var definition in definitions)
        {
            // Asked rather than assumed. A template already there belongs to whoever put it
            // there, whatever state it is in and whoever has signed for it.
            if ((await store.VersionsAsync(definition.Identifier, cancellationToken)).Count > 0)
            {
                continue;
            }

            await store.CreateAsync(definition, cancellationToken);
            created.Add(definition.Identifier);
        }

        return created;
    }

    /// <summary>The on-disk shape. The underscore fields carry the reasoning a reader needs.</summary>
    private sealed record SeedFile(
        [property: JsonPropertyName("_comment")] string? Comment,
        [property: JsonPropertyName("_shape")] string? Shape,
        string? Identifier,
        string? Name,
        string? Stylesheet);
}
