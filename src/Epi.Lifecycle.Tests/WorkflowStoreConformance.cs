using Xunit;

namespace Epi.Lifecycle.Tests;

/// <summary>
/// The behaviour every workflow store must exhibit, whatever backs it (FN-WFL-001).
/// </summary>
/// <remarks>
/// Shared source, run once against the in-memory store and once against a real PostgreSQL. The
/// cases that matter here are about derivation - what a task's assignment and openness are after
/// a sequence of events - and a store that computed them in SQL rather than from the shared
/// derivation could pass in memory and disagree in the database.
/// </remarks>
public abstract class WorkflowStoreConformance : IAsyncDisposable
{
    private static readonly VersionRef Version = new("doc-1", 1);

    private static readonly DateTimeOffset Noon = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private const string Task = "task-1";

    private readonly List<IWorkflowStore> _created = [];

    protected abstract Task<IWorkflowStore> CreateStoreAsync();

    private async Task<IWorkflowStore> NewStoreAsync()
    {
        var store = await CreateStoreAsync();
        _created.Add(store);
        return store;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var store in _created)
        {
            if (store is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
        }

        GC.SuppressFinalize(this);
    }

    private static TaskEvent Event(
        TaskEventKind kind, string assignee, int minutes = 0, string task = Task,
        VersionRef? version = null) =>
        new(task, version ?? Version, kind, "approve", assignee, "user-anna",
            Noon.AddMinutes(minutes), $"{kind}");

    [Fact]
    public async Task FN_WFL_001_a_version_nobody_was_asked_about_has_no_tasks()
    {
        var store = await NewStoreAsync();

        Assert.Empty(await store.ForVersionAsync(Version));
        Assert.Empty(await store.HistoryAsync(Task));
    }

    [Fact]
    public async Task FN_WFL_001_a_raised_task_is_open_and_assigned_where_it_was_raised()
    {
        var store = await NewStoreAsync();
        await store.AppendAsync(Event(TaskEventKind.Raised, "approver"));

        var task = Assert.Single(await store.ForVersionAsync(Version));

        Assert.Equal(Task, task.Identifier);
        Assert.Equal("approve", task.Action);
        Assert.Equal("approver", task.Assignee);
        Assert.Equal(Noon, task.RaisedAt);
        Assert.True(task.IsOpen);
    }

    [Fact]
    public async Task FN_WFL_001_the_latest_event_says_who_holds_a_task_now()
    {
        var store = await NewStoreAsync();
        await store.AppendAsync(Event(TaskEventKind.Raised, "approver"));
        await store.AppendAsync(Event(TaskEventKind.Reassigned, "user-ben", 10));

        Assert.Equal("user-ben", (await store.ForVersionAsync(Version))[0].Assignee);
    }

    [Fact]
    public async Task FN_WFL_001_reassignment_leaves_the_whole_sequence_behind_it()
    {
        // Who a task moved between is part of the record of how a version came to be approved,
        // so it is a sequence rather than a current value (ADR-031 decision 5).
        var store = await NewStoreAsync();
        await store.AppendAsync(Event(TaskEventKind.Raised, "approver"));
        await store.AppendAsync(Event(TaskEventKind.Reassigned, "user-ben", 10));
        await store.AppendAsync(Event(TaskEventKind.Reassigned, "user-rae", 20));

        var history = await store.HistoryAsync(Task);

        Assert.Equal(["approver", "user-ben", "user-rae"], history.Select(e => e.Assignee));
        Assert.Equal(
            [TaskEventKind.Raised, TaskEventKind.Reassigned, TaskEventKind.Reassigned],
            history.Select(e => e.Kind));
    }

    [Fact]
    public async Task FN_WFL_001_a_closed_task_is_no_longer_open()
    {
        var store = await NewStoreAsync();
        await store.AppendAsync(Event(TaskEventKind.Raised, "approver"));
        await store.AppendAsync(Event(TaskEventKind.Closed, "approver", 10));

        Assert.False((await store.ForVersionAsync(Version))[0].IsOpen);
        Assert.Empty(await store.OpenForAsync(["approver"]));
    }

    [Fact]
    public async Task FN_WFL_001_open_tasks_are_found_by_who_holds_them_now()
    {
        var store = await NewStoreAsync();
        await store.AppendAsync(Event(TaskEventKind.Raised, "approver"));
        await store.AppendAsync(Event(TaskEventKind.Reassigned, "user-ben", 10));

        // Reassigned away, so it is no longer the role's - and only the last event says so.
        Assert.Empty(await store.OpenForAsync(["approver"]));
        Assert.Single(await store.OpenForAsync(["user-ben"]));
    }

    [Fact]
    public async Task FN_WFL_001_asking_for_nobody_returns_nothing_rather_than_everything()
    {
        // An empty set rendered into a query becomes an absent predicate, which would return
        // every open task in the deployment.
        var store = await NewStoreAsync();
        await store.AppendAsync(Event(TaskEventKind.Raised, "approver"));

        Assert.Empty(await store.OpenForAsync([]));
    }

    [Fact]
    public async Task FN_WFL_001_tasks_are_kept_apart_by_version_and_by_task()
    {
        var store = await NewStoreAsync();
        await store.AppendAsync(Event(TaskEventKind.Raised, "approver"));
        await store.AppendAsync(Event(
            TaskEventKind.Raised, "approver", 5, task: "task-2",
            version: new VersionRef("doc-1", 2)));

        Assert.Equal(Task, Assert.Single(await store.ForVersionAsync(Version)).Identifier);
        Assert.Equal("task-2",
            Assert.Single(await store.ForVersionAsync(new VersionRef("doc-1", 2))).Identifier);
        Assert.Single(await store.HistoryAsync(Task));
    }

    [Fact]
    public async Task FN_WFL_001_several_open_tasks_for_one_role_all_come_back()
    {
        var store = await NewStoreAsync();
        await store.AppendAsync(Event(TaskEventKind.Raised, "approver"));
        await store.AppendAsync(Event(
            TaskEventKind.Raised, "approver", 5, task: "task-2",
            version: new VersionRef("doc-2", 1)));

        Assert.Equal(2, (await store.OpenForAsync(["approver"])).Count);
    }
}
