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

// The authoring projection over HTTP (FN-CC-010).
//   CAP-TPL-005 Template-driven guided authoring that shields authors from raw FHIR
//   CAP-SCM-009 Expose the content model to authoring, validation and rendering
//
// ADR-038. The gap ADR-037 decision 7 predicted building the surface would find: the surface
// must never see a Bundle, and the only read path returned one.
public sealed class SectionEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static string DocumentJson() => EpiBundleReader.Write(ContentScope.Stamp(
        EpiBundleReader.Read(File.ReadAllText(TestFixtures.Path("epi", "minimal-epi-document.json"))),
        new DocumentScope("uk-affiliate", "GB")));

    private WebApplicationFactory<Program> Host(IPolicyDecisionPoint? policy = null) =>
        TestFixtures.Configured(factory, host =>
        {
            host.UseSetting("Epi:Workflow:RoutingPath",
                TestFixtures.RepositoryPath("config", "workflow", "label"));
            host.ConfigureTestServices(services =>
            {
                services.AddAuthentication(WhoeverAsked.Name)
                    .AddScheme<AuthenticationSchemeOptions, WhoeverAsked>(WhoeverAsked.Name, _ => { });
                services.AddSingleton(policy ?? new AlwaysAllow());
                services.AddSingleton<ICredentialVerifier>(new NoSigning());
            });
        });

    private static HttpClient As(WebApplicationFactory<Program> host, string user, string role)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(WhoeverAsked.UserHeader, user);
        client.DefaultRequestHeaders.Add(WhoeverAsked.RolesHeader, role);
        return client;
    }

    private sealed record SectionsView(
        string DocumentIdentifier, int Version, string State, bool Editable,
        ProductView? Product, IReadOnlyList<string> Actions,
        IReadOnlyList<string> SignedActions,
        IReadOnlyDictionary<string, string> SignatureMeanings,
        IReadOnlyList<SectionView> Sections);

    private sealed record ProductView(string Identifier, string? Display);

    private sealed record SectionView(string Identity, string Title, string Narrative);

    private sealed record Created(string Identifier);

    private static async Task<string> AuthoredAsync(WebApplicationFactory<Program> host)
    {
        using var created = await As(host, "user-anna", "author").PostAsync("/fhir/Bundle",
            new StringContent(DocumentJson(), Encoding.UTF8, "application/fhir+json"));
        created.EnsureSuccessStatusCode();
        return (await created.Content.ReadFromJsonAsync<Created>())!.Identifier;
    }

    [Fact]
    public async Task FN_CC_010_a_version_reads_as_sections_and_not_as_a_bundle()
    {
        var host = Host();
        var id = await AuthoredAsync(host);

        var response = await As(host, "user-anna", "author")
            .GetStringAsync($"/labels/{id}/versions/1/sections");

        // The whole point of this endpoint. If it ever returns FHIR, the surface has to parse
        // FHIR, and the web tier becomes a second implementation of the content model.
        foreach (var leak in new[] { "resourceType", "\"Bundle\"", "\"Composition\"", "entry" })
        {
            Assert.DoesNotContain(leak, response, StringComparison.Ordinal);
        }

        Assert.Contains("sections", response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FN_CC_010_every_section_comes_back_with_its_identity_and_title()
    {
        var host = Host();
        var id = await AuthoredAsync(host);

        var view = await As(host, "user-anna", "author")
            .GetFromJsonAsync<SectionsView>($"/labels/{id}/versions/1/sections");

        Assert.NotEmpty(view!.Sections);
        Assert.All(view.Sections, section =>
        {
            Assert.False(string.IsNullOrWhiteSpace(section.Identity));
            Assert.False(string.IsNullOrWhiteSpace(section.Title));
        });
    }

    [Fact]
    public async Task FN_CC_010_saving_sections_mints_the_next_version()
    {
        // Saving never changes the version it was read from - it mints the next one, which is
        // what immutability means and what ADR-038 decision 6 corrects ADR-037 about.
        var host = Host();
        var id = await AuthoredAsync(host);
        var client = As(host, "user-anna", "author");
        var view = await client.GetFromJsonAsync<SectionsView>($"/labels/{id}/versions/1/sections");
        var first = view!.Sections[0];

        using var saved = await client.PostAsJsonAsync(
            $"/labels/{id}/versions/1/sections",
            new
            {
                sections = new[]
                {
                    new
                    {
                        identity = first.Identity,
                        title = first.Title,
                        narrative = "<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>Rewritten.</p></div>",
                    },
                },
            });

        Assert.Equal(HttpStatusCode.Created, saved.StatusCode);

        var next = await client.GetFromJsonAsync<SectionsView>($"/labels/{id}/versions/2/sections");
        Assert.Contains("Rewritten.", next!.Sections[0].Narrative, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FN_CC_010_the_version_that_was_read_is_unchanged_by_the_save()
    {
        var host = Host();
        var id = await AuthoredAsync(host);
        var client = As(host, "user-anna", "author");
        var view = await client.GetFromJsonAsync<SectionsView>($"/labels/{id}/versions/1/sections");
        var before = view!.Sections[0].Narrative;

        using var saved = await client.PostAsJsonAsync(
            $"/labels/{id}/versions/1/sections",
            new
            {
                sections = new[]
                {
                    new
                    {
                        identity = view.Sections[0].Identity,
                        title = view.Sections[0].Title,
                        narrative = "<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>Rewritten.</p></div>",
                    },
                },
            });
        saved.EnsureSuccessStatusCode();

        var reread = await client.GetFromJsonAsync<SectionsView>($"/labels/{id}/versions/1/sections");
        Assert.Equal(before, reread!.Sections[0].Narrative);
    }

    [Fact]
    public async Task FN_CC_010_a_save_naming_a_section_that_is_not_there_is_a_bad_request()
    {
        var host = Host();
        var id = await AuthoredAsync(host);

        using var response = await As(host, "user-anna", "author").PostAsJsonAsync(
            $"/labels/{id}/versions/1/sections",
            new
            {
                sections = new[]
                {
                    new
                    {
                        identity = "sec-invented",
                        title = "Invented",
                        narrative = "<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>x</p></div>",
                    },
                },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FN_CC_010_a_save_the_write_gate_rejects_is_reported_rather_than_stored()
    {
        // The surface bounds what an author can produce, and this is what happens when
        // something reaches the API that it did not produce. The gate is unchanged: this
        // endpoint assembles a Bundle and posts it through exactly the same pipeline.
        var host = Host();
        var id = await AuthoredAsync(host);
        var client = As(host, "user-anna", "author");
        var view = await client.GetFromJsonAsync<SectionsView>($"/labels/{id}/versions/1/sections");

        using var response = await client.PostAsJsonAsync(
            $"/labels/{id}/versions/1/sections",
            new
            {
                sections = new[]
                {
                    new
                    {
                        identity = view!.Sections[0].Identity,
                        title = view.Sections[0].Title,
                        narrative = "not a narrative div at all",
                    },
                },
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FN_CC_010_sections_are_refused_to_a_caller_the_policy_denies()
    {
        var host = Host();
        var id = await AuthoredAsync(host);

        var denied = Host(new AlwaysDeny());
        using var response = await As(denied, "user-cara", "reader")
            .GetAsync($"/labels/{id}/versions/1/sections");

        Assert.NotEqual(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task FN_CC_010_sections_are_refused_without_a_token()
    {
        using var response = await TestFixtures.Configured(factory).CreateClient()
            .GetAsync("/labels/01a00000-0000-7000-8000-00000000000a/versions/1/sections");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task FN_CC_010_a_version_nobody_wrote_is_not_found()
    {
        var host = Host();

        using var response = await As(host, "user-anna", "author")
            .GetAsync("/labels/01a00000-0000-7000-8000-0000000000ff/versions/1/sections");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task FN_CC_012_a_label_about_no_product_says_so_rather_than_inventing_one()
    {
        // A template instantiated before anybody chose a product is normal (ADR-040 decision 5),
        // and the surface has to be able to tell that from a product it was not shown.
        var host = Host();
        var id = await AuthoredAsync(host);

        var view = await As(host, "user-anna", "author")
            .GetFromJsonAsync<SectionsView>($"/labels/{id}/versions/1/sections");

        Assert.Null(view!.Product);
    }

    [Fact]
    public async Task FN_CC_012_a_save_can_say_which_product_the_label_is_about()
    {
        var host = Host();
        var id = await AuthoredAsync(host);
        var client = As(host, "user-anna", "author");
        var view = await client.GetFromJsonAsync<SectionsView>($"/labels/{id}/versions/1/sections");

        using var saved = await client.PostAsJsonAsync(
            $"/labels/{id}/versions/1/sections",
            new
            {
                sections = view!.Sections,
                product = new { identifier = "PROD-0001", display = "SYNTHETIC - Examplinum 10 mg" },
            });
        saved.EnsureSuccessStatusCode();

        var next = await client.GetFromJsonAsync<SectionsView>($"/labels/{id}/versions/2/sections");
        Assert.Equal("PROD-0001", next!.Product!.Identifier);
        Assert.Equal("SYNTHETIC - Examplinum 10 mg", next.Product.Display);
    }

    [Fact]
    public async Task FN_CC_012_a_save_that_names_no_product_leaves_the_one_that_was_there()
    {
        // Omission is not removal. A surface that saved sections without mentioning a product
        // would otherwise silently detach every label it touched from its product.
        var host = Host();
        var id = await AuthoredAsync(host);
        var client = As(host, "user-anna", "author");
        var view = await client.GetFromJsonAsync<SectionsView>($"/labels/{id}/versions/1/sections");

        using var first = await client.PostAsJsonAsync(
            $"/labels/{id}/versions/1/sections",
            new { sections = view!.Sections, product = new { identifier = "PROD-0001", display = "A product" } });
        first.EnsureSuccessStatusCode();

        var second = await client.GetFromJsonAsync<SectionsView>($"/labels/{id}/versions/2/sections");
        using var saved = await client.PostAsJsonAsync(
            $"/labels/{id}/versions/2/sections", new { sections = second!.Sections });
        saved.EnsureSuccessStatusCode();

        var third = await client.GetFromJsonAsync<SectionsView>($"/labels/{id}/versions/3/sections");
        Assert.Equal("PROD-0001", third!.Product!.Identifier);
    }

    [Fact]
    public async Task FN_CC_012_a_product_with_no_identifier_is_a_bad_request()
    {
        // A display alone is the free text ADR-040 exists to replace, and accepting one here
        // would put it straight back.
        var host = Host();
        var id = await AuthoredAsync(host);
        var client = As(host, "user-anna", "author");
        var view = await client.GetFromJsonAsync<SectionsView>($"/labels/{id}/versions/1/sections");

        using var response = await client.PostAsJsonAsync(
            $"/labels/{id}/versions/1/sections",
            new { sections = view!.Sections, product = new { identifier = "", display = "Typed" } });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FN_CC_013_a_version_says_what_may_be_done_to_it_from_here()
    {
        // The surface must not work this out. Deriving permitted transitions in a browser would
        // be a second implementation of the state model, and the weaker of the two
        // (ADR-037 decision 1).
        var host = Host();
        var id = await AuthoredAsync(host);

        var view = await As(host, "user-anna", "author")
            .GetFromJsonAsync<SectionsView>($"/labels/{id}/versions/1/sections");

        Assert.Contains("submit", view!.Actions);
    }

    [Fact]
    public async Task FN_CC_013_it_says_which_of_those_need_a_signature()
    {
        // So the surface asks for a password at a signed gate and nowhere else. Asking for one
        // anyway would teach people to type it whenever prompted, which is how the one that
        // matters stops being a control (ADR-041).
        var host = Host();
        var id = await AuthoredAsync(host);
        var client = As(host, "user-anna", "author");

        using var submitted = await client.PostAsJsonAsync(
            $"/labels/{id}/versions/1/transitions", new { action = "submit" });
        submitted.EnsureSuccessStatusCode();

        var view = await client.GetFromJsonAsync<SectionsView>($"/labels/{id}/versions/1/sections");

        Assert.Contains("approve", view!.Actions);
        Assert.Contains("approve", view.SignedActions);
    }

    [Fact]
    public async Task FN_CC_013_a_signed_gate_says_what_the_signature_must_mean()
    {
        // A signature that says the wrong thing is worse than none - the gate refuses it, and
        // the record would have asserted something nobody intended (ADR-020). The surface was
        // asserting a meaning of its own, which happened to match and would stop matching the
        // moment a deployment configured a different one.
        var host = Host();
        var id = await AuthoredAsync(host);
        var client = As(host, "user-anna", "author");

        using var submitted = await client.PostAsJsonAsync(
            $"/labels/{id}/versions/1/transitions", new { action = "submit" });
        submitted.EnsureSuccessStatusCode();

        var view = await client.GetFromJsonAsync<SectionsView>($"/labels/{id}/versions/1/sections");

        Assert.True(view!.SignatureMeanings.ContainsKey("approve"));
        Assert.False(string.IsNullOrWhiteSpace(view.SignatureMeanings["approve"]));
    }

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
