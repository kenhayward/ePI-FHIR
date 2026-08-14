using Epi.Lifecycle;
using Npgsql;

namespace Epi.Governance.Persistence;

/// <summary>
/// The durable lifecycle store: append-only registration and transition tables
/// (ADR-019 decision 6, CAP-LCM-007).
/// </summary>
/// <remarks>
/// Two tables rather than one. Registration records who authored a version and where it starts;
/// transitions record every move since. The current state is derived from the last transition
/// rather than stored, so there is no field anyone could set to a state the version never
/// reached by a permitted route.
/// </remarks>
public sealed class PostgresLifecycleStore(string connectionString)
    : ILifecycleStore, IAsyncDisposable
{
    private readonly Lazy<NpgsqlDataSource> _source =
        new(() => new NpgsqlDataSourceBuilder(connectionString).Build());

    /// <summary>Creates the tables and the triggers that make them append-only.</summary>
    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        await using var command = _source.Value.CreateCommand("""
            CREATE TABLE IF NOT EXISTS lifecycle_version (
                document_identifier TEXT    NOT NULL,
                document_version    INTEGER NOT NULL,
                author              TEXT    NOT NULL,
                initial_state       TEXT    NOT NULL,
                registered_at       TIMESTAMPTZ NULL,
                PRIMARY KEY (document_identifier, document_version)
            );

            CREATE TABLE IF NOT EXISTS lifecycle_transition (
                id                  BIGINT      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                document_identifier TEXT        NOT NULL,
                document_version    INTEGER     NOT NULL,
                from_state          TEXT        NOT NULL,
                to_state            TEXT        NOT NULL,
                action              TEXT        NOT NULL,
                actor               TEXT        NOT NULL,
                occurred_at         TIMESTAMPTZ NOT NULL,
                reason              TEXT        NULL,
                signature_reference TEXT        NULL
            );

            CREATE INDEX IF NOT EXISTS lifecycle_transition_by_version
                ON lifecycle_transition (document_identifier, document_version, id);

            -- A signature is spent platform-wide, so this lookup is by reference alone.
            CREATE INDEX IF NOT EXISTS lifecycle_transition_by_signature
                ON lifecycle_transition (signature_reference)
                WHERE signature_reference IS NOT NULL;

            -- CREATE TABLE IF NOT EXISTS does nothing at all to a table that already exists,
            -- so a column added later never appears in a database that predates it, and the
            -- first write afterwards fails. Nullable rather than backfilled with a default:
            -- inventing a registration time for rows written before the column existed would
            -- put a false timestamp in an evidentiary table, and absence of a recorded time is
            -- not evidence that the version did not exist. This is a bootstrap, not a
            -- migration - D3 Section 10.3 is where the real one belongs.
            ALTER TABLE lifecycle_version ADD COLUMN IF NOT EXISTS registered_at TIMESTAMPTZ NULL;

            CREATE OR REPLACE FUNCTION lifecycle_is_append_only() RETURNS TRIGGER AS $$
            BEGIN
                RAISE EXCEPTION '% is append-only: % is not permitted', TG_TABLE_NAME, TG_OP
                    USING ERRCODE = 'restrict_violation';
            END;
            $$ LANGUAGE plpgsql;

            DROP TRIGGER IF EXISTS lifecycle_version_no_change ON lifecycle_version;
            CREATE TRIGGER lifecycle_version_no_change
                BEFORE UPDATE OR DELETE ON lifecycle_version
                FOR EACH ROW EXECUTE FUNCTION lifecycle_is_append_only();

            DROP TRIGGER IF EXISTS lifecycle_transition_no_change ON lifecycle_transition;
            CREATE TRIGGER lifecycle_transition_no_change
                BEFORE UPDATE OR DELETE ON lifecycle_transition
                FOR EACH ROW EXECUTE FUNCTION lifecycle_is_append_only();
            """);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task RegisterAsync(
        VersionRef version, string author, string initialState, DateTimeOffset registeredAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);

        // No ON CONFLICT clause, deliberately. Re-registering would rewrite the recorded
        // author, which is what segregation of duties is checked against (CAP-IAM-006), so the
        // primary key refuses it.
        await using var command = _source.Value.CreateCommand("""
            INSERT INTO lifecycle_version
                (document_identifier, document_version, author, initial_state, registered_at)
            VALUES ($1, $2, $3, $4, $5)
            """);

        command.Parameters.AddWithValue(version.DocumentIdentifier);
        command.Parameters.AddWithValue(version.Version);
        command.Parameters.AddWithValue(author);
        command.Parameters.AddWithValue(initialState);
        command.Parameters.AddWithValue(registeredAt);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<DateTimeOffset?> RegisteredAtAsync(
        VersionRef version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);

        await using var command = _source.Value.CreateCommand("""
            SELECT registered_at FROM lifecycle_version
            WHERE document_identifier = $1 AND document_version = $2
            """);

        command.Parameters.AddWithValue(version.DocumentIdentifier);
        command.Parameters.AddWithValue(version.Version);

        // Read through the reader rather than as a scalar cast. Npgsql hands back a DateTime
        // for timestamptz, so "as DateTimeOffset?" is null for every row that has one - a
        // silent wrong answer rather than an error, and one that reads as "never registered".
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken) || reader.IsDBNull(0))
        {
            return null;
        }

        return reader.GetFieldValue<DateTimeOffset>(0);
    }

    public async Task<string?> AuthorOfAsync(
        VersionRef version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);

        await using var command = _source.Value.CreateCommand("""
            SELECT author FROM lifecycle_version
            WHERE document_identifier = $1 AND document_version = $2
            """);

        command.Parameters.AddWithValue(version.DocumentIdentifier);
        command.Parameters.AddWithValue(version.Version);

        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task<string?> CurrentStateAsync(
        VersionRef version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);

        // Derived: the destination of the last transition, or the state it was registered in.
        // Null when the version was never registered, so a caller cannot transition something
        // the platform has never seen.
        await using var command = _source.Value.CreateCommand("""
            SELECT COALESCE(
                (SELECT to_state FROM lifecycle_transition
                 WHERE document_identifier = $1 AND document_version = $2
                 ORDER BY id DESC LIMIT 1),
                (SELECT initial_state FROM lifecycle_version
                 WHERE document_identifier = $1 AND document_version = $2))
            """);

        command.Parameters.AddWithValue(version.DocumentIdentifier);
        command.Parameters.AddWithValue(version.Version);

        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

    public async Task<IReadOnlyList<StateTransition>> HistoryAsync(
        VersionRef version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);

        await using var command = _source.Value.CreateCommand("""
            SELECT from_state, to_state, action, actor, occurred_at, reason, signature_reference
            FROM lifecycle_transition
            WHERE document_identifier = $1 AND document_version = $2
            ORDER BY id
            """);

        command.Parameters.AddWithValue(version.DocumentIdentifier);
        command.Parameters.AddWithValue(version.Version);

        var transitions = new List<StateTransition>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            transitions.Add(new StateTransition(
                version,
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetFieldValue<DateTimeOffset>(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
        }

        return transitions;
    }

    public async Task<bool> IsSignatureUsedAsync(
        string reference, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reference);

        await using var command = _source.Value.CreateCommand(
            "SELECT EXISTS (SELECT 1 FROM lifecycle_transition WHERE signature_reference = $1)");

        command.Parameters.AddWithValue(reference);

        return await command.ExecuteScalarAsync(cancellationToken) is true;
    }

    public async Task AppendAsync(
        StateTransition transition, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);

        await using var command = _source.Value.CreateCommand("""
            INSERT INTO lifecycle_transition
                (document_identifier, document_version, from_state, to_state, action, actor,
                 occurred_at, reason, signature_reference)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
            """);

        command.Parameters.AddWithValue(transition.Version.DocumentIdentifier);
        command.Parameters.AddWithValue(transition.Version.Version);
        command.Parameters.AddWithValue(transition.From);
        command.Parameters.AddWithValue(transition.To);
        command.Parameters.AddWithValue(transition.Action);
        command.Parameters.AddWithValue(transition.Actor);
        command.Parameters.AddWithValue(transition.At);
        command.Parameters.AddWithValue((object?)transition.Reason ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)transition.SignatureReference ?? DBNull.Value);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        if (_source.IsValueCreated)
        {
            await _source.Value.DisposeAsync();
        }
    }
}
