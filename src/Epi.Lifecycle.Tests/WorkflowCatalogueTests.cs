using Xunit;

namespace Epi.Lifecycle.Tests;

// Selecting a review process by what the document is and where it is going (FN-WFL-004).
//   CAP-WFL-001 Configurable multi-step review/approval workflows per market and label type
//   CAP-WFL-006 Sequential and parallel review paths
//
// Every refusal here happens when the catalogue loads rather than when it is read. A process
// that depends on which file was read first is not a process, and the failure would show up as
// the wrong person being asked - which looks like nothing being wrong at all.
public sealed class WorkflowCatalogueTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"epi-routing-{Guid.NewGuid():n}");

    public WorkflowCatalogueTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private void Write(string file, string name, string? labelType, string? market, string rules)
    {
        var appliesTo = (labelType, market) switch
        {
            (null, null) => "{}",
            (not null, null) => $"{{\"labelType\": \"{labelType}\"}}",
            (null, not null) => $"{{\"market\": \"{market}\"}}",
            _ => $"{{\"labelType\": \"{labelType}\", \"market\": \"{market}\"}}",
        };

        File.WriteAllText(
            Path.Combine(_directory, file),
            $"{{\"name\": \"{name}\", \"appliesTo\": {appliesTo}, \"rules\": [{rules}]}}");
    }

    private const string OneReview =
        """{"state": "in-review", "action": "approve", "assignee": "approver"}""";

    private void WriteDefault() => Write("default.json", "default", null, null, OneReview);

    [Fact]
    public void FN_WFL_004_the_default_model_answers_when_nothing_more_specific_applies()
    {
        WriteDefault();

        var catalogue = WorkflowCatalogue.LoadFrom(_directory);

        Assert.Equal("default", catalogue.For("leaflet", "GB").Name);
    }

    [Fact]
    public void FN_WFL_004_a_model_for_both_beats_one_for_the_label_type_alone()
    {
        WriteDefault();
        Write("leaflet.json", "leaflet", "leaflet", null, OneReview);
        Write("leaflet-gb.json", "leaflet-gb", "leaflet", "GB", OneReview);

        var catalogue = WorkflowCatalogue.LoadFrom(_directory);

        Assert.Equal("leaflet-gb", catalogue.For("leaflet", "GB").Name);
    }

    [Fact]
    public void FN_WFL_004_the_label_type_beats_the_market()
    {
        // ADR-035 decision 2. A process is more often shaped by what the document is than by
        // where it is going, and an organisation for which that is wrong names both.
        WriteDefault();
        Write("leaflet.json", "leaflet", "leaflet", null, OneReview);
        Write("gb.json", "gb", null, "GB", OneReview);

        var catalogue = WorkflowCatalogue.LoadFrom(_directory);

        Assert.Equal("leaflet", catalogue.For("leaflet", "GB").Name);
    }

    [Fact]
    public void FN_WFL_004_the_market_answers_where_the_label_type_does_not_match()
    {
        WriteDefault();
        Write("leaflet.json", "leaflet", "leaflet", null, OneReview);
        Write("gb.json", "gb", null, "GB", OneReview);

        Assert.Equal("gb", WorkflowCatalogue.LoadFrom(_directory).For("smpc", "GB").Name);
    }

    [Fact]
    public void FN_WFL_004_content_with_no_label_type_still_gets_a_process()
    {
        // Nothing obliges a document to declare a type, and a document that does not is still
        // reviewed by somebody. Falling through to the default is the only honest answer.
        WriteDefault();
        Write("leaflet.json", "leaflet", "leaflet", null, OneReview);

        Assert.Equal("default", WorkflowCatalogue.LoadFrom(_directory).For(null, "GB").Name);
    }

    [Fact]
    public void FN_WFL_004_two_models_claiming_the_same_ground_are_refused()
    {
        WriteDefault();
        Write("gb-one.json", "gb-one", "leaflet", "GB", OneReview);
        Write("gb-two.json", "gb-two", "leaflet", "GB", OneReview);

        var refusal = Assert.Throws<LifecycleConfigurationException>(
            () => WorkflowCatalogue.LoadFrom(_directory));

        Assert.Contains("gb-one", string.Join(" ", refusal.Problems), StringComparison.Ordinal);
    }

    [Fact]
    public void FN_WFL_004_two_default_models_are_refused()
    {
        WriteDefault();
        Write("also-default.json", "also-default", null, null, OneReview);

        Assert.Throws<LifecycleConfigurationException>(() => WorkflowCatalogue.LoadFrom(_directory));
    }

    [Fact]
    public void FN_WFL_004_a_catalogue_with_no_default_is_refused()
    {
        // A label type nobody wrote a model for would be routed to nobody, and a review nobody
        // was asked for looks exactly like a review everybody passed.
        Write("leaflet.json", "leaflet", "leaflet", null, OneReview);

        Assert.Throws<LifecycleConfigurationException>(() => WorkflowCatalogue.LoadFrom(_directory));
    }

    [Fact]
    public void FN_WFL_004_a_directory_that_is_not_there_is_refused()
    {
        Assert.Throws<LifecycleConfigurationException>(
            () => WorkflowCatalogue.LoadFrom(Path.Combine(_directory, "no-such-directory")));
    }

    [Fact]
    public void FN_WFL_004_one_state_may_ask_several_people_at_once()
    {
        // Parallel review (CAP-WFL-006). Sequential steps are states, because a step completing
        // is a transition (CAP-WFL-005); simultaneous asks are rules, because nothing about
        // them is sequential (ADR-035 decision 1).
        Write("default.json", "default", null, null,
            """
            {"state": "in-review", "action": "approve", "assignee": "medical-reviewer"},
            {"state": "in-review", "action": "approve", "assignee": "legal-reviewer"}
            """);

        var rules = WorkflowCatalogue.LoadFrom(_directory).For(null, null).ForState("in-review");

        Assert.Equal(
            ["legal-reviewer", "medical-reviewer"],
            rules.Select(r => r.Assignee).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void FN_WFL_004_the_same_role_asked_twice_for_one_state_is_refused()
    {
        // Two identical asks are two tasks on one person's list for one job, and closing one
        // leaves the other. Several rules for a state are several people, not several asks.
        Write("default.json", "default", null, null,
            $"{OneReview}, {OneReview}");

        Assert.Throws<LifecycleConfigurationException>(() => WorkflowCatalogue.LoadFrom(_directory));
    }

    [Fact]
    public void FN_WFL_004_a_state_nobody_routed_asks_nothing()
    {
        WriteDefault();

        Assert.Empty(WorkflowCatalogue.LoadFrom(_directory).For(null, null).ForState("draft"));
    }

    [Fact]
    public void FN_WFL_004_each_parallel_ask_keeps_its_own_due_period()
    {
        // ADR-035's consequence about escalation: several asks in one state are several due
        // dates, and the earliest is not more overdue than the others.
        Write("default.json", "default", null, null,
            """
            {"state": "in-review", "action": "approve", "assignee": "medical-reviewer", "withinHours": 24},
            {"state": "in-review", "action": "approve", "assignee": "legal-reviewer", "withinHours": 120}
            """);

        var rules = WorkflowCatalogue.LoadFrom(_directory).For(null, null).ForState("in-review");

        Assert.Equal(
            [TimeSpan.FromHours(24), TimeSpan.FromHours(120)],
            rules.Select(r => r.Within).Order());
    }

    [Fact]
    public void FN_WFL_004_the_shipped_label_catalogue_loads()
    {
        // The one that fails if a model is added to the repository and never read.
        var catalogue = WorkflowCatalogue.LoadFrom(Path.Combine(
            RepositoryRoot(), "config", "workflow", "label"));

        Assert.NotEmpty(catalogue.For(null, null).ForState("in-review"));
    }

    [Fact]
    public void FN_WFL_004_the_shipped_variant_catalogue_loads()
    {
        var catalogue = WorkflowCatalogue.LoadFrom(Path.Combine(
            RepositoryRoot(), "config", "workflow", "variant"));

        Assert.NotEmpty(catalogue.For(null, null).ForState("in-linguistic-review"));
    }

    private static string RepositoryRoot() => Path.GetFullPath(Path.Combine(
        AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
}
