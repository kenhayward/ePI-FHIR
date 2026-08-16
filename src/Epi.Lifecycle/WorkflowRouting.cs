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
/// What a routing model applies to (ADR-035 decision 2).
/// </summary>
/// <remarks>
/// Both null is the default model, which answers wherever nothing more specific does. A
/// catalogue must have exactly one.
/// </remarks>
public sealed record RoutingApplicability(string? LabelType = null, string? Market = null)
{
    /// <summary>
    /// How specific this is, used to choose between models that all match (ADR-035 decision 2).
    /// </summary>
    /// <remarks>
    /// Label type outranks market because a process is more often shaped by what the document
    /// is - a package leaflet is reviewed differently from a summary of product characteristics,
    /// everywhere - than by where it is going. An organisation for which that is wrong names
    /// both, which is exact and costs one more file.
    /// </remarks>
    public int Specificity => (LabelType is null ? 0 : 2) + (Market is null ? 0 : 1);

    public bool Matches(string? labelType, string? market) =>
        (LabelType is null || string.Equals(LabelType, labelType, StringComparison.Ordinal))
        && (Market is null || string.Equals(Market, market, StringComparison.Ordinal));
}

/// <summary>
/// Which states raise which tasks (CAP-WFL-001, capability 21).
/// </summary>
/// <remarks>
/// Configuration, not code. An organisation whose review process differs changes a file, and a
/// market with an extra step is a file rather than a branch (ADR-012).
/// </remarks>
public sealed class WorkflowModel(
    string name, IReadOnlyList<RoutingRule> rules, RoutingApplicability? appliesTo = null)
{
    public string Name { get; } = name;

    public IReadOnlyList<RoutingRule> Rules { get; } = rules;

    public RoutingApplicability AppliesTo { get; } = appliesTo ?? new RoutingApplicability();

    /// <summary>
    /// Every rule for a state, which may be none and may be several (ADR-035 decision 1).
    /// </summary>
    /// <remarks>
    /// Several rules for one state are several people asked at once, not a sequence. A sequence
    /// is states, because a step completing is a lifecycle transition (CAP-WFL-005) and the
    /// transition is the evidence that it completed.
    /// <para>
    /// Matched exactly, like the lifecycle model's transitions: a routing rule that matched
    /// loosely would raise a task for a state nobody configured.
    /// </para>
    /// </remarks>
    public IReadOnlyList<RoutingRule> ForState(string state) =>
    [
        .. Rules.Where(rule => string.Equals(rule.State, state, StringComparison.Ordinal)),
    ];
}

/// <summary>
/// The routing models a deployment has, and which one applies (FN-WFL-004, ADR-035 decision 2).
/// </summary>
public sealed class WorkflowCatalogue
{
    private readonly IReadOnlyList<WorkflowModel> _models;

    private WorkflowCatalogue(IReadOnlyList<WorkflowModel> models) => _models = models;

    public IReadOnlyList<WorkflowModel> Models => _models;

    /// <summary>
    /// The model that applies to this label type and market, which is never none.
    /// </summary>
    /// <remarks>
    /// The most specific match wins, and the default matches everything, so there is always an
    /// answer. That the catalogue has a default is checked when it loads rather than here: a
    /// missing one discovered at routing time would be a review nobody was asked for, which
    /// looks exactly like a review everybody passed.
    /// </remarks>
    public WorkflowModel For(string? labelType, string? market) =>
        _models
            .Where(model => model.AppliesTo.Matches(labelType, market))
            .OrderByDescending(model => model.AppliesTo.Specificity)
            .First();

    /// <summary>Loads every routing model in a directory, refusing anything ambiguous.</summary>
    public static WorkflowCatalogue LoadFrom(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);

        if (!Directory.Exists(directory))
        {
            throw new LifecycleConfigurationException(
                [$"{directory}: no routing models directory. A deployment that started without "
                 + "one would ask nobody to review anything."]);
        }

        var problems = new List<string>();
        var models = new List<WorkflowModel>();

        foreach (var path in Directory.EnumerateFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            try
            {
                models.Add(WorkflowConfiguration.LoadFrom(path));
            }
            catch (LifecycleConfigurationException invalid)
            {
                problems.AddRange(invalid.Problems);
            }
        }

        // Refused here rather than resolved when the catalogue is read: two models claiming the
        // same ground would otherwise make the process depend on the order files happen to be
        // read in (ADR-035 decision 3).
        foreach (var clash in models
            .GroupBy(model => model.AppliesTo)
            .Where(group => group.Count() > 1))
        {
            problems.Add(
                $"{directory}: {string.Join(" and ", clash.Select(m => $"'{m.Name}'"))} all apply "
                + $"to {Describe(clash.Key)}, so which process runs would depend on the order "
                + "the files were read in.");
        }

        if (problems.Count == 0 && !models.Any(model => model.AppliesTo.Specificity == 0))
        {
            problems.Add(
                $"{directory}: no model applies by default. A label type nobody wrote a model "
                + "for would be routed to nobody, and a review nobody was asked for looks "
                + "exactly like a review everybody passed.");
        }

        return problems.Count > 0
            ? throw new LifecycleConfigurationException(problems)
            : new WorkflowCatalogue(models);
    }

    private static string Describe(RoutingApplicability applicability) => applicability switch
    {
        { LabelType: not null, Market: not null } a => $"label type '{a.LabelType}' in {a.Market}",
        { LabelType: not null } a => $"label type '{a.LabelType}'",
        { Market: not null } a => $"market {a.Market}",
        _ => "everything, by default",
    };
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

            // Several rules for one state are several people asked at once (ADR-035 decision 1).
            // The same role asked twice is two tasks on one list for one job, and closing one
            // leaves the other - which is why this refuses the pair rather than the state.
            if (!seen.Add($"{rule.State} {rule.Assignee} {rule.Action}"))
            {
                problems.Add(
                    $"{file}: '{rule.Assignee}' is asked to '{rule.Action}' twice in state "
                    + $"'{rule.State}'. Two identical asks are two tasks for one job.");
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

        var appliesTo = new RoutingApplicability(
            Blank(parsed?.AppliesTo?.LabelType), Blank(parsed?.AppliesTo?.Market));

        return problems.Count > 0
            ? throw new LifecycleConfigurationException(problems)
            : new WorkflowModel(parsed!.Name!, rules, appliesTo);
    }

    private static string? Blank(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value;

    private sealed record ModelFile(
        [property: JsonPropertyName("_comment")] string? Comment,
        [property: JsonPropertyName("_appliesTo")] string? AppliesToNote,
        [property: JsonPropertyName("_parallelNotSequential")] string? ParallelNote,
        string? Name,
        AppliesToFile? AppliesTo,
        IReadOnlyList<RuleFile>? Rules);

    private sealed record AppliesToFile(string? LabelType, string? Market);

    private sealed record RuleFile(
        string? State, string? Action, string? Assignee, double? WithinHours = null);
}

/// <summary>
/// What a version is and where it is going, for selecting a routing model
/// (ADR-035 decisions 2 and 4).
/// </summary>
/// <remarks>
/// Read from the content by whoever calls the engine, never taken from a request. A caller that
/// could state its own label type could choose its own reviewers. Both are optional because a
/// document is not obliged to declare a type, and one that does not is still reviewed by
/// somebody - it falls through to the default model.
/// </remarks>
public sealed record RoutingSubject(string? LabelType = null, string? Market = null);
