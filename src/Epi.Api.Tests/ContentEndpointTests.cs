using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Epi.ContentCore;
using Epi.Iam;
using Hl7.Fhir.Model;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Xunit;

namespace Epi.Api.Tests;

// The walking skeleton end to end over HTTP: authenticate, authorise, validate, store.
//   IT-007 A request without a valid token is rejected before reaching content
public sealed class ContentEndpointTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public ContentEndpointTests(WebApplicationFactory<Program> factory) => _factory = factory;

    private const string TestScheme = "Test";

    private static string DocumentJson(string affiliate = "uk-affiliate", string market = "GB")
    {
        var bundle = EpiBundleReader.Read(File.ReadAllText(TestFixtures.Path("epi", "minimal-epi-document.json")));
        return EpiBundleReader.Write(ContentScope.Stamp(bundle, new DocumentScope(affiliate, market)));
    }

    /// <summary>The host as deployed: real authentication, so an anonymous call is refused.</summary>
    private HttpClient Anonymous() => Configured(services => { }).CreateClient();

    /// <summary>The host with a stand-in authentication scheme and a stubbed policy.</summary>
    private HttpClient Authenticated(bool allow = true) => Configured(services =>
    {
        services.AddAuthentication(TestScheme).AddScheme<AuthenticationSchemeOptions, TestHandler>(TestScheme, _ => { });
        services.AddSingleton<IPolicyDecisionPoint>(new StubPolicy(allow));
    }).CreateClient();

    private WebApplicationFactory<Program> Configured(Action<IServiceCollection> configure) =>
        _factory.WithWebHostBuilder(host =>
        {
            host.UseSetting("Epi:MarketsPath", TestFixtures.RepositoryPath("config", "markets"));
            host.ConfigureTestServices(configure);
        });

    [Fact]
    public async Task IT_007_a_request_without_a_token_is_refused()
    {
        using var response = await Anonymous().PostAsync("/fhir/Bundle",
            new StringContent(DocumentJson(), Encoding.UTF8, "application/fhir+json"));

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IT_007_reading_without_a_token_is_refused_too()
    {
        using var response = await Anonymous().GetAsync("/fhir/Bundle/anything");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task IT_007_the_health_probe_stays_open()
    {
        // Liveness must not depend on the identity provider, or a token outage looks like a
        // dead service to the container platform.
        using var response = await Anonymous().GetAsync("/health");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task A_scoped_author_can_store_and_read_back_a_document()
    {
        var client = Authenticated();

        using var created = await client.PostAsync("/fhir/Bundle",
            new StringContent(DocumentJson(), Encoding.UTF8, "application/fhir+json"));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var body = await created.Content.ReadFromJsonAsync<CreatedDocument>();
        Assert.NotNull(body);

        using var fetched = await client.GetAsync($"/fhir/Bundle/{body!.Identifier}");
        Assert.Equal(HttpStatusCode.OK, fetched.StatusCode);
        Assert.Contains("Examplinum", await fetched.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task Content_the_policy_denies_is_refused_and_not_stored()
    {
        var client = Authenticated(allow: false);

        using var response = await client.PostAsync("/fhir/Bundle",
            new StringContent(DocumentJson(), Encoding.UTF8, "application/fhir+json"));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Malformed_content_is_rejected_with_itemised_problems()
    {
        var client = Authenticated();

        using var response = await client.PostAsync("/fhir/Bundle",
            new StringContent("""{"resourceType": "Bundle", "type": "collection"}""",
                Encoding.UTF8, "application/fhir+json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("document", await response.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
    }

    private sealed record CreatedDocument(string Identifier, string System, int Version);

    private sealed class StubPolicy(bool allow) : IPolicyDecisionPoint
    {
        public Task<AuthorizationDecision> DecideAsync(
            AuthorizationQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(allow
                ? new AuthorizationDecision(true, "stub")
                : AuthorizationDecision.Deny("stub"));
    }

    /// <summary>Stands in for the identity provider, issuing the claims a real token would.</summary>
    private sealed class TestHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, "user-anna"),
                new Claim(SubjectFactory.RolesClaim, "affiliate_author"),
                new Claim(SubjectFactory.AffiliatesClaim, "uk-affiliate"),
                new Claim(SubjectFactory.MarketsClaim, "GB"),
            ], TestScheme);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme)));
        }
    }
}
