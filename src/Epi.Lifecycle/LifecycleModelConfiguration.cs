using System.Text.Json;
using System.Text.Json.Serialization;

namespace Epi.Lifecycle;

/// <summary>Loads a lifecycle state model from configuration data (FN-LCM-001).</summary>
/// <remarks>
/// Validated before activation, like every other configuration the platform loads
/// (CAP-CFG-006). A state model is a control: one that half-loads is worse than one that does
/// not load, because the transitions it does define will appear to work.
/// </remarks>
public static class LifecycleModelConfiguration
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static LifecycleModel LoadFrom(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new LifecycleConfigurationException([$"{path}: lifecycle model not found."]);
        }

        var file = Path.GetFileName(path);
        ModelFile? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ModelFile>(File.ReadAllText(path), ReadOptions);
        }
        catch (JsonException error)
        {
            throw new LifecycleConfigurationException([$"{file}: not a valid lifecycle model - {error.Message}"]);
        }

        var problems = new List<string>();
        var states = parsed?.States ?? [];

        if (string.IsNullOrWhiteSpace(parsed?.Name))
        {
            problems.Add($"{file}: 'name' is required.");
        }

        if (states.Count == 0)
        {
            problems.Add($"{file}: 'states' must list at least one state.");
        }

        if (string.IsNullOrWhiteSpace(parsed?.Initial) || !states.Contains(parsed.Initial))
        {
            problems.Add($"{file}: initial state '{parsed?.Initial}' is not one of the declared states.");
        }

        var transitions = new List<LifecycleTransition>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var transition in parsed?.Transitions ?? [])
        {
            foreach (var (label, value) in new[] { ("from", transition.From), ("to", transition.To) })
            {
                if (string.IsNullOrWhiteSpace(value) || !states.Contains(value))
                {
                    problems.Add($"{file}: transition '{label}' refers to '{value}', which is not a declared state.");
                }
            }

            if (string.IsNullOrWhiteSpace(transition.Action))
            {
                problems.Add($"{file}: a transition has no action.");
                continue;
            }

            // Two rules for one move means the outcome depends on which is read first, which
            // is not something a control may leave to chance.
            if (!seen.Add($"{transition.From}|{transition.Action}"))
            {
                problems.Add(
                    $"{file}: two transitions define action '{transition.Action}' from state "
                    + $"'{transition.From}'.");
                continue;
            }

            transitions.Add(new LifecycleTransition(
                transition.From!, transition.To!, transition.Action,
                transition.RequiresSignature, transition.SegregatedFromAuthor));
        }

        return problems.Count > 0
            ? throw new LifecycleConfigurationException(problems)
            : new LifecycleModel(parsed!.Name!, parsed.Initial!, states, transitions);
    }

    private sealed record ModelFile(
        [property: JsonPropertyName("_comment")] string? Comment,
        string? Name,
        string? Initial,
        IReadOnlyList<string>? States,
        IReadOnlyList<TransitionFile>? Transitions);

    private sealed record TransitionFile(
        string? From, string? To, string? Action,
        bool RequiresSignature = false, bool SegregatedFromAuthor = false,
        string? SignatureMeaning = null);
}

/// <summary>Raised when a state model cannot be activated.</summary>
public sealed class LifecycleConfigurationException(IReadOnlyList<string> problems)
    : Exception($"The lifecycle model is not valid and was not activated:{Environment.NewLine}  "
        + string.Join($"{Environment.NewLine}  ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
