using System.Net.Http.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Epi.Iam.IntegrationTests;

/// <summary>
/// A real Open Policy Agent server, loaded with the policy this repository actually ships.
/// </summary>
/// <remarks>
/// The policy is uploaded through OPA's API rather than mounted, so the test exercises the
/// same file that <c>opa test</c> checks in CI: if policies/authz/example.rego changes, this
/// changes with it, and the platform's input contract is checked against the real rules.
/// </remarks>
public sealed class OpaServer : IAsyncLifetime
{
    private const string Image = "openpolicyagent/opa:1.19.0";
    private const int HttpPort = 8181;

    private readonly IContainer _container = new ContainerBuilder(Image)
        .WithCommand("run", "--server", "--addr", ":8181")
        .WithPortBinding(HttpPort, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(request => request.ForPort(HttpPort).ForPath("/health")))
        .Build();

    public HttpClient CreateClient() => new()
    {
        BaseAddress = new Uri($"http://{_container.Hostname}:{_container.GetMappedPublicPort(HttpPort)}/"),
    };

    public async System.Threading.Tasks.Task InitializeAsync()
    {
        await _container.StartAsync();

        using var client = CreateClient();

        var policy = await File.ReadAllTextAsync(RepositoryFile("policies", "authz", "example.rego"));
        var upload = await client.PutAsync("v1/policies/epi.authz", new StringContent(policy));
        upload.EnsureSuccessStatusCode();

        // The policy resolves roles from data.roles, which no file in the repository defines
        // yet: role definitions are configuration and will arrive with capability 21's role
        // administration. The fixture supplies them so the rules can be exercised end to end.
        var roles = await client.PutAsJsonAsync("v1/data/roles", new Dictionary<string, object>
        {
            ["affiliate_author"] = new { actions = new[] { "read", "author" } },
            ["affiliate_approver"] = new { actions = new[] { "read", "approve" } },
            ["reader"] = new { actions = new[] { "read" } },
        });
        roles.EnsureSuccessStatusCode();
    }

    public System.Threading.Tasks.Task DisposeAsync() => _container.DisposeAsync().AsTask();

    private static string RepositoryFile(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EpiPlatform.sln")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new InvalidOperationException("Could not locate the repository root.")
            : Path.Combine([directory.FullName, .. segments]);
    }
}

[CollectionDefinition(Name)]
public sealed class OpaCollection : ICollectionFixture<OpaServer>
{
    public const string Name = "opa";
}
