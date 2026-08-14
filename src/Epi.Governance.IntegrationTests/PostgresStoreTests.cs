using Epi.ContentCore;
using Epi.Governance.Persistence;
using Epi.Lifecycle;
using Epi.Lifecycle.Tests;
using Epi.Signature;
using Npgsql;
using Xunit;

namespace Epi.Governance.IntegrationTests;

/// <summary>The durable lifecycle store, held to the contract every one must meet.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Container")]
public sealed class PostgresLifecycleStoreConformanceTests(PostgresServer server)
    : LifecycleStoreConformance
{
    protected override async Task<ILifecycleStore> CreateStoreAsync()
    {
        var connectionString = await server.CreateDatabaseAsync();
        await GovernanceSchema.ApplyAsync(connectionString);
        var store = new PostgresLifecycleStore(connectionString);
        return store;
    }
}

/// <summary>The durable market approval store, held to the same contract.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Container")]
public sealed class PostgresMarketApprovalStoreConformanceTests(PostgresServer server)
    : MarketApprovalStoreConformance
{
    protected override async Task<IMarketApprovalStore> CreateStoreAsync()
    {
        var connectionString = await server.CreateDatabaseAsync();
        await GovernanceSchema.ApplyAsync(connectionString);
        var store = new PostgresMarketApprovalStore(connectionString);
        return store;
    }
}

/// <summary>The durable signature store, held to the same contract.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Container")]
public sealed class PostgresSignatureStoreConformanceTests(PostgresServer server)
    : Epi.Signature.Tests.SignatureStoreConformance
{
    protected override async Task<ISignatureStore> CreateStoreAsync()
    {
        var connectionString = await server.CreateDatabaseAsync();
        await GovernanceSchema.ApplyAsync(connectionString);
        var store = new PostgresSignatureStore(connectionString);
        return store;
    }
}

/// <summary>The durable pinned-context store, held to the same contract.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Container")]
public sealed class PostgresPinnedContextStoreConformanceTests(PostgresServer server)
    : PinnedContextStoreConformance
{
    protected override async Task<ILifecycleStore> CreateStoreAsync()
    {
        var connectionString = await server.CreateDatabaseAsync();
        await GovernanceSchema.ApplyAsync(connectionString);
        return new PostgresLifecycleStore(connectionString);
    }
}

/// <summary>
/// The guarantee the durable stores exist to provide, and which an in-memory one cannot make:
/// append-only enforced by the database rather than by the application.
/// </summary>
/// <remarks>
/// Asserted per table. A trigger on one table proves nothing about the others, and a governance
/// record that could be amended anywhere is amendable.
/// </remarks>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Container")]
public sealed class PostgresGovernanceRecordsAreAppendOnlyTests(PostgresServer server)
{
    private static readonly VersionRef Version = new("doc-1", 1);

    private static readonly DocumentIdentity Document =
        new(ContentCoreDefaults.DocumentIdentifierSystem, "018f2a10-0000-7000-8000-000000000001");

    /// <summary>A database holding one record in each governance table.</summary>
    private async Task<string> PopulatedAsync()
    {
        var connectionString = await server.CreateDatabaseAsync();
        await GovernanceSchema.ApplyAsync(connectionString);

        var lifecycle = new PostgresLifecycleStore(connectionString);
        await lifecycle.RegisterAsync(Version, "user-anna", "draft", DateTimeOffset.UtcNow);
        await lifecycle.AppendAsync(new StateTransition(
            Version, "draft", "in-review", "submit", "user-anna", DateTimeOffset.UtcNow));

        var markets = new PostgresMarketApprovalStore(connectionString);
        await markets.AppendAsync(new MarketStateTransition(
            new MarketVersion(Version, "GB"), "not-submitted", "submitted", "submit",
            "user-rae", DateTimeOffset.UtcNow, SignatureReference: "sig-GB"));

        var signatures = new PostgresSignatureStore(connectionString);
        await signatures.AppendAsync(new SignatureManifest(
            "sig-GB", "user-rae", "Rae Lindqvist", SignatureMeaning.Responsibility,
            Document, 1, "sha-256:abc", DateTimeOffset.UtcNow));

        await lifecycle.AppendAsync(
            new StateTransition(
                Version, "in-review", "approved", "approve", "user-ben", DateTimeOffset.UtcNow),
            new PinnedContext(
                Version, "sha-256:abc", "label", "approved",
                [new PinnedPackage("hl7.fhir.uv.emedicinal-product-info", "1.0.0", "c99767")],
                "https://epi.example.org/identifier/document", DateTimeOffset.UtcNow, "smpc-gb", 3));

        return connectionString;
    }

