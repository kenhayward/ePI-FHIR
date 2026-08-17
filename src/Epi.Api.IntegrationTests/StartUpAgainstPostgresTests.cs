using Epi.Governance.Persistence;
using Epi.Templates;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Npgsql;
using Testcontainers.PostgreSql;
using Xunit;

namespace Epi.Api.IntegrationTests;

/// <summary>
/// The API starting against a real, durable PostgreSQL (FN-CFG-004).
///   CAP-CFG-004 Apply configuration and schema changes deterministically
///
/// Every other test of the API runs on in-memory stores, which is right for what they assert and
/// blind to one whole class: what start-up does to a database. The host writes during start-up
/// now - a seeded template is registered with the lifecycle engine - and a write is only as good
/// as the schema underneath it.
/// </summary>
/// <remarks>
/// Found by a walkthrough against the development stack, not by any test: the API had been
/// applying the governance schema after seeding, so the first start-up that wrote anything died
/// with <c>column "artefact_kind" of relation "lifecycle_version" does not exist</c>. The
/// migration was correct and had simply not run yet.
/// <para>
/// An empty database is the sharpest form of the case. If migration comes second the seeding
/// write hits a table that does not exist and the host never starts, which is exactly what this
/// asserts against.
/// </para>
/// </remarks>
[Trait("Category", "Container")]
public sealed class StartUpAgainstPostgresTests : IAsyncLifetime
{
    /// <summary>The image the development stack runs (deploy/docker-compose).</summary>
    private const string Image = "postgres:16.15";

    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder(Image)
        .WithUsername("epi")
        .WithPassword("devpassword")
        .WithDatabase("epi_governance")
        .Build();

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private static string RepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null &&
               !File.Exists(Path.Combine(directory.FullName, "EpiPlatform.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException(
                "Could not locate the repository root (no EpiPlatform.sln above the test output).");
        }

