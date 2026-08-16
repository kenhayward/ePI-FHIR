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

// The reconciliation report, over HTTP (FN-LCM-008).
//   CAP-LCM-002 Version every label as immutable snapshots with a version lineage
//
// A report nobody can run is not a report. This is the surface, and the authorisation
// question it raises is the interesting part: an inert registration has no content, and
// scope is decided on the content (ADR-025), so there is nothing to scope this against.
// It is restricted by role instead.
public sealed class ReconciliationEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static string DocumentJson() => EpiBundleReader.Write(ContentScope.Stamp(
        EpiBundleReader.Read(File.ReadAllText(TestFixtures.Path("epi", "minimal-epi-document.json"))),
        new DocumentScope("uk-affiliate", "GB")));

    private WebApplicationFactory<Program> Host(IPolicyDecisionPoint? policy = null) =>
        factory.WithWebHostBuilder(host =>
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
                services.AddSingleton<IPolicyDecisionPoint>(policy ?? new AlwaysAllow());
                services.AddSingleton<ICredentialVerifier>(new KnownUsers());
            });
        });

    private static HttpClient As(WebApplicationFactory<Program> host, string user, string role)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(WhoeverAsked.UserHeader, user);
        client.DefaultRequestHeaders.Add(WhoeverAsked.RolesHeader, role);
        return client;
    }

    private sealed record ReconciliationView(
        DateTimeOffset RanAt,
        double SettleMinutes,
        IReadOnlyList<InertView> Inert);

    private sealed record InertView(
        string DocumentIdentifier,
        int Version,
        string Author,
        DateTimeOffset RegisteredAt,
        bool BlocksVersionNumber);

    [Fact]
    public async Task FN_LCM_008_a_platform_that_has_lost_nothing_reports_nothing()
    {
        var host = Host();

        var report = await As(host, "user-dev", "platform-operator")
            .GetFromJsonAsync<ReconciliationView>("/admin/reconciliation/registrations");

        Assert.NotNull(report);
        Assert.Empty(report!.Inert);
    }

    [Fact]
    public async Task FN_LCM_008_a_healthy_write_is_not_reported_as_inert()
    {
        // The case that would fail if the settle period were mistaken for a filter on age
        // alone: this registration is old enough to judge, and it has content.
        var host = Host();
        using var created = await As(host, "user-anna", "author").PostAsync("/fhir/Bundle",
            new StringContent(DocumentJson(), Encoding.UTF8, "application/fhir+json"));
        created.EnsureSuccessStatusCode();

        var report = await As(host, "user-dev", "platform-operator")
            .GetFromJsonAsync<ReconciliationView>(
                "/admin/reconciliation/registrations?settleMinutes=0.001");

        Assert.Empty(report!.Inert);
    }

    [Fact]
    public async Task FN_LCM_008_the_report_states_the_settle_period_it_used()
    {
        var host = Host();

        var report = await As(host, "user-dev", "platform-operator")
            .GetFromJsonAsync<ReconciliationView>(
                "/admin/reconciliation/registrations?settleMinutes=60");

        Assert.Equal(60, report!.SettleMinutes);
    }

    [Fact]
    public async Task FN_LCM_008_a_settle_period_of_zero_is_a_bad_request()
    {
        var host = Host();

        using var response = await As(host, "user-dev", "platform-operator")
            .GetAsync("/admin/reconciliation/registrations?settleMinutes=0");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FN_LCM_008_the_report_is_refused_without_a_token()
    {
        // The unmodified factory, so the real bearer scheme applies. Host() installs a test
        // scheme that authenticates every caller, which would make this case prove nothing.
        using var response = await TestFixtures.Configured(factory).CreateClient()
            .GetAsync("/admin/reconciliation/registrations");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FN_LCM_008_the_report_is_refused_to_a_subject_the_policy_denies()
    {
        // The whole control. There is no scope predicate standing behind this one, so if the
        // policy decision is not asked for - or its answer not acted on - the report is open
        // to everyone who can authenticate, and it names documents across every affiliate.
        var host = Host(new AlwaysDeny());

        using var response = await As(host, "user-cara", "reader")
            .GetAsync("/admin/reconciliation/registrations");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    /// <summary>Refuses every decision, so a missing check shows up as an allowed request.</summary>
    private sealed class AlwaysDeny : IPolicyDecisionPoint
    {
        public Task<AuthorizationDecision> DecideAsync(
            AuthorizationQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthorizationDecision(false, "this test denies everything."));
    }

    private sealed class AlwaysAllow : IPolicyDecisionPoint
    {
        public Task<AuthorizationDecision> DecideAsync(
            AuthorizationQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthorizationDecision(true, "stub"));
    }

    private sealed class KnownUsers : ICredentialVerifier
    {
        public Task<SignerIdentity?> VerifyAsync(
            string identifier, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult<SignerIdentity?>(null);
    }

    private sealed class WhoeverAsked(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "Test";

        public const string UserHeader = "X-Test-User";

        public const string RolesHeader = "X-Test-Roles";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = Request.Headers[UserHeader].FirstOrDefault() ?? "user-anna";
            var role = Request.Headers[RolesHeader].FirstOrDefault() ?? "author";

            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, user),
                    new Claim(SubjectFactory.RolesClaim, role),
                    new Claim(SubjectFactory.AffiliatesClaim, "uk-affiliate"),
                    new Claim(SubjectFactory.MarketsClaim, "GB"),
                ],
                Name);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Name)));
        }
    }
}
