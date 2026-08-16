using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Epi.Lifecycle.Tests;

// FN-WFL-001 Routing a review to whoever is asked to make it (ADR-031).
//   CAP-WFL-001 Configurable multi-step review/approval workflows, config-as-data
public sealed class WorkflowRoutingTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly VersionRef Version = new("doc-1", 1);

    private static readonly ApprovalContext Approved = new(
        "sha-256:abc123",
        [new PinnedPackage("hl7.fhir.uv.emedicinal-product-info", "1.0.0", "c99767")],
        "https://epi.example.org/identifier/document");

    private sealed class AnySignature : ISignatureCheck
    {
        public Task<SignatureCheckResult> IsValidAsync(
            string reference, VersionRef version, string actor, string meaning,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SignatureCheckResult.Valid);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EpiPlatform.sln")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }

    private static (LifecycleService Service, InMemoryWorkflowStore Tasks) Build(bool routed = true)
    {
        var tasks = new InMemoryWorkflowStore();
        var lifecycle = LifecycleModelConfiguration.LoadFrom(
            Path.Combine(RepositoryRoot(), "config", "lifecycle", "label-states.json"));
        var workflow = routed
            ? WorkflowCatalogue.LoadFrom(
                Path.Combine(RepositoryRoot(), "config", "workflow", "label"))
            : null;

        return (new LifecycleService(
            lifecycle, new InMemoryLifecycleStore(), new FakeTimeProvider(Noon), new AnySignature(),
            workflow: workflow, tasks: routed ? tasks : null), tasks);
    }

    private static Task ApproveAsync(LifecycleService service) => service.TransitionAsync(
        Version, "approve", "user-ben", signatureReference: "sig-1", approvalContext: Approved);

    [Fact]
    public async Task CAP_WFL_001_submitting_for_review_asks_somebody_to_approve()
    {
        var (service, tasks) = Build();
        await service.RegisterAsync(Version, "user-anna");

        await service.TransitionAsync(Version, "submit", "user-anna");

        var task = Assert.Single(await tasks.ForVersionAsync(Version));
        Assert.Equal("approve", task.Action);
        Assert.Equal("approver", task.Assignee);
        Assert.True(task.IsOpen);
    }

    [Fact]
    public async Task CAP_WFL_001_making_the_transition_closes_the_task_that_asked_for_it()
    {
        // Nothing marks a task done by hand: "done" means the thing was actually done, and the
        // only evidence of that is the transition (ADR-031 decision 2).
        var (service, tasks) = Build();
        await service.RegisterAsync(Version, "user-anna");
        await service.TransitionAsync(Version, "submit", "user-anna");

        await ApproveAsync(service);

        Assert.All(await tasks.ForVersionAsync(Version), task => Assert.False(task.IsOpen));
    }

    [Fact]
    public async Task CAP_WFL_001_a_task_records_who_asked_and_who_answered()
    {
        var (service, tasks) = Build();
        await service.RegisterAsync(Version, "user-anna");
        await service.TransitionAsync(Version, "submit", "user-anna");
        var identifier = (await tasks.ForVersionAsync(Version))[0].Identifier;

        await ApproveAsync(service);

        var history = await tasks.HistoryAsync(identifier);
        Assert.Equal([TaskEventKind.Raised, TaskEventKind.Closed], history.Select(e => e.Kind));
        Assert.Equal("user-anna", history[0].Actor);
        Assert.Equal("user-ben", history[1].Actor);
    }

    [Fact]
    public async Task CAP_WFL_001_reassignment_is_recorded_rather_than_overwritten()
    {
        // Who a task moved between is part of the record of how a version came to be approved
        // (ADR-031 decision 5).
        var (service, tasks) = Build();
        await service.RegisterAsync(Version, "user-anna");
        await service.TransitionAsync(Version, "submit", "user-anna");
        var task = (await tasks.ForVersionAsync(Version))[0];

        await tasks.AppendAsync(new TaskEvent(
            task.Identifier, Version, TaskEventKind.Reassigned, task.Action, "user-ben",
            "user-anna", Noon.AddHours(1), "Ben is covering"));

        Assert.Equal("user-ben", (await tasks.ForVersionAsync(Version))[0].Assignee);
        Assert.Equal(
            ["approver", "user-ben"],
            (await tasks.HistoryAsync(task.Identifier)).Select(e => e.Assignee));
    }

    [Fact]
    public async Task CAP_WFL_001_overdue_is_asked_rather_than_scheduled()
    {
        // A task overdue is overdue whether or not anything noticed, and a job that has failed
        // for a week is indistinguishable from a queue that is empty (ADR-031 decision 6).
        var (service, tasks) = Build();
        await service.RegisterAsync(Version, "user-anna");
        await service.TransitionAsync(Version, "submit", "user-anna");

        var task = (await tasks.ForVersionAsync(Version))[0];
        var allowed = TimeSpan.FromHours(120);

        Assert.False(task.IsOverdueAt(Noon.AddHours(119), allowed));
        Assert.True(task.IsOverdueAt(Noon.AddHours(121), allowed));
    }

    [Fact]
    public async Task CAP_WFL_001_a_closed_task_is_never_overdue()
    {
        var (service, tasks) = Build();
        await service.RegisterAsync(Version, "user-anna");
        await service.TransitionAsync(Version, "submit", "user-anna");
        await ApproveAsync(service);

        var task = (await tasks.ForVersionAsync(Version))[0];

        Assert.False(task.IsOverdueAt(Noon.AddYears(1), TimeSpan.FromHours(1)));
    }

    [Fact]
    public async Task CAP_WFL_001_what_is_waiting_for_a_role_is_a_query()
    {
        var (service, tasks) = Build();
        await service.RegisterAsync(Version, "user-anna");
        await service.TransitionAsync(Version, "submit", "user-anna");

        Assert.Single(await tasks.OpenForAsync(["approver"]));
        Assert.Empty(await tasks.OpenForAsync(["author"]));

        // Empty matches nothing, not everything - the failure ADR-022 decision 3 guards the
        // search predicate against, arriving in a different query.
        Assert.Empty(await tasks.OpenForAsync([]));
    }

    [Fact]
    public async Task CAP_WFL_001_a_deployment_with_no_routing_configured_raises_no_tasks()
    {
        // Routing is optional and the gate is unaffected either way: a task never decides
        // whether a transition may happen (ADR-031 decision 1).
        var (service, tasks) = Build(routed: false);
        await service.RegisterAsync(Version, "user-anna");

        await service.TransitionAsync(Version, "submit", "user-anna");

        Assert.Empty(await tasks.ForVersionAsync(Version));
    }

    [Fact]
    public async Task CAP_WFL_001_a_state_nothing_routes_asks_nobody()
    {
        var (service, tasks) = Build();
        await service.RegisterAsync(Version, "user-anna");

        await service.TransitionAsync(Version, "withdraw", "user-anna");

        Assert.Empty(await tasks.ForVersionAsync(Version));
    }

    [Fact]
    public async Task CAP_WFL_001_a_task_identifier_is_opaque_and_addressable()
    {
        // Minted like every other identifier the platform assigns (ADR-015). A composite of the
        // version, the action and the timestamp carried meaning - and slashes and colons, so
        // the task it named could not be addressed in a URL at all.
        var (service, tasks) = Build();
        await service.RegisterAsync(Version, "user-anna");
        await service.TransitionAsync(Version, "submit", "user-anna");

        var identifier = (await tasks.ForVersionAsync(Version))[0].Identifier;

        Assert.True(Guid.TryParse(identifier, out _));
        Assert.Equal(identifier, Uri.EscapeDataString(identifier));
    }

    [Fact]
    public void CAP_WFL_001_the_shipped_routing_asks_an_approver_to_approve()
    {
        var model = WorkflowCatalogue.LoadFrom(
            Path.Combine(RepositoryRoot(), "config", "workflow", "label")).For(null, null);

        var rule = Assert.Single(model.ForState("in-review"));

        Assert.Equal("approve", rule.Action);
        Assert.Equal("approver", rule.Assignee);
        Assert.Equal(TimeSpan.FromHours(120), rule.Within);
    }

    [Fact]
    public void CAP_WFL_001_the_same_role_asked_twice_for_one_state_is_refused()
    {
        // This case used to refuse *any* two rules for one state, on the grounds that the ask
        // would depend on which was read first. ADR-035 decision 1 reverses that deliberately:
        // several rules for a state are several people asked at once, which is what parallel
        // review is (CAP-WFL-006). What remains refused is narrower and is a real defect - two
        // identical asks are two tasks on one person's list for one job, and closing one leaves
        // the other open.
        var directory = Directory.CreateTempSubdirectory("epi-workflow-").FullName;
        var path = Path.Combine(directory, "routing.json");

        try
        {
            File.WriteAllText(path, """
                {
                  "name": "duplicated",
                  "rules": [
                    {"state": "in-review", "action": "approve", "assignee": "approver"},
                    {"state": "in-review", "action": "approve", "assignee": "approver"}
                  ]
                }
                """);

            var error = Assert.Throws<LifecycleConfigurationException>(
                () => WorkflowConfiguration.LoadFrom(path));

            Assert.Contains(error.Problems, p => p.Contains("in-review", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CAP_WFL_006_a_market_with_two_reviewers_asks_them_both()
    {
        // The whole of Germany's difference is a file (config/workflow/label). If selection
        // stopped working the platform would fall back to the default and ask one person,
        // which looks exactly like a platform that is working.
        var (service, tasks) = Build();
        await service.RegisterAsync(Version, "user-anna");

        await service.TransitionAsync(
            Version, "submit", "user-anna",
            routingSubject: new RoutingSubject("package-leaflet", "DE"));

        var asked = await tasks.ForVersionAsync(Version);

        Assert.Equal(
            ["approver", "linguistic-reviewer"],
            asked.Select(t => t.Assignee).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task CAP_WFL_001_a_market_with_no_model_of_its_own_gets_the_default()
    {
        var (service, tasks) = Build();
        await service.RegisterAsync(Version, "user-anna");

        await service.TransitionAsync(
            Version, "submit", "user-anna",
            routingSubject: new RoutingSubject("package-leaflet", "GB"));

        Assert.Equal("approver", Assert.Single(await tasks.ForVersionAsync(Version)).Assignee);
    }

    [Fact]
    public async Task CAP_WFL_001_a_transition_out_closes_every_parallel_ask()
    {
        // Two asks in, and both have to end when the version leaves the state. One left open
        // would sit on somebody's list for a version that has moved on.
        var (service, tasks) = Build();
        await service.RegisterAsync(Version, "user-anna");
        await service.TransitionAsync(
            Version, "submit", "user-anna", routingSubject: new RoutingSubject("package-leaflet", "DE"));

        await service.TransitionAsync(
            Version, "approve", "user-ben", signatureReference: "sig-1",
            approvalContext: Approved, routingSubject: new RoutingSubject("package-leaflet", "DE"));

        Assert.All(await tasks.ForVersionAsync(Version), task => Assert.False(task.IsOpen));
    }
}
