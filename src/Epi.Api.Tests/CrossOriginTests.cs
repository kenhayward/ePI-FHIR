using System.Net;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Epi.Api.Tests;

// Letting a browser reach the platform (FN-CFG-005).
//   CAP-CFG-006 Resolve every configuration a component needs at start-up
//   CAP-IAM-001 Authenticate via the enterprise identity provider
//
// A defect found by opening the authoring surface: it signed in, and then every request to the
// platform failed with "Failed to fetch". The surface is served from one origin and the API
// answers on another, and the API sent no cross-origin headers at all - so a browser refused
// every call before it left the page (ADR-050).
//
// Nothing caught it because every test here talks to the API through a test host rather than
// through a browser, and a browser is the only thing that enforces this.
public sealed class CrossOriginTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string Surface = "http://localhost:5173";

    private static WebApplicationFactory<Program> Host(
        WebApplicationFactory<Program> factory, string? origins) =>
        TestFixtures.Configured(factory, host =>
        {
            if (origins is not null)
            {
                host.UseSetting("Epi:Cors:Origins", origins);
            }
        });

    private static async Task<HttpResponseMessage> AskFromAsync(
        WebApplicationFactory<Program> host, string origin, string path = "/health")
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("Origin", origin);

        return await host.CreateClient().SendAsync(request);
    }

    [Fact]
    public async Task FN_CFG_005_a_configured_origin_is_allowed_to_read_the_answer()
    {
        // The header is what a browser looks for. Without it the response arrives and is thrown
        // away unread, which is what "Failed to fetch" means.
        using var response = await AskFromAsync(Host(factory, Surface), Surface);

        Assert.Equal(
            Surface,
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task FN_CFG_005_an_origin_nobody_configured_is_not_allowed()
    {
        // Named origins rather than any origin. A regulated platform that answered every page on
        // the internet would be one whose access control depended entirely on a token nobody has
        // yet stolen.
        using var response = await AskFromAsync(Host(factory, Surface), "http://evil.example.org");

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task FN_CFG_005_a_preflight_from_a_configured_origin_is_answered()
    {
        // The request a browser makes before anything with an Authorization header. Answering
        // 405 to it, as this did, means every authenticated call fails before it is sent.
        using var request = new HttpRequestMessage(HttpMethod.Options, "/labels/search");
        request.Headers.Add("Origin", Surface);
        request.Headers.Add("Access-Control-Request-Method", "GET");
        request.Headers.Add("Access-Control-Request-Headers", "authorization");

        using var response = await Host(factory, Surface).CreateClient().SendAsync(request);

        Assert.NotEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
        Assert.Equal(
            Surface,
            response.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task FN_CFG_005_more_than_one_origin_can_be_configured()
    {
        // A deployment serving the surface and something else - a documentation site, a second
        // surface - configures both rather than opening up to everything.
        var host = Host(factory, $"{Surface},https://epi.example.org");

        using var second = await AskFromAsync(host, "https://epi.example.org");

        Assert.Equal(
            "https://epi.example.org",
            second.Headers.GetValues("Access-Control-Allow-Origin").Single());
    }

    [Fact]
    public async Task FN_CFG_005_a_deployment_that_configures_none_allows_none()
    {
        // Rather than defaulting to something. An API that allowed localhost by default would be
        // a production deployment answering a page served from a developer's machine.
        using var response = await AskFromAsync(Host(factory, null), Surface);

        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task FN_CFG_005_any_origin_is_refused_as_configuration()
    {
        // '*' is the shape somebody reaches for when a browser refuses them, and it is the one
        // thing this must not permit: it would make the platform readable by any page a signed-in
        // author happened to visit. Refused at start-up, where it is somebody's decision to fix,
        // rather than accepted quietly.
        var host = Host(factory, "*");

        var refused = await Assert.ThrowsAnyAsync<Exception>(
            async () => await AskFromAsync(host, Surface));

        Assert.Contains("*", refused.Message + refused.InnerException?.Message, StringComparison.Ordinal);
    }
}
