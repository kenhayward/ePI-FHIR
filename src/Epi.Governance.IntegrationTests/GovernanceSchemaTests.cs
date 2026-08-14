using Epi.Governance.Persistence;
using Npgsql;
using Xunit;

namespace Epi.Governance.IntegrationTests;

/// <summary>
/// The governance schema, applied as ordered migrations (ADR-024 decisions 5 to 7).
/// </summary>
/// <remarks>
/// Container-backed on purpose. The defect this replaces was invisible to every test that
/// starts from an empty database, which is what an in-memory equivalent would always do: the
/// cases that matter here are about a database that already exists.
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Container")]
public sealed class GovernanceSchemaTests(PostgresServer server)
{
    private static async Task<bool> TableExistsAsync(string connectionString, string table)
    {
        await using var source = new NpgsqlDataSourceBuilder(connectionString).Build();
        await using var command = source.CreateCommand("SELECT to_regclass($1) IS NOT NULL");
        command.Parameters.AddWithValue(table);
        return await command.ExecuteScalarAsync() is true;
    }

    private static async Task<bool> ColumnExistsAsync(
        string connectionString, string table, string column)
    {
        await using var source = new NpgsqlDataSourceBuilder(connectionString).Build();
        await using var command = source.CreateCommand("""
            SELECT EXISTS (
                SELECT 1 FROM information_schema.columns
                WHERE table_name = $1 AND column_name = $2)
            """);
        command.Parameters.AddWithValue(table);
        command.Parameters.AddWithValue(column);
        return await command.ExecuteScalarAsync() is true;
    }

    [Fact]
    public async Task FN_CFG_004_applying_the_schema_creates_every_governance_table()
    {
        var connectionString = await server.CreateDatabaseAsync();

        await GovernanceSchema.ApplyAsync(connectionString);

        foreach (var table in new[]
                 {
                     "audit_record", "lifecycle_version", "lifecycle_transition",
                     "market_approval_transition", "signature_manifest", "pinned_context",
                 })
        {
            Assert.True(await TableExistsAsync(connectionString, table), table);
        }
    }

    [Fact]
    public async Task FN_CFG_004_every_migration_is_recorded_once_and_applying_twice_changes_nothing()
    {
        var connectionString = await server.CreateDatabaseAsync();

        await GovernanceSchema.ApplyAsync(connectionString);
        var first = await GovernanceSchema.AppliedAsync(connectionString);

        await GovernanceSchema.ApplyAsync(connectionString);
        var second = await GovernanceSchema.AppliedAsync(connectionString);

        Assert.Equal(GovernanceSchema.Migrations.Select(m => m.Id), first);
        Assert.Equal(first, second);
    }

    [Fact]
    public async Task FN_CFG_004_a_migration_already_recorded_is_never_run_again()
    {
        // Proven with a migration that would fail on a second run. Asserting only that applying
        // twice does not throw would pass just as well against a runner that re-ran everything
        // and happened to use idempotent DDL - which is exactly what the old bootstrap did.
        var connectionString = await server.CreateDatabaseAsync();
        var once = new SchemaMigration("test-once", "CREATE TABLE run_once (id INTEGER)");

        await GovernanceSchema.ApplyAsync(connectionString, [once]);
        await GovernanceSchema.ApplyAsync(connectionString, [once]);

        Assert.Equal(["test-once"], await GovernanceSchema.AppliedAsync(connectionString));
    }

    [Fact]
    public async Task FN_CFG_004_a_new_migration_is_applied_to_a_database_that_predates_it()
    {
        // The defect that started this: a column added later never appeared in a database
        // created before it, and the first write afterwards failed.
        var connectionString = await server.CreateDatabaseAsync();
        var first = new SchemaMigration("test-table", "CREATE TABLE later (id INTEGER)");
        var second = new SchemaMigration("test-column", "ALTER TABLE later ADD COLUMN added TEXT");

        await GovernanceSchema.ApplyAsync(connectionString, [first]);
        Assert.False(await ColumnExistsAsync(connectionString, "later", "added"));

        await GovernanceSchema.ApplyAsync(connectionString, [first, second]);

        Assert.True(await ColumnExistsAsync(connectionString, "later", "added"));
        Assert.Equal(["test-table", "test-column"], await GovernanceSchema.AppliedAsync(connectionString));
    }

    [Fact]
    public async Task FN_CFG_004_a_failing_migration_names_itself_and_leaves_nothing_behind()
    {
        var connectionString = await server.CreateDatabaseAsync();
        var good = new SchemaMigration("test-good", "CREATE TABLE good (id INTEGER)");
        var bad = new SchemaMigration(
            "test-bad", "CREATE TABLE half (id INTEGER); THIS IS NOT SQL");

        var error = await Assert.ThrowsAsync<SchemaMigrationException>(
            () => GovernanceSchema.ApplyAsync(connectionString, [good, bad]));

        Assert.Equal("test-bad", error.MigrationId);

        // The migration before it stands; the failing one left nothing, not even its first
        // statement. A half-applied migration is the state nobody can reason about.
        Assert.True(await TableExistsAsync(connectionString, "good"));
        Assert.False(await TableExistsAsync(connectionString, "half"));
        Assert.Equal(["test-good"], await GovernanceSchema.AppliedAsync(connectionString));
    }

    [Fact]
    public async Task FN_AUD_003_the_migration_ledger_is_append_only()
    {
        // Not evidence about a label, but the record of what was done to the store that holds
        // the evidence. It costs one trigger (ADR-024 decision 6).
        var connectionString = await server.CreateDatabaseAsync();
        await GovernanceSchema.ApplyAsync(connectionString);

        await using var source = new NpgsqlDataSourceBuilder(connectionString).Build();
        await using var update = source.CreateCommand("UPDATE schema_migration SET id = 'rewritten'");
        await using var delete = source.CreateCommand("DELETE FROM schema_migration");

        Assert.Contains("append-only",
            (await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync())).MessageText,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("append-only",
            (await Assert.ThrowsAsync<PostgresException>(() => delete.ExecuteNonQueryAsync())).MessageText,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FN_CFG_004_the_shipped_schema_is_applied_in_the_order_it_is_declared()
    {
        var connectionString = await server.CreateDatabaseAsync();

        await GovernanceSchema.ApplyAsync(connectionString);

        Assert.Equal(GovernanceSchema.Migrations.Select(m => m.Id),
            await GovernanceSchema.AppliedAsync(connectionString));
        Assert.NotEmpty(GovernanceSchema.Migrations);
    }
}