    [Theory]
    [InlineData("lifecycle_version", "author")]
    [InlineData("lifecycle_transition", "actor")]
    [InlineData("market_approval_transition", "actor")]
    [InlineData("signature_manifest", "signer_identifier")]
    [InlineData("pinned_context", "content_hash")]
    public async Task FN_AUD_003_the_database_refuses_an_update_even_from_a_direct_connection(
        string table, string column)
    {
        // An application-level guarantee protects against the application. This protects
        // against anything holding a connection - a later service, a migration script, a
        // person with psql - which is what a GxP record actually requires.
        await using var source = new NpgsqlDataSourceBuilder(await PopulatedAsync()).Build();
        await using var update = source.CreateCommand($"UPDATE {table} SET {column} = 'someone-else'");

        var error = await Assert.ThrowsAsync<PostgresException>(() => update.ExecuteNonQueryAsync());

        Assert.Contains("append-only", error.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("lifecycle_version")]
    [InlineData("lifecycle_transition")]
    [InlineData("market_approval_transition")]
    [InlineData("signature_manifest")]
    [InlineData("pinned_context")]
    public async Task FN_AUD_003_the_database_refuses_a_delete_even_from_a_direct_connection(
        string table)
    {
        await using var source = new NpgsqlDataSourceBuilder(await PopulatedAsync()).Build();
        await using var delete = source.CreateCommand($"DELETE FROM {table}");

        var error = await Assert.ThrowsAsync<PostgresException>(() => delete.ExecuteNonQueryAsync());

        Assert.Contains("append-only", error.MessageText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FN_AUD_003_the_records_survive_a_refused_deletion()
    {
        // The refusal is only useful if the record is still there afterwards. A trigger that
        // raised after the row had gone would fail the test above and still lose the evidence.
        var connectionString = await PopulatedAsync();

        foreach (var table in new[]
                 {
                     "lifecycle_version", "lifecycle_transition",
                     "market_approval_transition", "signature_manifest", "pinned_context",
                 })
        {
            await using var source = new NpgsqlDataSourceBuilder(connectionString).Build();
            await using var delete = source.CreateCommand($"DELETE FROM {table}");
            await Assert.ThrowsAsync<PostgresException>(() => delete.ExecuteNonQueryAsync());
        }

        var lifecycle = new PostgresLifecycleStore(connectionString);
        Assert.Equal("user-anna", await lifecycle.AuthorOfAsync(Version));
        Assert.Equal(["submit", "approve"],
            (await lifecycle.HistoryAsync(Version)).Select(t => t.Action));
        Assert.NotNull(await lifecycle.ForAsync(Version));

        var markets = new PostgresMarketApprovalStore(connectionString);
        Assert.Equal("submitted", await markets.CurrentStateAsync(new MarketVersion(Version, "GB")));

        var signatures = new PostgresSignatureStore(connectionString);
        Assert.NotNull(await signatures.FindAsync("sig-GB"));
    }

    [Fact]
    public async Task FN_WFL_003_a_signature_spent_in_either_store_is_spent_everywhere()
    {
        // Single use has to hold across both durable stores, not only across the in-memory
        // ones. Neither can see the other's table, so this is what SpentSignatures is for.
        var connectionString = await PopulatedAsync();
        var lifecycle = new PostgresLifecycleStore(connectionString);
        var markets = new PostgresMarketApprovalStore(connectionString);
        var spent = new SpentSignatures(lifecycle, markets);

        Assert.True(await spent.IsSignatureUsedAsync("sig-GB"));
        Assert.False(await spent.IsSignatureUsedAsync("sig-never-used"));
    }
}
