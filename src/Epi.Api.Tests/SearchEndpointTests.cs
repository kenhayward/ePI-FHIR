using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using Epi.ContentCore;
using Epi.Iam;
using Epi.Signature;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Epi.Api.Tests;

// Search over HTTP, scoped to the caller.
//   IT-016 Search returns only what the caller may see, and can return the current-approved
//          version per market
public sealed class SearchEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string RaesPassword = "horse-battery-staple-correct";

    private static string DocumentJson(string affiliate, string market) => EpiBundleReader.Write(
        ContentScope.Stamp(
            EpiBundleReader.Read(File.ReadAllText(TestFixtures.Path("epi", "minimal-epi-document.json"))),
            new DocumentScope(affiliate, market)));

    /// <summary>One host, so every caller in a test shares the same in-memory stores.</summary>
    private WebApplicationFactory<Program> Host() => factory.WithWebHostBuilder(host =>
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
            services.AddAuthentication(WhoeverAsked.Name)
                .AddScheme<AuthenticationSchemeOptions, WhoeverAsked>(WhoeverAsked.Name, _ => { });

            // Mirrors scope_covers_resource in policies/authz, which is itself tested against a
            // real OPA in Epi.Iam.IntegrationTests. What is under test here is what the API
            // does with a decision, on the path that has no single document to decide about.
            services.AddSingleton<IPolicyDecisionPoint>(new RolesAndScope());
            services.AddSingleton<ICredentialVerifier>(new KnownUsers());
        });
    });

    /// <summary>A caller whose token asserts the markets the header names.</summary>
    private static HttpClient As(WebApplicationFactory<Program> host, string user, string markets)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(WhoeverAsked.UserHeader, user);
        client.DefaultRequestHeaders.Add(WhoeverAsked.MarketsHeader, markets);
        return client;
    }

    private static async Task<string> CreateAsync(HttpClient client, string market)
    {
        using var response = await client.PostAsync("/fhir/Bundle",
            new StringContent(DocumentJson("uk-affiliate", market), Encoding.UTF8, "application/fhir+json"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedDocument>())!.Identifier;
    }

    private static async Task<Page> SearchAsync(HttpClient client, string query = "")
    {
        using var response = await client.GetAsync($"/labels/search{query}");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<Page>())!;
    }

    [Fact]
    public async Task IT_016_a_search_returns_the_content_the_caller_may_see()
    {
        var host = Host();
        var identifier = await CreateAsync(As(host, "user-anna", "GB"), "GB");

        var page = await SearchAsync(As(host, "user-anna", "GB"));

        var hit = Assert.Single(page.Hits);
        Assert.Equal(identifier, hit.DocumentIdentifier);
        Assert.Equal("draft", hit.State);
        Assert.Equal("GB", hit.Market);
        Assert.Equal(1, page.Total);
    }

    [Fact]
    public async Task IT_016_content_in_a_market_the_caller_does_not_hold_is_invisible()
    {
        // Not refused - invisible, and absent from the total. A caller must not be able to
        // learn from a count that there is something they may not see (CAP-SCH-004).
        var host = Host();
        var wide = As(host, "user-anna", "GB,EU");
        await CreateAsync(wide, "GB");
        await CreateAsync(wide, "EU");

        Assert.Equal(2, (await SearchAsync(wide)).Total);

        var narrow = await SearchAsync(As(host, "user-anna", "GB"));

        Assert.Equal(1, narrow.Total);
        Assert.Equal("GB", Assert.Single(narrow.Hits).Market);
    }

    [Fact]
    public async Task CAP_SCH_004_a_caller_the_policy_permits_nothing_sees_nothing()
    {
        var host = Host();
        await CreateAsync(As(host, "user-anna", "GB"), "GB");

        var page = await SearchAsync(As(host, "user-anna", "ZZ"));

        Assert.Empty(page.Hits);
        Assert.Equal(0, page.Total);
    }

    [Fact]
    public async Task CAP_SCH_001_results_can_be_narrowed_by_state_and_identifier()
    {
        var host = Host();
        var anna = As(host, "user-anna", "GB");
        var identifier = await CreateAsync(anna, "GB");
        await CreateAsync(anna, "GB");

        using var submitted = await anna.PostAsJsonAsync(
            $"/labels/{identifier}/versions/1/transitions", new { action = "submit", reason = "ready" });
        submitted.EnsureSuccessStatusCode();

        var inReview = await SearchAsync(anna, "?state=in-review");

        Assert.Equal(identifier, Assert.Single(inReview.Hits).DocumentIdentifier);
        Assert.Equal(1, (await SearchAsync(anna, $"?identifier={identifier}")).Total);
    }

    [Fact]
    public async Task CAP_SCH_006_a_page_is_bounded_and_the_total_is_the_scoped_total()
    {
        var host = Host();
        var anna = As(host, "user-anna", "GB");
        for (var i = 0; i < 3; i++)
        {
            await CreateAsync(anna, "GB");
        }

        var page = await SearchAsync(anna, "?pageSize=2");

        Assert.Equal(2, page.Hits.Count);
        Assert.Equal(3, page.Total);
        Assert.Equal(2, page.PageSize);
    }

    [Fact]
    public async Task IT_016_the_current_approved_version_is_the_one_the_market_approved()
    {
        var host = Host();
        var rae = As(host, "user-rae", "GB,EU");
        var identifier = await CreateAsync(As(host, "user-anna", "GB"), "GB");

        // Nothing is approved until a market says so, whatever the internal state.
        using var none = await rae.GetAsync($"/labels/{identifier}/current-approved?market=GB");
        Assert.Equal(HttpStatusCode.NotFound, none.StatusCode);

        await ApproveInMarketAsync(rae, identifier, "GB");

        using var found = await rae.GetAsync($"/labels/{identifier}/current-approved?market=GB");
        Assert.Equal(HttpStatusCode.OK, found.StatusCode);
        var hit = await found.Content.ReadFromJsonAsync<Hit>();
        Assert.Equal(identifier, hit!.DocumentIdentifier);
        Assert.Equal(1, hit.Version);

        // ADR-005 at the surface: the same content, and the other market has approved nothing.
        using var elsewhere = await rae.GetAsync($"/labels/{identifier}/current-approved?market=EU");
        Assert.Equal(HttpStatusCode.NotFound, elsewhere.StatusCode);
    }

    [Fact]
    public async Task IT_016_a_current_approved_version_outside_the_callers_scope_is_not_found()
    {
        var host = Host();
        var rae = As(host, "user-rae", "GB,EU");
        var identifier = await CreateAsync(As(host, "user-anna", "GB,EU"), "EU");
        await ApproveInMarketAsync(rae, identifier, "EU");

        using var response = await As(host, "user-anna", "GB")
            .GetAsync($"/labels/{identifier}/current-approved?market=EU");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CAP_IAM_002_seeing_a_label_is_not_permission_to_deal_with_a_regulator()
    {
        // The author may read the label and may not submit it to a regulator. Without this the
        // market endpoint would be gated only by whether the caller can see the content, which
        // is the weakest gate in the platform and the wrong one for an act of the organisation.
        var host = Host();
        var identifier = await CreateAsync(As(host, "user-anna", "GB"), "GB");

        using var response = await As(host, "user-anna", "GB").PostAsJsonAsync(
            $"/labels/{identifier}/versions/1/markets/GB/transitions",
            new { action = "submit", reason = "not mine to make" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CAP_IAM_002_recording_a_regulators_decision_needs_its_own_permission()
    {
        // The unsigned half of CAP-LCM-012 is still a permission: transcribing what a regulator
        // decided is not something everyone who may read a label may do.
        var host = Host();
        var identifier = await CreateAsync(As(host, "user-anna", "GB"), "GB");

        using var response = await As(host, "user-anna", "GB").PostAsJsonAsync(
            $"/labels/{identifier}/versions/1/markets/GB/transitions",
            new { action = "begin-assessment", reason = "not mine to record" });

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CAP_SCH_004_search_is_refused_without_a_token()
    {
        using var response = await TestFixtures.Configured(factory).CreateClient().GetAsync("/labels/search");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CAP_SCH_002_current_approved_is_refused_without_a_token()
    {
        using var response = await TestFixtures.Configured(factory).CreateClient()
            .GetAsync($"/labels/{Guid.NewGuid()}/current-approved?market=GB");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>
    /// Drives a version to approved in one market over HTTP: submit under signature, begin
    /// assessment, record the regulator's decision (CAP-LCM-012).
    /// </summary>
    private static async Task ApproveInMarketAsync(HttpClient client, string identifier, string market)
    {
        using var signed = await client.PostAsJsonAsync("/signatures", new
        {
            documentIdentifier = identifier,
            version = 1,
            meaning = "Responsibility",
            password = RaesPassword,
            reason = "submitting to the regulator",
        });
        signed.EnsureSuccessStatusCode();
        var signature = (await signed.Content.ReadFromJsonAsync<SignatureReceipt>())!.Reference;

        var path = $"/labels/{identifier}/versions/1/markets/{market}/transitions";
        foreach (var (action, reference) in new[]
                 {
                     ("submit", signature),
                     ("begin-assessment", null),
                 })
        {
            using var response = await client.PostAsJsonAsync(
                path, new { action, reason = "test", signatureReference = reference });
            response.EnsureSuccessStatusCode();
        }

        // Recording an approval must state when it takes effect; "immediately" is stated by
        // giving a moment, never by leaving it out (ADR-029 decision 3).
        using var approved = await client.PostAsJsonAsync(path, new
        {
            action = "record-approval",
            reason = "test",
            effectiveFrom = DateTimeOffset.UtcNow.AddYears(1),
        });
        approved.EnsureSuccessStatusCode();
    }

    private sealed record CreatedDocument(string Identifier, string System, int Version);

    private sealed record SignatureReceipt(string Reference);

    private sealed record Hit(
        string DocumentIdentifier, int Version, string Title, string Affiliate, string Market,
        string State, string? Language, string? Product, string? DocumentType);

    private sealed record Page(IReadOnlyList<Hit> Hits, int Total, int PageSize);

    private sealed class KnownUsers : ICredentialVerifier
    {
        public Task<SignerIdentity?> VerifyAsync(
            string identifier, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult((identifier, password) switch
            {
                ("user-rae", RaesPassword) => new SignerIdentity("user-rae", "Rae Lindqvist"),
                _ => null,
            });
    }

    /// <summary>
    /// The shipped policy as a stand-in: the role must grant the action and the caller's scope
    /// must cover the resource. Kept in step with policies/authz and policies/data by hand,
    /// which is why the real thing is exercised against a real OPA in Epi.Iam.IntegrationTests
    /// and end to end by tools/walkthrough.py.
    /// </summary>
    private sealed class RolesAndScope : IPolicyDecisionPoint
    {
        private static readonly Dictionary<string, string[]> Actions = new(StringComparer.Ordinal)
        {
            ["author"] = ["read", "author"],
            ["approver"] = ["read", "approve"],
            ["regulatory"] = ["read", "submit-to-regulator", "record-decision"],
            ["reader"] = ["read"],
        };

        public Task<AuthorizationDecision> DecideAsync(
            AuthorizationQuery query, CancellationToken cancellationToken = default)
        {
            var granted = query.Subject.Roles.Any(
                role => Actions.TryGetValue(role, out var actions) && actions.Contains(query.Action));

            var inScope = query.Subject.Affiliates.Contains(query.Resource.Affiliate)
                          && query.Subject.Markets.Contains(query.Resource.Market);

            return Task.FromResult(granted && inScope
                ? new AuthorizationDecision(true, "stub")
                : AuthorizationDecision.Deny(granted ? "out of scope" : "no role grants that action"));
        }
    }

    /// <summary>
    /// Signs the caller in as whoever the headers name, with whatever markets they name, so one
    /// host can serve callers of different reach - which is the only way to test that a search
    /// is scoped rather than merely filtered.
    /// </summary>
    private sealed class WhoeverAsked(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "Test";

        public const string UserHeader = "X-Test-User";

        public const string MarketsHeader = "X-Test-Markets";

        /// <summary>The roles the demonstration realm gives each user.</summary>
        private static string RoleOf(string user) => user switch
        {
            "user-ben" => "approver",
            "user-rae" => "regulatory",
            _ => "author",
        };

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = Request.Headers[UserHeader].FirstOrDefault() ?? "user-anna";
            var markets = (Request.Headers[MarketsHeader].FirstOrDefault() ?? "GB").Split(',');

            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, user),
                    new Claim(SubjectFactory.RolesClaim, RoleOf(user)),
                    new Claim(SubjectFactory.AffiliatesClaim, "uk-affiliate"),
                    .. markets.Select(market => new Claim(SubjectFactory.MarketsClaim, market.Trim())),
                ],
                Name);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Name)));
        }
    }
}
