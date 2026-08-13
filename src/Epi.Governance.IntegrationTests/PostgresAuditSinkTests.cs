using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Epi.Governance.Audit;
using Epi.Governance.Tests;
using Npgsql;
using Xunit;

namespace Epi.Governance.IntegrationTests;

/// <summary>A real PostgreSQL, the same image the development stack runs.</summary>
public sealed class PostgresServer : IAsyncLifetime
{
    private const string Image = "postgres:16";
    private const int Port = 5432;

    private readonly IContainer _container = new ContainerBuilder(Image)
        .WithEnvironment("POSTGRES_USER", "epi")
        .WithEnvironment("POSTGRES_PASSWORD", "devpassword")
        .WithEnvironment("POSTGRES_DB", "epi_audit")
        .WithPortBinding(Port, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer().UntilCommandIsCompleted("pg_isready", "-U", "epi"))
        .Build();

    public string ConnectionString =>
        $"Host={_container.Hostname};Port={_container.GetMappedPublicPort(Port)};"
        + "Username=epi;Password=devpassword;Database=epi_audit";

    /// <summary>A database of its own, so tests sharing the container cannot see each other.</summary>
    public async Task<string> CreateDatabaseAsync()
    {
        var database = $"audit_{Guid.NewGuid():N}";
        await using var admin = new NpgsqlDataSourceBuilder(ConnectionString).Build();
        await using var create = admin.CreateCommand($"CREATE DATABASE {database}");
        await create.ExecuteNonQueryAsync();

        return ConnectionString.Replace("Database=epi_audit", $"Database={database}");
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
    protected override async Task<IAuditSink> CreateSinkAsync()
    {
        var sink = new PostgresAuditSink(await server.CreateDatabaseAsync());
        await sink.InitialiseAsync();
        return sink;
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
        var sink = new PostgresAuditSink(connectionString);
        await sink.InitialiseAsync();
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
