using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Epi.ContentCore;
using Epi.Iam;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Epi.Api.Tests;

// Content under lifecycle management, over HTTP.
//   CAP-LCM-001 A configurable lifecycle state model with explicitly allowed transitions
//   CAP-LCM-003 Internal lifecycle state held separately from per-market approval state
//   CAP-IAM-007 Affiliate and market scope applied at data access
public sealed class LifecycleEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string TestScheme = "Test";

    private static string DocumentJson(string affiliate = "uk-affiliate", string market = "GB")
    {
        var bundle = EpiBundleReader.Read(
            File.ReadAllText(TestFixtures.Path("epi", "minimal-epi-document.json")));
        return EpiBundleReader.Write(ContentScope.Stamp(bundle, new DocumentScope(affiliate, market)));
    }

    private HttpClient Authenticated(bool allow = true) => factory.WithWebHostBuilder(host =>
    {
        host.UseSetting("Epi:MarketsPath", TestFixtures.RepositoryPath("config", "markets"));
        host.UseSetting("Epi:IdentifiersPath",
            TestFixtures.RepositoryPath("config", "identifiers.json"));
        host.UseSetting("Epi:Lifecycle:StatesPath",
            TestFixtures.RepositoryPath("config", "lifecycle", "label-states.json"));
        host.UseSetting("Epi:Lifecycle:MarketStatesPath",
            TestFixtures.RepositoryPath("config", "lifecycle", "market-approval-states.json"));
        host.UseSetting("Epi:MasterDataPath",
            TestFixtures.RepositoryPath("config", "master-data", "products.json"));
        host.ConfigureTestServices(services =>
        {
            services.AddAuthentication(TestScheme)
                .AddScheme<AuthenticationSchemeOptions, TestHandler>(TestScheme, _ => { });
            services.AddSingleton<IPolicyDecisionPoint>(new StubPolicy(allow));
        });
    }).CreateClient();

    /// <summary>Creates a document and returns its identifier.</summary>
    private static async Task<string> CreateAsync(HttpClient client)
    {
        using var response = await client.PostAsync("/fhir/Bundle",
            new StringContent(DocumentJson(), Encoding.UTF8, "application/fhir+json"));

        response.EnsureSuccessStatusCode();
        var created = await response.Content.ReadFromJsonAsync<CreatedDocument>();
        return created!.Identifier;
    }

    [Fact]
    public async Task CAP_LCM_001_content_is_under_lifecycle_management_the_moment_it_exists()
    {
        // Otherwise there is a window in which a stored version has no state, no author, and
        // therefore no segregation of duties - and nothing to say it was ever a draft.
        var client = Authenticated();
        var identifier = await CreateAsync(client);

        using var response = await client.GetAsync($"/labels/{identifier}/versions/1/state");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var state = await response.Content.ReadFromJsonAsync<VersionState>();
        Assert.Equal("draft", state!.State);
        Assert.Equal("user-anna", state.Author);
    }

    [Fact]
    public async Task CAP_LCM_003_state_reports_each_market_separately_from_the_internal_state()
    {
        // ADR-005 at the surface: the answer names the internal state and every market, so
        // "approved" can never be read as meaning approved everywhere.
        var client = Authenticated();
        var identifier = await CreateAsync(client);

        using var response = await client.GetAsync($"/labels/{identifier}/versions/1/state");
        var state = await response.Content.ReadFromJsonAsync<VersionState>();

        Assert.Equal("draft", state!.State);
        Assert.Equal("not-submitted", state.Markets["GB"]);
        Assert.Equal("not-submitted", state.Markets["EU"]);
    }

    [Fact]
    public async Task CAP_LCM_001_the_state_of_a_version_that_does_not_exist_is_not_found()
    {
        using var response = await Authenticated()
            .GetAsync($"/labels/{Guid.NewGuid()}/versions/1/state");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CAP_IAM_007_state_the_caller_may_not_see_is_indistinguishable_from_absent()
    {
        // The same answer the content endpoint gives, and for the same reason: a caller must
        // not be able to learn that a document they may not see exists (CAP-SCH-004).
        var identifier = await CreateAsync(Authenticated());

        using var response = await Authenticated(allow: false)
            .GetAsync($"/labels/{identifier}/versions/1/state");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CAP_LCM_001_state_is_refused_without_a_token()
    {
        using var response = await TestFixtures.Configured(factory).CreateClient()
            .GetAsync($"/labels/{Guid.NewGuid()}/versions/1/state");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record CreatedDocument(string Identifier, string System, int Version);

    private sealed record VersionState(
        string Identifier,
        int Version,
        string State,
        string Author,
        IReadOnlyDictionary<string, string> Markets);

    [Fact]
    public async Task FN_LCM_009_each_market_says_what_may_be_done_to_it_from_here()
    {
        // Per-market approval is held separately from internal lifecycle on purpose (ADR-005),
        // and the surface must not work out either. Deriving them in a browser would be two
        // more implementations of two state models.
        var client = Authenticated();
        var id = await CreateAsync(client);

        var state = await client.GetFromJsonAsync<StateWithMarkets>(
            $"/labels/{id}/versions/1/state");

        Assert.Equal("not-submitted", state!.Markets["GB"]);
        Assert.Contains("submit", state.MarketActions["GB"].Actions);
    }

    [Fact]
    public async Task FN_LCM_009_submitting_to_a_regulator_is_signed_and_recording_its_answer_is_not()
    {
        // CAP-LCM-012, and the distinction the whole market model exists around: submitting is
        // an act of this organisation by an accountable person; recording what a regulator
        // decided is a factual entry about somebody else's decision.
        var client = Authenticated();
        var id = await CreateAsync(client);

        var state = await client.GetFromJsonAsync<StateWithMarkets>(
            $"/labels/{id}/versions/1/state");

        Assert.Contains("submit", state!.MarketActions["GB"].SignedActions);
        Assert.DoesNotContain("record-approval", state.MarketActions["GB"].SignedActions);
    }

    [Fact]
    public async Task FN_LCM_009_the_action_that_records_an_approval_says_it_needs_an_effective_date()
    {
        // ADR-029 decision 3: required on the transition that records an approval, refused on
        // any other, never defaulted. A surface that asked for one everywhere would be
        // collecting a date the platform refuses.
        var client = Authenticated();
        var id = await CreateAsync(client);

        var state = await client.GetFromJsonAsync<StateWithMarkets>(
            $"/labels/{id}/versions/1/state");

        Assert.All(state!.MarketActions.Values, market =>
            Assert.DoesNotContain("submit", market.ActionsNeedingEffectiveDate));
    }

    private sealed record StateWithMarkets(
        string Identifier, int Version, string State,
        IReadOnlyDictionary<string, string> Markets,
        IReadOnlyDictionary<string, MarketView> MarketActions);

    private sealed record MarketView(
        IReadOnlyList<string> Actions,
        IReadOnlyList<string> SignedActions,
        IReadOnlyList<string> ActionsNeedingEffectiveDate);

    private sealed class StubPolicy(bool allow) : IPolicyDecisionPoint
    {
        public Task<AuthorizationDecision> DecideAsync(
            AuthorizationQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(allow
                ? new AuthorizationDecision(true, "stub")
                : AuthorizationDecision.Deny("stub"));
    }

    /// <summary>Signs every caller in as the same person, so tests are about the endpoint.</summary>
    private sealed class TestHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-anna"),
                    new Claim("affiliate", "uk-affiliate"),
                    new Claim("market", "GB"),
                ],
                TestScheme);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), TestScheme)));
        }
    }
}
