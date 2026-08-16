using System.Text.Json;
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
    : ILifecycleStore, IPinnedContextStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Lazy<NpgsqlDataSource> _source =
        new(() => new NpgsqlDataSourceBuilder(connectionString).Build());

    public async Task RegisterAsync(
        VersionRef version, string author, string initialState, DateTimeOffset registeredAt,
        string kind = RegisteredArtefact.Content, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);
        ArgumentException.ThrowIfNullOrWhiteSpace(author);

        // No ON CONFLICT clause, deliberately. Re-registering would rewrite the recorded
        // author, which is what segregation of duties is checked against (CAP-IAM-006), so the
        // primary key refuses it.
        await using var command = _source.Value.CreateCommand("""
            INSERT INTO lifecycle_version
                (document_identifier, document_version, author, initial_state, registered_at,
                 artefact_kind)
            VALUES ($1, $2, $3, $4, $5, $6)
            """);

        command.Parameters.AddWithValue(version.DocumentIdentifier);
        command.Parameters.AddWithValue(version.Version);
        command.Parameters.AddWithValue(author);
        command.Parameters.AddWithValue(initialState);
        command.Parameters.AddWithValue(registeredAt);
        command.Parameters.AddWithValue(kind);

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

    public async Task<IReadOnlyList<Registration>> RegistrationsBeforeAsync(
        DateTimeOffset moment, CancellationToken cancellationToken = default)
    {
        // Exclusive of the moment itself, so a caller's settle period means what it says.
        await using var command = _source.Value.CreateCommand("""
            SELECT document_identifier, document_version, author, registered_at, artefact_kind
            FROM lifecycle_version
            WHERE registered_at < $1
            ORDER BY registered_at, document_identifier, document_version
            """);

        command.Parameters.AddWithValue(moment);

        var registrations = new List<Registration>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            registrations.Add(new Registration(
                new VersionRef(reader.GetString(0), reader.GetInt32(1)),
                reader.GetString(2),

                // GetFieldValue rather than a cast, for the reason recorded on
                // RegisteredAtAsync: Npgsql hands back a DateTime for timestamptz.
                reader.GetFieldValue<DateTimeOffset>(3),
                reader.IsDBNull(4) ? RegisteredArtefact.Content : reader.GetString(4)));
        }

        return registrations;
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

    public async Task<IReadOnlyList<int>> VersionsInStateAsync(
        string documentIdentifier, string state, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(state);

        // The state a version holds now is the last transition it made, or the state it was
        // registered in when it has made none - derived, never stored (ADR-019 decision 1).
        await using var command = _source.Value.CreateCommand("""
            SELECT v.document_version
            FROM lifecycle_version v
            LEFT JOIN LATERAL (
                SELECT to_state FROM lifecycle_transition t
                WHERE t.document_identifier = v.document_identifier
                  AND t.document_version = v.document_version
                ORDER BY t.id DESC LIMIT 1
            ) latest ON TRUE
            WHERE v.document_identifier = $1
              AND COALESCE(latest.to_state, v.initial_state) = $2
            ORDER BY v.document_version
            """);

        command.Parameters.AddWithValue(documentIdentifier);
        command.Parameters.AddWithValue(state);

        var versions = new List<int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            versions.Add(reader.GetInt32(0));
        }

        return versions;
    }

    public async Task AppendAsync(
        StateTransition transition, PinnedContext? pin = null,
        StateTransition? consequence = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(transition);

        // One transaction, because a transition that lands without its pin leaves an approved
        // version with no record of what it was approved against (ADR-024 decision 1).
        await using var connection = await _source.Value.OpenConnectionAsync(cancellationToken);
        await using var transacted = await connection.BeginTransactionAsync(cancellationToken);

        await InsertAsync(transition, connection, transacted, cancellationToken);

        if (consequence is not null)
        {
            await InsertAsync(consequence, connection, transacted, cancellationToken);
        }

        if (pin is not null)
        {
            await using var pinning = new NpgsqlCommand("""
                INSERT INTO pinned_context
                    (document_identifier, document_version, content_hash, state_model, state,
                     packages, identifier_authority, pinned_at, template, template_version,
                     terminology_bindings)
                VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10, $11)
                """, connection, transacted);

            pinning.Parameters.AddWithValue(pin.Version.DocumentIdentifier);
            pinning.Parameters.AddWithValue(pin.Version.Version);
            pinning.Parameters.AddWithValue(pin.ContentHash);
            pinning.Parameters.AddWithValue(pin.StateModel);
            pinning.Parameters.AddWithValue(pin.State);
            pinning.Parameters.Add(new NpgsqlParameter
            {
                Value = JsonSerializer.Serialize(pin.Packages, Json),
                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb,
            });
            pinning.Parameters.AddWithValue(pin.IdentifierAuthority);
            pinning.Parameters.AddWithValue(pin.PinnedAt);
            pinning.Parameters.AddWithValue((object?)pin.Template ?? DBNull.Value);
            pinning.Parameters.AddWithValue((object?)pin.TemplateVersion ?? DBNull.Value);
            pinning.Parameters.Add(new NpgsqlParameter
            {
                // Always written, even when empty. NULL is reserved for a pin taken before
                // bindings were recorded at all, and an empty array says the approval was asked
                // and had none (ADR-036 decision 3).
                Value = JsonSerializer.Serialize(pin.TerminologyBindings, Json),
                NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb,
            });

            try
            {
                await pinning.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (PostgresException duplicate)
                when (duplicate.SqlState == PostgresErrorCodes.UniqueViolation)
            {
                // The primary key is the control, not a check performed first: two approvals
                // racing must not both write, and only the database can decide that. Rolling
                // back takes the transition with it, which is the point.
                await transacted.RollbackAsync(cancellationToken);
                throw new ContextAlreadyPinnedException(pin.Version);
            }
        }

        await transacted.CommitAsync(cancellationToken);
    }

    public async Task<PinnedContext?> ForAsync(
        VersionRef version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);

        await using var command = _source.Value.CreateCommand("""
            SELECT content_hash, state_model, state, packages, identifier_authority, pinned_at,
                   template, template_version, terminology_bindings
            FROM pinned_context
            WHERE document_identifier = $1 AND document_version = $2
            """);

        command.Parameters.AddWithValue(version.DocumentIdentifier);
        command.Parameters.AddWithValue(version.Version);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new PinnedContext(
            version,
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            JsonSerializer.Deserialize<List<PinnedPackage>>(reader.GetString(3), Json) ?? [],
            reader.GetString(4),
            reader.GetFieldValue<DateTimeOffset>(5),
            reader.IsDBNull(6) ? null : reader.GetString(6),
            reader.IsDBNull(7) ? null : reader.GetInt32(7),
            reader.IsDBNull(8)
                ? null
                : JsonSerializer.Deserialize<List<TerminologyBinding>>(reader.GetString(8), Json));
    }

    private static async Task InsertAsync(
        StateTransition transition, NpgsqlConnection connection, NpgsqlTransaction transacted,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            INSERT INTO lifecycle_transition
                (document_identifier, document_version, from_state, to_state, action, actor,
                 occurred_at, reason, signature_reference)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9)
            """, connection, transacted);

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
