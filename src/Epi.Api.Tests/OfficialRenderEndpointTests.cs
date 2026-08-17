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

// The artefact of record, over HTTP (FN-RND-004).
//   CAP-RND-002 Store rendered output as immutable assets
//   CAP-RND-004 Distinguish an author preview from an official render
//
// The preview endpoint next door renders anything an author may read and files nothing. This one
// refuses everything the preview allows and files what it produces, which is the whole difference
// between looking at your work and producing the document a regulator is sent (ADR-046).
public sealed class OfficialRenderEndpointTests(WebApplicationFactory<Program> factory)
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

    private sealed record Saved(string DocumentIdentifier, int Version);

    private sealed record Created(string Identifier);

    private sealed record TemplateView(string Identifier, int Version, string State);

    private sealed record SignatureReceipt(string Reference);

    private sealed record RenderView(
        string Template, int TemplateVersion, string Key, string MediaType, bool AlreadyFiled);

    /// <summary>A saved label version, created the way every other test creates one.</summary>
    private static async Task<Saved> SavedAsync(HttpClient client)
    {
        using var response = await client.PostAsync(
            "/fhir/Bundle", new StringContent(DocumentJson(), Encoding.UTF8, "application/fhir+json"));
        response.EnsureSuccessStatusCode();

        var created = await response.Content.ReadFromJsonAsync<Created>();
        return new Saved(created!.Identifier, 1);
    }

    private static string DocumentJson() => EpiBundleReader.Write(ContentScope.Stamp(
        EpiBundleReader.Read(File.ReadAllText(TestFixtures.Path("epi", "minimal-epi-document.json"))),
        new DocumentScope("uk-affiliate", "GB")));

    /// <summary>A signature over an artefact, by whoever is asking.</summary>
    private static async Task<string> SignatureAsync(
        HttpClient client, string identifier, int version, string artefact, string password)
    {
        using var signed = await client.PostAsJsonAsync("/signatures", new
        {
            documentIdentifier = identifier,
            version,
            artefact,
            meaning = "Approval",
            password,
            reason = "checked",
        });

        signed.EnsureSuccessStatusCode();
        return (await signed.Content.ReadFromJsonAsync<SignatureReceipt>())!.Reference;
    }

    /// <summary>The first template, taken to approved by somebody who did not write it.</summary>
    private static async Task<string> ApprovedTemplateAsync(WebApplicationFactory<Program> host)
    {
        var anna = As(host, "user-anna");
        var template = (await anna.GetFromJsonAsync<IReadOnlyList<TemplateView>>("/templates"))![0];

        using var submitted = await anna.PostAsJsonAsync(
            $"/templates/{template.Identifier}/versions/{template.Version}/transitions",
            new { action = "submit" });
        submitted.EnsureSuccessStatusCode();

        var ben = As(host, "user-ben");
        var signature = await SignatureAsync(
            ben, template.Identifier, template.Version, "template", BensPassword);

        using var approved = await ben.PostAsJsonAsync(
            $"/templates/{template.Identifier}/versions/{template.Version}/transitions",
            new { action = "approve", signatureReference = signature });
        approved.EnsureSuccessStatusCode();

        return template.Identifier;
    }

    /// <summary>An approved label version: submitted by its author, approved by somebody else.</summary>
    private static async Task<Saved> ApprovedLabelAsync(WebApplicationFactory<Program> host)
    {
        var anna = As(host, "user-anna");
        var saved = await SavedAsync(anna);

        using var submitted = await anna.PostAsJsonAsync(
            $"/labels/{saved.DocumentIdentifier}/versions/{saved.Version}/transitions",
            new { action = "submit" });
        submitted.EnsureSuccessStatusCode();

        var ben = As(host, "user-ben");
        var signature = await SignatureAsync(
            ben, saved.DocumentIdentifier, saved.Version, "content", BensPassword);

        using var approved = await ben.PostAsJsonAsync(
            $"/labels/{saved.DocumentIdentifier}/versions/{saved.Version}/transitions",
            new { action = "approve", signatureReference = signature });
        approved.EnsureSuccessStatusCode();

        return saved;
    }

    [Fact]
    public async Task FN_RND_004_an_approved_version_and_an_approved_template_produce_a_render()
    {
        // What ADR-033 decision 2 has been waiting for since iteration 3, reachable at last now
        // that a template can be approved (ADR-047).
        var host = Host();
        var template = await ApprovedTemplateAsync(host);
        var saved = await ApprovedLabelAsync(host);

        using var response = await As(host, "user-anna").PostAsJsonAsync(
            $"/labels/{saved.DocumentIdentifier}/versions/{saved.Version}/renders",
            new { template });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var view = await response.Content.ReadFromJsonAsync<RenderView>();
        Assert.Equal(template, view!.Template);
        Assert.False(view.AlreadyFiled);
    }

    [Fact]
    public async Task FN_RND_004_what_was_filed_can_be_listed_and_read_back()
    {
        var host = Host();
        var template = await ApprovedTemplateAsync(host);
        var saved = await ApprovedLabelAsync(host);
        var anna = As(host, "user-anna");

        using var produced = await anna.PostAsJsonAsync(
            $"/labels/{saved.DocumentIdentifier}/versions/{saved.Version}/renders",
            new { template });
        produced.EnsureSuccessStatusCode();

        var filed = await anna.GetFromJsonAsync<IReadOnlyList<RenderView>>(
            $"/labels/{saved.DocumentIdentifier}/versions/{saved.Version}/renders");
        var only = Assert.Single(filed!);

        var leaflet = await anna.GetStringAsync(
            $"/labels/{saved.DocumentIdentifier}/versions/{saved.Version}/renders/"
            + $"{only.Template}/{only.TemplateVersion}");

        Assert.Contains("SYNTHETIC", leaflet, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("preview", leaflet, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FN_RND_004_asking_twice_files_once()
    {
        // A render is a pure function of its two versions, so asking again asks for the same
        // bytes. Answering 409 on the write-once rule would make an idempotent request look like
        // a conflict and teach callers to retry through it.
        var host = Host();
        var template = await ApprovedTemplateAsync(host);
        var saved = await ApprovedLabelAsync(host);
        var anna = As(host, "user-anna");
        var path = $"/labels/{saved.DocumentIdentifier}/versions/{saved.Version}/renders";

        using var first = await anna.PostAsJsonAsync(path, new { template });
        using var second = await anna.PostAsJsonAsync(path, new { template });

        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.True((await second.Content.ReadFromJsonAsync<RenderView>())!.AlreadyFiled);
        Assert.Single((await anna.GetFromJsonAsync<IReadOnlyList<RenderView>>(path))!);
    }

    [Fact]
    public async Task FN_RND_004_a_draft_version_has_no_official_render()
    {
        // The rule the preview exists to serve instead. An official render of a draft is a
        // document somebody will eventually send (CAP-RND-004).
        var host = Host();
        var template = await ApprovedTemplateAsync(host);
        var anna = As(host, "user-anna");
        var saved = await SavedAsync(anna);

        using var response = await anna.PostAsJsonAsync(
            $"/labels/{saved.DocumentIdentifier}/versions/{saved.Version}/renders",
            new { template });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task FN_TPL_005_a_template_nobody_approved_produces_no_official_render()
    {
        // The state a fresh deployment is in: the seeded templates arrive as drafts (ADR-042
        // decision 7), so content could be approved and there would still be nothing to render
        // it with until somebody signs for a template.
        var host = Host();
        var saved = await ApprovedLabelAsync(host);

        using var response = await As(host, "user-anna").PostAsJsonAsync(
            $"/labels/{saved.DocumentIdentifier}/versions/{saved.Version}/renders",
            new { template = "qrd-package-leaflet" });

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task FN_RND_004_a_version_nobody_wrote_has_no_official_render()
    {
        using var response = await As(Host(), "user-anna").PostAsJsonAsync(
            "/labels/01a00000-0000-7000-8000-0000000000ff/versions/1/renders",
            new { template = "qrd-package-leaflet" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FN_RND_004_a_render_needs_a_template_to_be_named()
    {
        var host = Host();
        var anna = As(host, "user-anna");
        var saved = await SavedAsync(anna);

        using var response = await anna.PostAsJsonAsync(
            $"/labels/{saved.DocumentIdentifier}/versions/{saved.Version}/renders",
            new { template = "" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FN_RND_004_nothing_is_filed_for_a_version_that_has_not_been_rendered()
    {
        var host = Host();
        var anna = As(host, "user-anna");
        var saved = await SavedAsync(anna);

        var filed = await anna.GetFromJsonAsync<IReadOnlyList<RenderView>>(
            $"/labels/{saved.DocumentIdentifier}/versions/{saved.Version}/renders");

        Assert.Empty(filed!);
    }

    [Fact]
    public async Task FN_RND_004_a_render_that_was_never_produced_cannot_be_read()
    {
        var host = Host();
        var saved = await SavedAsync(As(host, "user-anna"));

        using var response = await As(host, "user-anna").GetAsync(
            $"/labels/{saved.DocumentIdentifier}/versions/{saved.Version}"
            + "/renders/qrd-package-leaflet/1");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FN_RND_004_an_official_render_is_refused_without_a_token()
    {
        using var response = await TestFixtures.Configured(factory).CreateClient()
            .PostAsJsonAsync(
                "/labels/01a00000-0000-7000-8000-0000000000ff/versions/1/renders",
                new { template = "qrd-package-leaflet" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FN_RND_004_a_listing_is_refused_without_a_token()
    {
        using var response = await TestFixtures.Configured(factory).CreateClient()
            .GetAsync("/labels/01a00000-0000-7000-8000-0000000000ff/versions/1/renders");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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

        private string Who() => Request.Headers[UserHeader].FirstOrDefault() ?? "user-anna";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, Who()),
                    new Claim(SubjectFactory.UsernameClaim, Who()),
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
