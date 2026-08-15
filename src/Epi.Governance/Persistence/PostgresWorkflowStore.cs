using Epi.Lifecycle;
using Npgsql;

namespace Epi.Governance.Persistence;

/// <summary>
/// The durable record of what routing has asked of whom (CAP-WFL-001, ADR-031).
/// </summary>
/// <remarks>
/// Append-only by trigger, like every other evidentiary table here. A task's assignment and
/// whether it is open are derived from its events on the way out rather than stored as columns:
/// a field is correct when it is written, and the history is correct always.
/// </remarks>
public sealed class PostgresWorkflowStore(string connectionString) : IWorkflowStore, IAsyncDisposable
{
    private readonly Lazy<NpgsqlDataSource> _source =
        new(() => new NpgsqlDataSourceBuilder(connectionString).Build());

    public async Task AppendAsync(TaskEvent taskEvent, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(taskEvent);

        await using var command = _source.Value.CreateCommand("""
            INSERT INTO workflow_task_event
                (task_identifier, document_identifier, document_version, kind, action, assignee,
                 actor, occurred_at, reason)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
            """);

        command.Parameters.AddWithValue(taskEvent.TaskIdentifier);
        command.Parameters.AddWithValue(taskEvent.Version.DocumentIdentifier);
        command.Parameters.AddWithValue(taskEvent.Version.Version);
        command.Parameters.AddWithValue(taskEvent.Kind.ToString());
        command.Parameters.AddWithValue(taskEvent.Action);
        command.Parameters.AddWithValue(taskEvent.Assignee);
        command.Parameters.AddWithValue(taskEvent.Actor);
        command.Parameters.AddWithValue(taskEvent.At);
        command.Parameters.AddWithValue((object?)taskEvent.Reason ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TaskEvent>> HistoryAsync(
        string taskIdentifier, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(taskIdentifier);

        await using var command = _source.Value.CreateCommand("""
            SELECT task_identifier, document_identifier, document_version, kind, action, assignee,
                   actor, occurred_at, reason
            FROM workflow_task_event
            WHERE task_identifier = $1
            ORDER BY id
            """);

        command.Parameters.AddWithValue(taskIdentifier);
        return await ReadAsync(command, cancellationToken);
    }

    public async Task<IReadOnlyList<WorkflowTask>> ForVersionAsync(
        VersionRef version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);

        await using var command = _source.Value.CreateCommand("""
            SELECT task_identifier, document_identifier, document_version, kind, action, assignee,
                   actor, occurred_at, reason
            FROM workflow_task_event
            WHERE document_identifier = $1 AND document_version = $2
            ORDER BY id
            """);

        command.Parameters.AddWithValue(version.DocumentIdentifier);
        command.Parameters.AddWithValue(version.Version);

        return WorkflowTasks.Derive(await ReadAsync(command, cancellationToken));
    }

    public async Task<IReadOnlyList<WorkflowTask>> OpenForAsync(
        IReadOnlyCollection<string> assignees, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(assignees);

        // An empty set matches nothing rather than everything. Rendered into SQL as an absent
        // predicate it would return every open task in the deployment, which is the failure
        // ADR-022 decision 3 exists to prevent, arriving in a different query.
        if (assignees.Count == 0)
        {
            return [];
        }

        // Every event of every task those assignees currently hold, so the derivation sees whole
        // histories: a task reassigned away from a role is no longer theirs, and only the last
        // event says so.
        await using var command = _source.Value.CreateCommand("""
            SELECT e.task_identifier, e.document_identifier, e.document_version, e.kind, e.action,
                   e.assignee, e.actor, e.occurred_at, e.reason
            FROM workflow_task_event e
            WHERE e.task_identifier IN (
                SELECT DISTINCT task_identifier FROM workflow_task_event WHERE assignee = ANY($1))
            ORDER BY e.id
            """);

        command.Parameters.AddWithValue(assignees.ToArray());

        var wanted = assignees.ToHashSet(StringComparer.Ordinal);
        return
        [
            .. WorkflowTasks.Derive(await ReadAsync(command, cancellationToken))
                .Where(task => task.IsOpen && wanted.Contains(task.Assignee)),
        ];
    }

    private static async Task<IReadOnlyList<TaskEvent>> ReadAsync(
        NpgsqlCommand command, CancellationToken cancellationToken)
    {
        var events = new List<TaskEvent>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(new TaskEvent(
                reader.GetString(0),
                new VersionRef(reader.GetString(1), reader.GetInt32(2)),
                Enum.Parse<TaskEventKind>(reader.GetString(3)),
                reader.GetString(4),
                reader.GetString(5),
                reader.GetString(6),
                reader.GetFieldValue<DateTimeOffset>(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return events;
    }

    public async ValueTask DisposeAsync()
    {
        if (_source.IsValueCreated)
        {
            await _source.Value.DisposeAsync();
        }
    }
}
