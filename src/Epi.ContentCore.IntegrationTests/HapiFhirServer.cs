using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Epi.ContentCore;
using Hl7.Fhir.Rest;
using Xunit;

namespace Epi.ContentCore.IntegrationTests;

/// <summary>
/// A real HAPI FHIR server in a container, shared by every test in the collection.
/// </summary>
/// <remarks>
/// The same image the development stack runs (deploy/docker-compose), pinned to R5 per
/// ADR-016. Started once per test run: HAPI takes tens of seconds to become ready, so a
/// container per test class would dominate the run time.
/// </remarks>
public sealed class HapiFhirServer : IAsyncLifetime
{
    // Pinned rather than :latest so a CI run is reproducible (D3 Section 10.3). The dev stack
    // uses the same image; keep the two in step.
    private const string Image = "hapiproject/hapi:v7.4.0";
    private const int HttpPort = 8080;

    private readonly IContainer _container = new ContainerBuilder(Image)
        .WithEnvironment("hapi.fhir.fhir_version", "R5")
        // An empty in-container H2 database: the store contract is what is under test, not
        // HAPI's persistence configuration.
        .WithPortBinding(HttpPort, assignRandomHostPort: true)
        .WithWaitStrategy(Wait.ForUnixContainer()
            .UntilHttpRequestIsSucceeded(request => request
                .ForPort(HttpPort)
                .ForPath("/fhir/metadata")
                .ForStatusCode(System.Net.HttpStatusCode.OK)))
        .Build();

    public string BaseUrl => $"http://{_container.Hostname}:{_container.GetMappedPublicPort(HttpPort)}/fhir";

    /// <summary>
    /// A client configured exactly as production configures it, so the tests exercise the
    /// real client behaviour rather than a more forgiving one.
    /// </summary>
    public FhirClient CreateClient() => FhirContentClient.Create(BaseUrl);

    public System.Threading.Tasks.Task InitializeAsync() => _container.StartAsync();

    public System.Threading.Tasks.Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

/// <summary>
/// One HAPI container for the whole run. Tests in this collection share it, so they must not
/// depend on the server being empty - every test works within its own minted identity.
/// </summary>
[CollectionDefinition(Name)]
public sealed class HapiCollection : ICollectionFixture<HapiFhirServer>
{
    public const string Name = "hapi";
}
