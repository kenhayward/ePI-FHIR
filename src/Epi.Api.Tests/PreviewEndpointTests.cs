using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Epi.ContentCore;
using Epi.Iam;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Epi.Api.Tests;

// Seeing the leaflet a version produces.
//   CAP-RND-001 Render FHIR ePI to accessible HTML
//   CAP-RND-004 A draft render is distinguishable from an official one
//
// Rendering has existed since iteration 3 - HTML, PDF, a print engine, an asset store - and the
// API had no reference to any of it. None of it was reachable.
//
// What can honestly be offered is a preview and not an official render, and that follows from
// ADR-033 decision 2 rather than from a limitation chosen here: a render template is content
// that somebody approves, there is no template store yet, and a render made with a template
// nobody approved cannot be the artefact filed with a regulator.
public sealed class PreviewEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static string DocumentJson() => EpiBundleReader.Write(ContentScope.Stamp(
        EpiBundleReader.Read(File.ReadAllText(TestFixtures.Path("epi", "minimal-epi-document.json"))),
        new DocumentScope("uk-affiliate", "GB")));

    private WebApplicationFactory<Program> Host() =>
        TestFixtures.Configured(factory, host => host.ConfigureTestServices(services =>
        {
            services.AddAuthentication(WhoeverAsked.Name)
                .AddScheme<AuthenticationSchemeOptions, WhoeverAsked>(WhoeverAsked.Name, _ => { });
            services.AddSingleton<IPolicyDecisionPoint>(new AlwaysAllow());
        }));

    private static HttpClient As(WebApplicationFactory<Program> host)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(WhoeverAsked.RolesHeader, "author");
        return client;
    }

    private sealed record Created(string Identifier);

    private static async Task<string> AuthoredAsync(WebApplicationFactory<Program> host)
    {
        using var created = await As(host).PostAsync("/fhir/Bundle",
            new StringContent(DocumentJson(), Encoding.UTF8, "application/fhir+json"));
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<Created>())!.Identifier;
    }

    [Fact]
    public async Task CAP_RND_001_a_version_previews_as_the_leaflet_it_produces()
    {
        var host = Host();
        var id = await AuthoredAsync(host);

        using var response = await As(host).GetAsync($"/labels/{id}/versions/1/preview");
        var html = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Examplinum", html, StringComparison.Ordinal);
        Assert.StartsWith("text/html", response.Content.Headers.ContentType!.MediaType!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CAP_RND_004_a_preview_says_it_is_a_preview()
    {
        // CAP-RND-004. An author preview indistinguishable from an official render is a document
        // that will eventually be sent to somebody, and no render made with an unapproved
        // template can be the artefact filed with a regulator (ADR-033 decision 2).
        var host = Host();
        var id = await AuthoredAsync(host);

        var html = await As(host).GetStringAsync($"/labels/{id}/versions/1/preview");

        Assert.Contains("PREVIEW", html, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CAP_RND_007_two_previews_of_one_version_are_the_same_bytes()
    {
        // Determinism reaches the surface rather than stopping at the renderer (ADR-033
        // decision 1). A preview that differed between refreshes would make an author doubt
        // what they were looking at.
        var host = Host();
        var id = await AuthoredAsync(host);
        var client = As(host);

        var first = await client.GetStringAsync($"/labels/{id}/versions/1/preview");
        await Task.Delay(TimeSpan.FromSeconds(1));
        var second = await client.GetStringAsync($"/labels/{id}/versions/1/preview");

        Assert.Equal(first, second);
    }

    [Fact]
    public async Task CAP_RND_001_a_version_nobody_wrote_has_no_preview()
    {
        using var response = await As(Host())
            .GetAsync("/labels/01a00000-0000-7000-8000-0000000000ff/versions/1/preview");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CAP_RND_001_a_preview_is_refused_without_a_token()
    {
        using var response = await TestFixtures.Configured(factory).CreateClient()
            .GetAsync("/labels/01a00000-0000-7000-8000-00000000000a/versions/1/preview");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class AlwaysAllow : IPolicyDecisionPoint
    {
        public Task<AuthorizationDecision> DecideAsync(
            AuthorizationQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthorizationDecision(true, "stub"));
    }

    private sealed class WhoeverAsked(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "Test";

        public const string RolesHeader = "X-Test-Roles";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-anna"),
                    new Claim(SubjectFactory.RolesClaim,
                        Request.Headers[RolesHeader].FirstOrDefault() ?? "author"),
                    new Claim(SubjectFactory.AffiliatesClaim, "uk-affiliate"),
                    new Claim(SubjectFactory.MarketsClaim, "GB"),
                ],
                Name);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Name)));
        }
    }
}
