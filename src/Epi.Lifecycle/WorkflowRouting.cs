using System.Text.Json;
using System.Text.Json.Serialization;

namespace Epi.Lifecycle;

/// <summary>
/// One routing rule: what a state asks for, of whom, and by when (ADR-031 decision 3).
/// </summary>
/// <param name="State">The state a version reaches that raises this task.</param>
/// <param name="Action">The transition the task is asking somebody to make.</param>
/// <param name="Assignee">The role the ask goes to.</param>
/// <param name="Within">How long it may stay open before it is overdue, if there is a limit.</param>
public sealed record RoutingRule(
    string State, string Action, string Assignee, TimeSpan? Within = null);

/// <summary>
/// Which states raise which tasks, per label type and market (CAP-WFL-001, capability 21).
/// </summary>
/// <remarks>
/// Configuration, not code. An organisation whose review process differs changes a file, and a
/// market with an extra step is a row rather than a branch (ADR-012).
/// </remarks>
public sealed class WorkflowModel(string name, IReadOnlyList<RoutingRule> rules)
{
    public string Name { get; } = name;

    public IReadOnlyList<RoutingRule> Rules { get; } = rules;

    /// <summary>The rule for a state, or null where that state asks nothing of anyone.</summary>
    /// <remarks>
    /// Matched exactly, like the lifecycle model's transitions: a routing rule that matched
    /// loosely would raise a task for a state nobody configured.
    /// </remarks>
    public RoutingRule? For(string state) => Rules.FirstOrDefault(
        rule => string.Equals(rule.State, state, StringComparison.Ordinal));
}

/// <summary>Loads a workflow model from configuration data (FN-WFL-001).</summary>
public static class WorkflowConfiguration
{
    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
    };

    public static WorkflowModel LoadFrom(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
        {
            throw new LifecycleConfigurationException([$"{path}: workflow model not found."]);
        }

        var file = Path.GetFileName(path);
        ModelFile? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ModelFile>(File.ReadAllText(path), ReadOptions);
        }
        catch (JsonException error)
        {
            throw new LifecycleConfigurationException(
                [$"{file}: not a valid workflow model - {error.Message}"]);
        }

        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(parsed?.Name))
        {
            problems.Add($"{file}: 'name' is required.");
        }

        var rules = new List<RoutingRule>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var rule in parsed?.Rules ?? [])
        {
            if (string.IsNullOrWhiteSpace(rule.State) || string.IsNullOrWhiteSpace(rule.Action)
                || string.IsNullOrWhiteSpace(rule.Assignee))
            {
                problems.Add($"{file}: a routing rule needs a state, an action and an assignee.");
                continue;
            }

            // Two rules for one state means the ask depends on which is read first, which is
            // not something a process may leave to chance.
            if (!seen.Add(rule.State))
            {
                problems.Add($"{file}: two rules route state '{rule.State}'.");
                continue;
            }

            if (rule.WithinHours is { } hours and <= 0)
            {
                problems.Add($"{file}: '{rule.State}' allows {hours} hours, which is no time at all.");
                continue;
            }

            rules.Add(new RoutingRule(
                rule.State, rule.Action, rule.Assignee,
                rule.WithinHours is { } within ? TimeSpan.FromHours(within) : null));
        }

        return problems.Count > 0
            ? throw new LifecycleConfigurationException(problems)
            : new WorkflowModel(parsed!.Name!, rules);
    }

    private sealed record ModelFile(
        [property: JsonPropertyName("_comment")] string? Comment,
        string? Name,
        IReadOnlyList<RuleFile>? Rules);

    private sealed record RuleFile(
        string? State, string? Action, string? Assignee, double? WithinHours = null);
}
