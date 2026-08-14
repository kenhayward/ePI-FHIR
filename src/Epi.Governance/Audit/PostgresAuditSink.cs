using Npgsql;

namespace Epi.Governance.Audit;

/// <summary>
/// The durable audit sink: an append-only table (D3 Section 3.1, ADR-013).
/// </summary>
/// <remarks>
/// <para>
/// Append-only is enforced <em>by the database</em>, not by this class. A trigger refuses
/// UPDATE and DELETE on the table, so a record cannot be altered by anything holding a
/// connection - a later service, a migration script, or a person with psql. An application-level
/// guarantee protects against the application; a database-level one protects against everyone,
/// which is what CAP-AUD-002 and an inspection actually require.
/// </para>
/// <para>
/// Long-term retention and sealed export to WORM storage are capability 22's, working from
/// this table.
/// </para>
/// </remarks>
public sealed class PostgresAuditSink(string connectionString, TimeProvider? time = null)
    : IAuditSink, IAsyncDisposable
{
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    /// <remarks>
    /// One data source for the life of the sink, built on first use. A data source owns the
    /// connection pool, so building and disposing one per statement - as this did - opens and
    /// tears down a TCP connection for every append and every read. Under a test suite doing
    /// that in a tight loop it surfaced as "attempted to read past the end of the stream": the
    /// server closing a connection the client still believed it held.
    /// </remarks>
    private readonly Lazy<NpgsqlDataSource> _source =
        new(() => new NpgsqlDataSourceBuilder(connectionString).Build());

    public async Task AppendAsync(AuditRecord record, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);

        await using var command = _source.Value.CreateCommand("""
            INSERT INTO audit_record
                (actor, action, target, outcome, recorded_at, before_state, after_state, reason)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8)
            """);

        // The sink stamps the time, not the caller (ADR-018).
        command.Parameters.AddWithValue(record.Actor);
        command.Parameters.AddWithValue(record.Action);
        command.Parameters.AddWithValue(record.Target);
        command.Parameters.AddWithValue(record.Outcome.ToString());
        command.Parameters.AddWithValue(_time.GetUtcNow());
        command.Parameters.AddWithValue((object?)record.Before ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)record.After ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)record.Reason ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AuditRecord>> ReadAsync(CancellationToken cancellationToken = default)
    {
        await using var command = _source.Value.CreateCommand("""
            SELECT actor, action, target, outcome, recorded_at, before_state, after_state, reason
            FROM audit_record
            ORDER BY id
            """);

        var records = new List<AuditRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new AuditRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                Enum.Parse<AuditOutcome>(reader.GetString(3)),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }

        return records;
    }

    public async ValueTask DisposeAsync()
    {
        if (_source.IsValueCreated)
        {
            await _source.Value.DisposeAsync();
        }
    }
}
