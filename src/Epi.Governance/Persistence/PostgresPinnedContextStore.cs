using System.Text.Json;
using Epi.Lifecycle;
using Npgsql;

namespace Epi.Governance.Persistence;

/// <summary>
/// The durable store for what a version was approved against (CAP-LCM-011, ADR-023).
/// </summary>
/// <remarks>
/// Append-only by trigger, like every other evidentiary table here, and unique per version by
/// primary key: the database refuses a second pin rather than trusting the service to check
/// first. The packages are held as JSON because they are a list whose shape belongs to the
/// record rather than to a query - nothing joins on a package, and a second table would be a
/// second thing to keep append-only for no benefit.
/// </remarks>
public sealed class PostgresPinnedContextStore(string connectionString)
    : IPinnedContextStore, IAsyncDisposable
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly Lazy<NpgsqlDataSource> _source =
        new(() => new NpgsqlDataSourceBuilder(connectionString).Build());

    /// <summary>Creates the table and the trigger that makes it append-only.</summary>
    public async Task InitialiseAsync(CancellationToken cancellationToken = default)
    {
        await using var command = _source.Value.CreateCommand("""
            CREATE TABLE IF NOT EXISTS pinned_context (
                document_identifier TEXT        NOT NULL,
                document_version    INTEGER     NOT NULL,
                content_hash        TEXT        NOT NULL,
                state_model         TEXT        NOT NULL,
                state               TEXT        NOT NULL,
                packages            JSONB       NOT NULL,
                identifier_authority TEXT       NOT NULL,
                pinned_at           TIMESTAMPTZ NOT NULL,
                template            TEXT        NULL,
                template_version    INTEGER     NULL,
                PRIMARY KEY (document_identifier, document_version)
            );

            CREATE OR REPLACE FUNCTION pinned_context_is_append_only() RETURNS TRIGGER AS $$
            BEGIN
                RAISE EXCEPTION 'pinned_context is append-only: % is not permitted',
                    TG_OP USING ERRCODE = 'restrict_violation';
            END;
            $$ LANGUAGE plpgsql;

            DROP TRIGGER IF EXISTS pinned_context_no_change ON pinned_context;
            CREATE TRIGGER pinned_context_no_change
                BEFORE UPDATE OR DELETE ON pinned_context
                FOR EACH ROW EXECUTE FUNCTION pinned_context_is_append_only();
            """);

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task PinAsync(PinnedContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await using var command = _source.Value.CreateCommand("""
            INSERT INTO pinned_context
                (document_identifier, document_version, content_hash, state_model, state,
                 packages, identifier_authority, pinned_at, template, template_version)
            VALUES ($1, $2, $3, $4, $5, $6, $7, $8, $9, $10)
            """);

        command.Parameters.AddWithValue(context.Version.DocumentIdentifier);
        command.Parameters.AddWithValue(context.Version.Version);
        command.Parameters.AddWithValue(context.ContentHash);
        command.Parameters.AddWithValue(context.StateModel);
        command.Parameters.AddWithValue(context.State);
        command.Parameters.Add(new NpgsqlParameter
        {
            Value = JsonSerializer.Serialize(context.Packages, Json),
            NpgsqlDbType = NpgsqlTypes.NpgsqlDbType.Jsonb,
        });
        command.Parameters.AddWithValue(context.IdentifierAuthority);
        command.Parameters.AddWithValue(context.PinnedAt);
        command.Parameters.AddWithValue((object?)context.Template ?? DBNull.Value);
        command.Parameters.AddWithValue((object?)context.TemplateVersion ?? DBNull.Value);

        try
        {
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
        catch (PostgresException duplicate) when (duplicate.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // The primary key is the control, not a check the service performed first: two
            // approvals racing must not both write, and only the database can decide that.
            throw new ContextAlreadyPinnedException(context.Version);
        }
    }

    public async Task<PinnedContext?> ForAsync(
        VersionRef version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(version);

        await using var command = _source.Value.CreateCommand("""
            SELECT content_hash, state_model, state, packages, identifier_authority, pinned_at,
                   template, template_version
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
            reader.IsDBNull(7) ? null : reader.GetInt32(7));
    }

    public async ValueTask DisposeAsync()
    {
        if (_source.IsValueCreated)
        {
            await _source.Value.DisposeAsync();
        }
    }
}
