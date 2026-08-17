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

// Approving a render template under signature (FN-TPL-006).
//   CAP-TPL-008 Template lifecycle with approval via workflow
//   CAP-AUD-003 Electronic signature at approval gates
//
// The gate was configured and unreachable. config/lifecycle/template-states.json requires a
// signature to approve a template, and a signature could only be minted over a FHIR Bundle - so
// POST /signatures answered 404 for every template, and no template could get past in-review.
// Nothing caught it because the tests that existed asserted the refusals, and a gate nobody can
// pass refuses everything correctly (ADR-047).
public sealed class TemplateSignatureTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AnnasPassword = "anna-password";

    private const string BensPassword = "ben-password";

    private WebApplicationFactory<Program> Host() =>
        TestFixtures.Configured(factory, host => host.ConfigureTestServices(services =>
        {
            services.AddAuthentication(WhoeverAsked.Name)
                .AddScheme<AuthenticationSchemeOptions, WhoeverAsked>(WhoeverAsked.Name, _ => { });
            services.AddSingleton<IPolicyDecisionPoint>(new AlwaysAllow());
            services.AddSingleton<ICredentialVerifier>(new KnownUsers());
        }));

    private static HttpClient As(WebApplicationFactory<Program> host, string user)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(WhoeverAsked.UserHeader, user);
        return client;
    }

    private sealed record TemplateView(string Identifier, int Version, string Name, string State);

    private sealed record SignatureReceipt(
        string Reference, string Signer, string PrintedName, string Meaning, string ContentHash);

    private static async Task<TemplateView> FirstAsync(HttpClient client) =>
        (await client.GetFromJsonAsync<IReadOnlyList<TemplateView>>("/templates"))![0];

    private static Task<HttpResponseMessage> SignAsync(
        HttpClient client, TemplateView template, string password, string meaning = "Approval") =>
        client.PostAsJsonAsync("/signatures", new
        {
            documentIdentifier = template.Identifier,
            version = template.Version,
            artefact = "template",
            meaning,
            password,
            reason = "reviewed the stylesheet against the QRD",
        });

    /// <summary>A template submitted for review, which is where approval becomes possible.</summary>
    private static async Task<TemplateView> InReviewAsync(WebApplicationFactory<Program> host)
    {
        var anna = As(host, "user-anna");
        var template = await FirstAsync(anna);

        using var submitted = await anna.PostAsJsonAsync(
            $"/templates/{template.Identifier}/versions/{template.Version}/transitions",
            new { action = "submit" });
        submitted.EnsureSuccessStatusCode();

        return template;
    }

    [Fact]
    public async Task FN_TPL_006_a_template_can_be_signed_for()
    {
        // The whole defect in one assertion: this answered 404 for every template.
        var host = Host();

        using var signed = await SignAsync(As(host, "user-ben"), await FirstAsync(As(host, "user-anna")), BensPassword);

        Assert.Equal(HttpStatusCode.OK, signed.StatusCode);
    }

    [Fact]
    public async Task FN_TPL_006_the_signature_records_who_signed_and_what_they_signed()
    {
        var host = Host();

        using var signed = await SignAsync(
            As(host, "user-ben"), await FirstAsync(As(host, "user-anna")), BensPassword);
        var receipt = await signed.Content.ReadFromJsonAsync<SignatureReceipt>();

        Assert.Equal("user-ben", receipt!.Signer);
        Assert.Equal("Ben Okafor", receipt.PrintedName);
        Assert.StartsWith("sha-256:", receipt.ContentHash, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FN_TPL_006_a_signature_opens_the_template_approval_gate()
    {
        // What the gate exists for, reached for the first time: a template becomes approved,
        // which is what an official render has been waiting on since ADR-033 decision 2.
        var host = Host();
        var template = await InReviewAsync(host);
        var ben = As(host, "user-ben");

        using var signed = await SignAsync(ben, template, BensPassword);
        var receipt = await signed.Content.ReadFromJsonAsync<SignatureReceipt>();

        using var approved = await ben.PostAsJsonAsync(
            $"/templates/{template.Identifier}/versions/{template.Version}/transitions",
            new { action = "approve", signatureReference = receipt!.Reference });

        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        var after = (await As(host, "user-anna")
                .GetFromJsonAsync<IReadOnlyList<TemplateView>>("/templates"))!
            .First(t => t.Identifier == template.Identifier);
        Assert.Equal("approved", after.State);
    }

    [Fact]
    public async Task FN_TPL_006_one_person_s_signature_does_not_carry_another_s_approval()
    {
        // The segregation the gate is made of. Ben signs; Anna cannot spend it.
        var host = Host();
        var template = await InReviewAsync(host);

        using var signed = await SignAsync(As(host, "user-ben"), template, BensPassword);
        var receipt = await signed.Content.ReadFromJsonAsync<SignatureReceipt>();

        using var approved = await As(host, "user-anna").PostAsJsonAsync(
            $"/templates/{template.Identifier}/versions/{template.Version}/transitions",
            new { action = "approve", signatureReference = receipt!.Reference });

        Assert.NotEqual(HttpStatusCode.OK, approved.StatusCode);
    }

    [Fact]
    public async Task FN_TPL_006_a_signature_meaning_something_else_does_not_open_the_gate()
    {
        var host = Host();
        var template = await InReviewAsync(host);
        var ben = As(host, "user-ben");

        using var signed = await SignAsync(ben, template, BensPassword, meaning: "Responsibility");
        var receipt = await signed.Content.ReadFromJsonAsync<SignatureReceipt>();

        using var approved = await ben.PostAsJsonAsync(
            $"/templates/{template.Identifier}/versions/{template.Version}/transitions",
            new { action = "approve", signatureReference = receipt!.Reference });

        Assert.NotEqual(HttpStatusCode.OK, approved.StatusCode);
    }

    [Fact]
    public async Task FN_TPL_006_signing_for_a_template_still_needs_the_password()
    {
        // The second identification component, checked at the point of signing rather than
        // inferred from a session (ADR-020 decision 1). A token is not a signature.
        var host = Host();

        using var signed = await SignAsync(
            As(host, "user-ben"), await FirstAsync(As(host, "user-anna")), "not-bens-password");

        Assert.Equal(HttpStatusCode.Forbidden, signed.StatusCode);
    }

    [Fact]
    public async Task FN_TPL_006_a_template_that_does_not_exist_cannot_be_signed_for()
    {
        var host = Host();

        using var signed = await SignAsync(
            As(host, "user-ben"),
            new TemplateView("no-such-template", 1, "Nothing", "draft"),
            BensPassword);

        Assert.Equal(HttpStatusCode.NotFound, signed.StatusCode);
    }

    [Fact]
    public async Task FN_TPL_006_an_artefact_the_platform_does_not_sign_for_is_refused()
    {
        // Rather than quietly falling back to content, which would hash a label the caller
        // never named and hand back a signature over something else entirely.
        var host = Host();

        using var response = await As(host, "user-ben").PostAsJsonAsync("/signatures", new
        {
            documentIdentifier = "qrd-package-leaflet",
            version = 1,
            artefact = "stylesheet",
            meaning = "Approval",
            password = BensPassword,
            reason = "checked",
        });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    private sealed class KnownUsers : ICredentialVerifier
    {
        public Task<SignerIdentity?> VerifyAsync(
            string identifier, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult((identifier, password) switch
            {
                ("user-anna", AnnasPassword) => new SignerIdentity("user-anna", "Anna Novak"),
                ("user-ben", BensPassword) => new SignerIdentity("user-ben", "Ben Okafor"),
                _ => null,
            });
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

        public const string UserHeader = "X-Test-User";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = Request.Headers[UserHeader].FirstOrDefault() ?? "user-anna";
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, user),
                    new Claim(SubjectFactory.UsernameClaim, user),
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
