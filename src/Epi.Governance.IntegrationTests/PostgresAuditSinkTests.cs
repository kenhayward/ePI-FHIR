using Epi.Governance.Audit;
using Epi.Governance.Persistence;
using Epi.Governance.Tests;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Epi.Governance.IntegrationTests;

/// <summary>A real PostgreSQL, the same image the development stack runs.</summary>
public sealed class PostgresServer : IAsyncLifetime
{
    /// <summary>The image the development stack runs (deploy/docker-compose).</summary>
    private const string Image = "postgres:16";

    /// <remarks>
    /// The Testcontainers PostgreSQL module rather than a hand-built container, for its
    /// readiness check. The official image runs a temporary server so initdb can create the
    /// database, then stops it and starts the real one - and pg_isready answers happily during
    /// that first phase. Tests began against the temporary server and were cut off mid-restart,
    /// which arrived as "connection reset by peer" rather than as anything about readiness.
    /// </remarks>
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image)
        .WithUsername("epi")
        .WithPassword("devpassword")
        .WithDatabase("epi_audit")
        // The default of 100 is generous for an application and thin for a suite that gives
        // every case its own database, and so its own pool.
        .WithCommand("-c", "max_connections=300")
        .Build();

    public string ConnectionString => _container.GetConnectionString();

    /// <summary>A database of its own, so tests sharing the container cannot see each other.</summary>
    /// <remarks>
    /// The pool is capped small. Each case gets its own database and therefore its own pool, and
    /// a server that allows a hundred connections does not go far when dozens of cases each want
    /// a poolful. Cases dispose their stores, and this is the belt to that pair of braces: a
    /// leaked pool costs a couple of connections rather than a share of the whole server.
    /// </remarks>
    public async Task<string> CreateDatabaseAsync()
    {
        var database = $"audit_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlDataSourceBuilder(ConnectionString).Build();
        await using var create = admin.CreateCommand($"CREATE DATABASE {database}");
        await create.ExecuteNonQueryAsync();

        return ConnectionString.Replace("Database=epi_audit", $"Database={database}")
               + ";Maximum Pool Size=3";
    }

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class PostgresCollection : ICollectionFixture<PostgresServer>
{
    public const string Name = "postgres";
}

/// <summary>The durable sink, held to the same contract as the in-memory one.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Container")]
public sealed class PostgresAuditSinkConformanceTests(PostgresServer server) : AuditSinkConformance
{
    protected override async Task<IAuditSink> CreateSinkAsync(TimeProvider? time = null)
    {
        // A database of its own per sink, so the conformance cases cannot see each other's
        // records through a shared table.
        var connectionString = await server.CreateDatabaseAsync();
        await GovernanceSchema.ApplyAsync(connectionString);
        return new PostgresAuditSink(connectionString, time);
    }
}

/// <summary>
/// The guarantee the durable sink exists to provide, and which an in-memory one cannot make:
/// append-only enforced by the database rather than by the application.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Container")]
public sealed class PostgresAppendOnlyTests(PostgresServer server)
{
    private async Task<string> SinkWithOneRecordAsync()
    {
        var connectionString = await server.CreateDatabaseAsync();
        await GovernanceSchema.ApplyAsync(connectionString);
        var sink = new PostgresAuditSink(connectionString);
        await sink.AppendAsync(
            new AuditRecord("user-anna", "content.create", "doc-1", AuditOutcome.Succeeded, default));
        return connectionString;
    }

    [Fact]
    public async Task FN_AUD_003_the_database_refuses_an_update_even_from_a_direct_connection()
    {
        // An application-level guarantee protects against the application. This protects
        // against anything holding a connection - a later service, a migration script, a
        // person with psql - which is what CAP-AUD-002 actually requires.
        await using var source = new NpgsqlDataSourceBuilder(await SinkWithOneRecordAsync()).Build();
        await using var update = source.CreateCommand("UPDATE audit_record SET actor = 'someone-else'");

        var error = await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync());

        Assert.Contains("append-only", error.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FN_AUD_003_the_database_refuses_a_delete_even_from_a_direct_connection()
    {
        await using var source = new NpgsqlDataSourceBuilder(await SinkWithOneRecordAsync()).Build();
        await using var delete = source.CreateCommand("DELETE FROM audit_record");

        var error = await Assert.ThrowsAsync<PostgresException>(() => delete.ExecuteNonQueryAsync());

        Assert.Contains("append-only", error.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FN_AUD_003_the_record_survives_a_refused_deletion()
    {
        var connectionString = await SinkWithOneRecordAsync();

        await using (var source = new NpgsqlDataSourceBuilder(connectionString).Build())
        await using (var delete = source.CreateCommand("DELETE FROM audit_record"))
        {
            await Assert.ThrowsAsync<PostgresException>(() => delete.ExecuteNonQueryAsync());
        }

        Assert.Single(await new PostgresAuditSink(connectionString).ReadAsync());
    }
}
