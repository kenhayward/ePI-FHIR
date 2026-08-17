using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;

namespace Epi.Api.Tests;

/// <summary>
/// Locates the shared fixtures under tests/ - the repository's home for sample ePI bundles
/// and conformance fixtures, rather than copies in each project's build output.
/// </summary>
internal static class TestFixtures
{
    public static string Path(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(System.IO.Path.Combine(directory.FullName, "EpiPlatform.sln")))
        {
            directory = directory.Parent;
        }

        if (directory is null)
        {
            throw new InvalidOperationException(
                "Could not locate the repository root (no EpiPlatform.sln found above the test output).");
        }

        return System.IO.Path.Combine([directory.FullName, "tests", "fixtures", .. segments]);
    }

    /// <summary>A path inside the repository, for configuration the host reads at start-up.</summary>
    public static string RepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(System.IO.Path.Combine(directory.FullName, "EpiPlatform.sln")))
        {
            directory = directory.Parent;
        }

        return System.IO.Path.Combine([directory!.FullName, .. segments]);
    }

    /// <summary>
    /// A host pointed at the repository's own configuration, and nothing else.
    /// </summary>
    /// <remarks>
    /// The platform resolves every configuration path at start-up and refuses to run without
    /// them, so a bare factory no longer starts: its content root is the API project, which
    /// holds the application and not the configuration. That is the intended behaviour - three
    /// defects came from a container that started with none - and it means a test host has to
    /// say where the configuration is, exactly as a deployment does.
    /// <para>
    /// Here rather than repeated in each test class, because five of them need the same four
    /// settings and a sixth added later would otherwise find out by failing.
    /// </para>
    /// </remarks>
    public static WebApplicationFactory<Program> Configured(
        WebApplicationFactory<Program> factory,
        Action<IWebHostBuilder>? and = null) =>
        factory.WithWebHostBuilder(host =>
        {
            host.UseSetting("Epi:MarketsPath", RepositoryPath("config", "markets"));
            host.UseSetting("Epi:IdentifiersPath", RepositoryPath("config", "identifiers.json"));
            host.UseSetting("Epi:Lifecycle:StatesPath",
                RepositoryPath("config", "lifecycle", "label-states.json"));
            host.UseSetting("Epi:Lifecycle:MarketStatesPath",
                RepositoryPath("config", "lifecycle", "market-approval-states.json"));
            host.UseSetting("Epi:MasterDataPath",
                RepositoryPath("config", "master-data", "products.json"));
            host.UseSetting("Epi:TemplateSeedPath",
                RepositoryPath("config", "templates", "seed"));
            host.UseSetting("Epi:Lifecycle:TemplateStatesPath",
                RepositoryPath("config", "lifecycle", "template-states.json"));
            and?.Invoke(host);
        });
}
