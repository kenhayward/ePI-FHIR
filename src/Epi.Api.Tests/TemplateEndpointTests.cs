using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
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

// Templates, under the lifecycle a label already has (FN-TPL-005).
//   CAP-TPL-008 Template lifecycle with approval via workflow and access via IAM
//   CAP-TPL-001 A versioned library of templates
//
// ADR-042 decision 3: the same engine, the same segregation of duties, the same signature gate -
// because a template determines what a patient reads, and approving one is a regulatory act with
// a named accountable person behind it. Not a second approval mechanism, which would be a second
// set of rules to keep in step with the first.
public sealed class TemplateEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private WebApplicationFactory<Program> Host() =>
        TestFixtures.Configured(factory, host =>
        {
            host.ConfigureTestServices(services =>
            {
                services.AddAuthentication(WhoeverAsked.Name)
                    .AddScheme<AuthenticationSchemeOptions, WhoeverAsked>(WhoeverAsked.Name, _ => { });
                services.AddSingleton<IPolicyDecisionPoint>(new AlwaysAllow());
                services.AddSingleton<ICredentialVerifier>(new NoSigning());
            });
        });

    private static HttpClient As(WebApplicationFactory<Program> host, string user)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(WhoeverAsked.UserHeader, user);
        return client;
    }

    private sealed record TemplateView(
        string Identifier, int Version, string Name, string State,
        IReadOnlyList<string> Actions);

    private static async Task<TemplateView> FirstAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<IReadOnlyList<TemplateView>>("/templates"))![0];

    [Fact]
    public async Task FN_TPL_005_the_templates_a_deployment_has_are_listed_with_their_state()
    {
        // Nobody types a template identifier, and nobody guesses whether one may be used. Both
        // come from the platform (ADR-037 decision 3, ADR-042 decision 3).
        var view = await As(Host(), "user-anna")
            .GetFromJsonAsync<IReadOnlyList<TemplateView>>("/templates");

        Assert.NotEmpty(view!);
        Assert.All(view!, template => Assert.False(string.IsNullOrWhiteSpace(template.State)));
    }

    [Fact]
    public async Task FN_TPL_005_a_seeded_template_starts_as_a_draft()
    {
        // ADR-042 decision 7. Seeding an approved template would assert a signature nobody gave,
        // so what arrives is a starting point rather than a decision.
        var view = await As(Host(), "user-anna")
            .GetFromJsonAsync<IReadOnlyList<TemplateView>>("/templates");

        Assert.All(view!, template => Assert.Equal("draft", template.State));
    }

    [Fact]
    public async Task FN_TPL_005_a_template_says_what_may_be_done_to_it_from_here()
    {
        var first = await FirstAsync(As(Host(), "user-anna"));

        Assert.Contains("submit", first.Actions);
    }

    [Fact]
    public async Task FN_TPL_005_a_template_moves_through_the_same_engine_a_label_does()
    {
        var host = Host();
        var client = As(host, "user-anna");
        var first = await FirstAsync(client);

        using var submitted = await client.PostAsJsonAsync(
            $"/templates/{first.Identifier}/versions/{first.Version}/transitions",
            new { action = "submit" });

        Assert.Equal(HttpStatusCode.OK, submitted.StatusCode);

        var after = (await client.GetFromJsonAsync<IReadOnlyList<TemplateView>>("/templates"))!
            .First(template => template.Identifier == first.Identifier);
        Assert.Equal("in-review", after.State);
    }

    [Fact]
    public async Task FN_TPL_005_whoever_wrote_a_template_may_not_approve_it()
    {
        // The condition that makes template approval mean anything, and it is a label's own rule
        // reaching a different artefact because the engine never knew the difference.
        var host = Host();
        var anna = As(host, "user-anna");
        var first = await FirstAsync(anna);

        using var submitted = await anna.PostAsJsonAsync(
            $"/templates/{first.Identifier}/versions/{first.Version}/transitions",
            new { action = "submit" });
        submitted.EnsureSuccessStatusCode();

        using var approved = await anna.PostAsJsonAsync(
            $"/templates/{first.Identifier}/versions/{first.Version}/transitions",
            new { action = "approve", signatureReference = "sig-1" });

        Assert.NotEqual(HttpStatusCode.OK, approved.StatusCode);
    }

    [Fact]
    public async Task FN_TPL_005_approving_a_template_without_a_signature_is_refused()
    {
        // A template determines what a patient reads, so somebody signs for it - the same gate,
        // not a lighter one (ADR-042 decision 3).
        var host = Host();
        var anna = As(host, "user-anna");
        var first = await FirstAsync(anna);

        using var submitted = await anna.PostAsJsonAsync(
            $"/templates/{first.Identifier}/versions/{first.Version}/transitions",
            new { action = "submit" });
        submitted.EnsureSuccessStatusCode();

        using var approved = await As(host, "user-ben").PostAsJsonAsync(
            $"/templates/{first.Identifier}/versions/{first.Version}/transitions",
            new { action = "approve" });

        Assert.NotEqual(HttpStatusCode.OK, approved.StatusCode);
    }

    [Fact]
    public async Task FN_TPL_005_templates_are_refused_without_a_token()
    {
        using var response = await TestFixtures.Configured(factory).CreateClient()
            .GetAsync("/templates");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed class AlwaysAllow : IPolicyDecisionPoint
    {
        public Task<AuthorizationDecision> DecideAsync(
            AuthorizationQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(new AuthorizationDecision(true, "stub"));
    }

    private sealed class NoSigning : ICredentialVerifier
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

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier,
                        Request.Headers[UserHeader].FirstOrDefault() ?? "user-anna"),
                    new Claim(SubjectFactory.RolesClaim, "author"),
                    new Claim(SubjectFactory.AffiliatesClaim, "uk-affiliate"),
                    new Claim(SubjectFactory.MarketsClaim, "GB"),
                ],
                Name);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Name)));
        }
    }
}
