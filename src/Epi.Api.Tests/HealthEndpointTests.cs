using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Epi.Api.Tests;

// The health endpoint is the first thread through the host: it proves the service builds,
// starts, routes, and answers. It is scaffolding rather than capability behaviour, so it
// carries no CAP or FN identifier and does not appear in the traceability matrices.
public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    [Fact]
    public async Task Health_endpoint_reports_the_service_as_healthy()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(body);
        Assert.Equal("healthy", body!.Status);
        Assert.Equal("epi-api", body.Service);
    }

    private sealed record HealthResponse(string Status, string Service);
}
