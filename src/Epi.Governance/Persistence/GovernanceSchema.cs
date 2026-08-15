using Npgsql;

namespace Epi.Governance.Persistence;

/// <summary>One ordered, recorded change to the governance schema (ADR-024 decision 5).</summary>
public sealed record SchemaMigration(string Id, string Sql);

/// <summary>
/// Applies the governance schema as ordered migrations, recorded in a ledger.
/// </summary>
/// <remarks>
/// Replaces the per-store <c>InitialiseAsync</c> bootstraps. Those used
/// <c>CREATE TABLE IF NOT EXISTS</c>, which does nothing at all to a table that already exists -
/// so a column added later never appeared in a database that predated it, and the first write
/// afterwards failed. CI could not see it, because CI starts from an empty database every time.
/// <para>
/// Deliberately small: a ledger, a loop and a transaction per migration. A migration framework
/// is a dependency and a conventions layer for what is currently a few hundred lines of DDL,
/// and swapping this for one later is a contained change (ADR-024 alternatives).
/// </para>
/// </remarks>
public static class GovernanceSchema
{
    /// <summary>The ledger, and the trigger that keeps it append-only.</summary>
    private const string Ledger = """
        CREATE TABLE IF NOT EXISTS schema_migration (
            id         TEXT        PRIMARY KEY,
            applied_at TIMESTAMPTZ NOT NULL
        );

        CREATE OR REPLACE FUNCTION schema_migration_is_append_only() RETURNS TRIGGER AS $$
        BEGIN
            RAISE EXCEPTION 'schema_migration is append-only: % is not permitted',
                TG_OP USING ERRCODE = 'restrict_violation';
        END;
        $$ LANGUAGE plpgsql;

        DROP TRIGGER IF EXISTS schema_migration_no_change ON schema_migration;
        CREATE TRIGGER schema_migration_no_change
            BEFORE UPDATE OR DELETE ON schema_migration
            FOR EACH ROW EXECUTE FUNCTION schema_migration_is_append_only();
        """;

    /// <summary>
    /// The governance schema, in order. Never edit a migration that has shipped - add another.
    /// </summary>
    public static IReadOnlyList<SchemaMigration> Migrations { get; } =
    [
        // The append-only audit trail (iteration 1, ADR-018).
        new("0001-audit-record", """
            CREATE TABLE IF NOT EXISTS audit_record (
                id            BIGINT      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                actor         TEXT        NOT NULL,
                action        TEXT        NOT NULL,
                target        TEXT        NOT NULL,
                outcome       TEXT        NOT NULL,
                recorded_at   TIMESTAMPTZ NOT NULL,
                before_state  TEXT        NULL,
                after_state   TEXT        NULL,
                reason        TEXT        NULL
            );
            CREATE OR REPLACE FUNCTION audit_record_is_append_only() RETURNS TRIGGER AS $$
            BEGIN
                RAISE EXCEPTION 'audit_record is append-only: % is not permitted', TG_OP
                    USING ERRCODE = 'restrict_violation';
            END;
            $$ LANGUAGE plpgsql;
            DROP TRIGGER IF EXISTS audit_record_no_change ON audit_record;
            CREATE TRIGGER audit_record_no_change
                BEFORE UPDATE OR DELETE ON audit_record
                FOR EACH ROW EXECUTE FUNCTION audit_record_is_append_only();
            """),

        // Internal lifecycle state: who authored a version, and every transition (ADR-019).
        new("0002-lifecycle", """
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
            """),

        // Per-market regulatory-approval state, separate from internal state (ADR-005).
        new("0003-market-approval", """
            CREATE TABLE IF NOT EXISTS market_approval_transition (
                id                  BIGINT      GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
                document_identifier TEXT        NOT NULL,
                document_version    INTEGER     NOT NULL,
                market              TEXT        NOT NULL,
                from_state          TEXT        NOT NULL,
                to_state            TEXT        NOT NULL,
                action              TEXT        NOT NULL,
                actor               TEXT        NOT NULL,
                occurred_at         TIMESTAMPTZ NOT NULL,
                reason              TEXT        NULL,
                signature_reference TEXT        NULL
            );
            CREATE INDEX IF NOT EXISTS market_approval_by_subject
                ON market_approval_transition (document_identifier, document_version, market, id);
            CREATE INDEX IF NOT EXISTS market_approval_by_signature
                ON market_approval_transition (signature_reference)
                WHERE signature_reference IS NOT NULL;
            CREATE OR REPLACE FUNCTION market_approval_is_append_only() RETURNS TRIGGER AS $$
            BEGIN
                RAISE EXCEPTION 'market_approval_transition is append-only: % is not permitted',
                    TG_OP USING ERRCODE = 'restrict_violation';
            END;
            $$ LANGUAGE plpgsql;
            DROP TRIGGER IF EXISTS market_approval_no_change ON market_approval_transition;
            CREATE TRIGGER market_approval_no_change
                BEFORE UPDATE OR DELETE ON market_approval_transition
                FOR EACH ROW EXECUTE FUNCTION market_approval_is_append_only();
            """),

        // Electronic signature manifests (ADR-020).
        new("0004-signature-manifest", """
            CREATE TABLE IF NOT EXISTS signature_manifest (
                reference            TEXT        PRIMARY KEY,
                signer_identifier    TEXT        NOT NULL,
                signer_printed_name  TEXT        NOT NULL,
                meaning              TEXT        NOT NULL,
                document_system      TEXT        NOT NULL,
                document_value       TEXT        NOT NULL,
                document_version     INTEGER     NOT NULL,
                content_hash         TEXT        NOT NULL,
                signed_at            TIMESTAMPTZ NOT NULL,
                reason               TEXT        NULL
            );
            CREATE OR REPLACE FUNCTION signature_manifest_is_append_only() RETURNS TRIGGER AS $$
            BEGIN
                RAISE EXCEPTION 'signature_manifest is append-only: % is not permitted', TG_OP
                    USING ERRCODE = 'restrict_violation';
            END;
            $$ LANGUAGE plpgsql;
            DROP TRIGGER IF EXISTS signature_manifest_no_change ON signature_manifest;
            CREATE TRIGGER signature_manifest_no_change
                BEFORE UPDATE OR DELETE ON signature_manifest
                FOR EACH ROW EXECUTE FUNCTION signature_manifest_is_append_only();
            """),

        // What a version was approved against (ADR-023).
        new("0005-pinned-context", """
            CREATE TABLE IF NOT EXISTS pinned_context (
                document_identifier  TEXT        NOT NULL,
                document_version     INTEGER     NOT NULL,
                content_hash         TEXT        NOT NULL,
                state_model          TEXT        NOT NULL,
                state                TEXT        NOT NULL,
                packages             JSONB       NOT NULL,
                identifier_authority TEXT        NOT NULL,
                pinned_at            TIMESTAMPTZ NOT NULL,
                template             TEXT        NULL,
                template_version     INTEGER     NULL,
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
            """),

        // When a version came under management, so "state at a past moment" can tell a draft
        // from a version that did not exist yet. Nullable: inventing a time for rows written
        // before the column existed would put a false timestamp in an evidentiary table.
        new("0006-lifecycle-registered-at", """
            ALTER TABLE lifecycle_version ADD COLUMN IF NOT EXISTS registered_at TIMESTAMPTZ NULL;
            """),
        // When a market's approval takes effect (ADR-029). Nullable: only a transition that
        // records an approval has one, and inventing a date for the others would put an
        // effective date on a submission.
        new("0007-market-approval-effective-from", """
            ALTER TABLE market_approval_transition
                ADD COLUMN IF NOT EXISTS effective_from TIMESTAMPTZ NULL;
            """),
    ];