        return Path.Combine([directory.FullName, .. segments]);
    }

    private WebApplicationFactory<Program> Host(string connectionString) =>
        new WebApplicationFactory<Program>().WithWebHostBuilder(host =>
        {
            host.UseSetting("Epi:MarketsPath", RepositoryPath("config", "markets"));
            host.UseSetting("Epi:IdentifiersPath", RepositoryPath("config", "identifiers.json"));
            host.UseSetting("Epi:Lifecycle:StatesPath",
                RepositoryPath("config", "lifecycle", "label-states.json"));
            host.UseSetting("Epi:Lifecycle:MarketStatesPath",
                RepositoryPath("config", "lifecycle", "market-approval-states.json"));
            host.UseSetting("Epi:Lifecycle:TemplateStatesPath",
                RepositoryPath("config", "lifecycle", "template-states.json"));
            host.UseSetting("Epi:MasterDataPath",
                RepositoryPath("config", "master-data", "products.json"));
            host.UseSetting("Epi:TemplateSeedPath", RepositoryPath("config", "templates", "seed"));
            host.UseSetting("Epi:Workflow:RoutingPath", RepositoryPath("config", "workflow", "label"));
            host.UseSetting("Epi:Governance:ConnectionString", connectionString);
        });

    private static async Task<long> CountAsync(string connectionString, string sql)
    {
        await using var source = new NpgsqlDataSourceBuilder(connectionString).Build();
        await using var command = source.CreateCommand(sql);

        return (long)(await command.ExecuteScalarAsync())!;
    }

    [Fact]
    public async Task FN_CFG_004_the_api_starts_against_a_database_it_has_never_seen()
    {
        var host = Host(_container.GetConnectionString());

        using var response = await host.CreateClient().GetAsync("/health");

        Assert.True(
            response.IsSuccessStatusCode,
            "the API must apply the governance schema before anything start-up writes uses it");
    }

    [Fact]
    public async Task FN_CFG_004_the_api_starts_a_second_time_against_the_database_it_wrote()
    {
        // A restart, which is the ordinary thing a deployment does and the one nothing tested.
        // Start-up writes now, and a write that is not idempotent is a service that comes up
        // once: seeding recreated its templates on the second start and registering them again
        // violated the lifecycle primary key, so the API died on restart. Everything looked
        // healthy right up until somebody restarted it.
        var connectionString = _container.GetConnectionString();

        var first = Host(connectionString);
        using (var started = await first.CreateClient().GetAsync("/health"))
        {
            started.EnsureSuccessStatusCode();
        }

        await first.DisposeAsync();

        var second = Host(connectionString);
        using var again = await second.CreateClient().GetAsync("/health");

        Assert.True(again.IsSuccessStatusCode, "the API must survive a restart");
    }

    [Fact]
    public async Task FN_TPL_005_a_restart_does_not_register_a_seeded_template_twice()
    {
        // The consequence beyond starting: a second registration would be a second version
        // number spent on a template nobody versioned, and a lifecycle history that says a
        // template was created twice when it was created once.
        var connectionString = _container.GetConnectionString();

        var first = Host(connectionString);
        using (var started = await first.CreateClient().GetAsync("/health"))
        {
            started.EnsureSuccessStatusCode();
        }

        var registered = await CountAsync(
            connectionString,
            "SELECT count(*) FROM lifecycle_version WHERE author = 'platform:template-seed'");

        await first.DisposeAsync();

        var second = Host(connectionString);
        using (var again = await second.CreateClient().GetAsync("/health"))
        {
            again.EnsureSuccessStatusCode();
        }

        Assert.Equal(
            registered,
            await CountAsync(
                connectionString,
                "SELECT count(*) FROM lifecycle_version WHERE author = 'platform:template-seed'"));
    }

    [Fact]
    public async Task FN_TPL_003_a_template_survives_the_process_that_created_it()
    {
        // The half that was missing. A template's lifecycle state was durable and the template
        // itself was not, so a restart left a registration pointing at a template that no longer
        // existed - and seeding, asking the empty store, made a new one (ADR-043).
        var connectionString = _container.GetConnectionString();

        var first = Host(connectionString);
        using (var started = await first.CreateClient().GetAsync("/health"))
        {
            started.EnsureSuccessStatusCode();
        }

        await first.DisposeAsync();

        var second = Host(connectionString);
        using var again = await second.CreateClient().GetAsync("/health");
        again.EnsureSuccessStatusCode();

        Assert.True(
            await CountAsync(connectionString, "SELECT count(*) FROM render_template") > 0,
            "the templates a deployment was seeded with should still be there after a restart");
    }

    [Fact]
    public async Task FN_TPL_005_a_seeded_template_nobody_registered_is_registered_on_the_next_start()
    {
        // Two stores and no transaction between them. Seeding writes a template and then
        // registers it, so a process that dies between the two leaves a template no lifecycle
        // record knows about - and the old rule, "register what I just created", would never
        // look at it again because the next start creates nothing.
        //
        // Reproduced by writing the template and no registration, which is exactly the state
        // that crash leaves behind. Start-up ensures rather than creates (ADR-043 decision 3).
        var connectionString = _container.GetConnectionString();
        await GovernanceSchema.ApplyAsync(connectionString);

        await using (var templates = new PostgresTemplateStore(connectionString))
        {
            await templates.CreateAsync(new RenderTemplateDefinition(
                "qrd-package-leaflet", "EU QRD package leaflet", "body { }"));
        }

        var host = Host(connectionString);
        using var started = await host.CreateClient().GetAsync("/health");
        started.EnsureSuccessStatusCode();

        Assert.Equal(
            1,
            await CountAsync(
                connectionString,
                """
                SELECT count(*) FROM lifecycle_version
                WHERE document_identifier = 'qrd-package-leaflet'
                """));
    }

    [Fact]
    public async Task FN_TPL_005_a_seeded_template_is_registered_in_the_durable_store()
    {
        // The write that found the ordering defect, asserted for what it is rather than only for
        // not throwing: a fresh deployment comes up with its standard templates under lifecycle
        // management, in a database that survives the container (ADR-042 decision 7).
        var connectionString = _container.GetConnectionString();
        var host = Host(connectionString);

        using var started = await host.CreateClient().GetAsync("/health");
        started.EnsureSuccessStatusCode();

        Assert.True(
            await CountAsync(
                connectionString,
                "SELECT count(*) FROM lifecycle_version WHERE author = 'platform:template-seed'")
            > 0,
            "the seeded templates should have been registered with the lifecycle engine");
    }

    [Fact]
    public async Task FN_LCM_008_a_seeded_template_registration_says_it_is_a_template()
    {
        // Otherwise it reads as a label whose content never arrived, and the reconciliation
        // report flags every one of them forever - a report that flags non-defects trains its
        // reader to ignore it.
        var connectionString = _container.GetConnectionString();
        var host = Host(connectionString);

        using var started = await host.CreateClient().GetAsync("/health");
        started.EnsureSuccessStatusCode();

        Assert.Equal(
            0,
            await CountAsync(
                connectionString,
                """
                SELECT count(*) FROM lifecycle_version
                WHERE author = 'platform:template-seed' AND artefact_kind <> 'template'
                """));
    }
}