    /// <summary>
    /// Applies every migration not already recorded, in order.
    /// </summary>
    /// <exception cref="SchemaMigrationException">
    /// If one fails. The database is left as that migration found it, and the exception names
    /// the migration: a service running against a schema it could not fully apply is a service
    /// whose writes may or may not land.
    /// </exception>
    public static async Task ApplyAsync(
        string connectionString,
        IReadOnlyList<SchemaMigration>? migrations = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        await using var source = new NpgsqlDataSourceBuilder(connectionString).Build();

        await using (var ledger = source.CreateCommand(Ledger))
        {
            await ledger.ExecuteNonQueryAsync(cancellationToken);
        }

        var applied = (await AppliedAsync(source, cancellationToken)).ToHashSet(StringComparer.Ordinal);

        foreach (var migration in migrations ?? Migrations)
        {
            if (applied.Contains(migration.Id))
            {
                continue;
            }

            // One transaction per migration, and the ledger entry inside it: a migration that
            // half-applied, or that applied without being recorded, is a state nobody can
            // reason about afterwards.
            await using var connection = await source.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

            try
            {
                await using (var command = new NpgsqlCommand(migration.Sql, connection, transaction))
                {
                    await command.ExecuteNonQueryAsync(cancellationToken);
                }

                await using (var record = new NpgsqlCommand(
                    "INSERT INTO schema_migration (id, applied_at) VALUES ($1, now())",
                    connection, transaction))
                {
                    record.Parameters.AddWithValue(migration.Id);
                    await record.ExecuteNonQueryAsync(cancellationToken);
                }

                await transaction.CommitAsync(cancellationToken);
            }
            catch (Exception error) when (error is PostgresException or NpgsqlException)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw new SchemaMigrationException(migration.Id, error);
            }
        }
    }

    /// <summary>The migration identifiers this database has recorded, in the order applied.</summary>
    public static async Task<IReadOnlyList<string>> AppliedAsync(
        string connectionString, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        await using var source = new NpgsqlDataSourceBuilder(connectionString).Build();
        return await AppliedAsync(source, cancellationToken);
    }

    private static async Task<IReadOnlyList<string>> AppliedAsync(
        NpgsqlDataSource source, CancellationToken cancellationToken)
    {
        await using var command = source.CreateCommand(
            "SELECT id FROM schema_migration ORDER BY applied_at, id");

        var applied = new List<string>();
        try
        {
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
            {
                applied.Add(reader.GetString(0));
            }
        }
        catch (PostgresException missing) when (missing.SqlState == PostgresErrorCodes.UndefinedTable)
        {
            // No ledger yet means nothing has been applied, which is a fact rather than a fault.
            return [];
        }

        return applied;
    }
}

/// <summary>Raised when the governance schema could not be brought up to date.</summary>
public sealed class SchemaMigrationException(string id, Exception inner)
    : Exception($"Governance schema migration '{id}' failed and was rolled back: {inner.Message}", inner)
{
    public string MigrationId { get; } = id;
}
