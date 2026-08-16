using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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

// Reconstructing a historical version over HTTP (ADR-023).
//   IT-017 A historical version is reconstructable with the metadata that made it valid
//   CAP-LCM-006 Reconstruct the full content and metadata of any historical version
//   CAP-LCM-011 Pin the content snapshot and its validating context at approval
//   CAP-SCH-002 Retrieve a specific version
public sealed class ReconstructionEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AnnasPassword = "correct-horse-battery-staple";
    private const string BensPassword = "battery-staple-correct-horse";

    private static string DocumentJson(string title) => EpiBundleReader.Write(
        Titled(ContentScope.Stamp(
            EpiBundleReader.Read(File.ReadAllText(TestFixtures.Path("epi", "minimal-epi-document.json"))),
            new DocumentScope("uk-affiliate", "GB")), title));

    private static Hl7.Fhir.Model.Bundle Titled(Hl7.Fhir.Model.Bundle bundle, string title)
    {
        ((Hl7.Fhir.Model.Composition)bundle.Entry[0].Resource!).Title = title;
        return bundle;
    }

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
            services.AddSingleton<IPolicyDecisionPoint>(new ScopeCoversResource());
            services.AddSingleton<ICredentialVerifier>(new KnownUsers());
        });
    });

    private static HttpClient As(WebApplicationFactory<Program> host, string user)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(WhoeverAsked.Header, user);
        return client;
    }

    private static async Task<string> CreateAsync(HttpClient client, string title)
    {
        using var response = await client.PostAsync("/fhir/Bundle",
            new StringContent(DocumentJson(title), Encoding.UTF8, "application/fhir+json"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedDocument>())!.Identifier;
    }

    /// <summary>Author submits, someone else signs and approves - the governed path.</summary>
    private static async Task ApproveAsync(WebApplicationFactory<Program> host, string identifier)
    {
        var anna = As(host, "user-anna");
        using var submitted = await anna.PostAsJsonAsync(
            $"/labels/{identifier}/versions/1/transitions", new { action = "submit", reason = "ready" });
        submitted.EnsureSuccessStatusCode();

        var ben = As(host, "user-ben");
        using var signed = await ben.PostAsJsonAsync("/signatures", new
        {
            documentIdentifier = identifier,
            version = 1,
            meaning = "Approval",
            password = BensPassword,
            reason = "checked against source",
        });
        signed.EnsureSuccessStatusCode();
        var reference = (await signed.Content.ReadFromJsonAsync<SignatureReceipt>())!.Reference;

        using var approved = await ben.PostAsJsonAsync(
            $"/labels/{identifier}/versions/1/transitions",
            new { action = "approve", signatureReference = reference });
        approved.EnsureSuccessStatusCode();
    }

    [Fact]
    public async Task CAP_SCH_002_a_specific_version_is_retrievable_not_only_the_latest()
    {
        // Reconstruction is worthless while the only reachable content is the latest version
        // (ADR-023 decision 6).
        var host = Host();
        var anna = As(host, "user-anna");
        var identifier = await CreateAsync(anna, "SYNTHETIC - first");

        using var second = await anna.PostAsync($"/fhir/Bundle/{identifier}/versions",
            new StringContent(DocumentJson("SYNTHETIC - second"), Encoding.UTF8, "application/fhir+json"));
        second.EnsureSuccessStatusCode();

        using var first = await anna.GetAsync($"/fhir/Bundle/{identifier}/versions/1");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Contains("SYNTHETIC - first", await first.Content.ReadAsStringAsync(), StringComparison.Ordinal);

        using var latest = await anna.GetAsync($"/fhir/Bundle/{identifier}");
        Assert.Contains("SYNTHETIC - second", await latest.Content.ReadAsStringAsync(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task CAP_SCM_004_a_label_borrowing_a_unit_nobody_can_see_is_refused_over_http()
    {
        // Over HTTP, and without saying whether the unit exists: a borrow must not be a way of
        // discovering one (ADR-026, CAP-SCH-004).
        var host = Host();
        var bundle = EpiBundleReader.Read(DocumentJson("SYNTHETIC - borrows a ghost"));
        var composition = (Hl7.Fhir.Model.Composition)bundle.Entry[0].Resource!;
        ReusableUnits.Borrow(
            composition.Section[0],
            new UnitReference(ContentIdentity.Mint(), 1));

        using var response = await As(host, "user-anna").PostAsync("/fhir/Bundle",
            new StringContent(EpiBundleReader.Write(bundle), Encoding.UTF8, "application/fhir+json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CAP_SCM_005_a_dangling_cross_reference_is_refused_over_http()
    {
        // The write gate is the last place a label pointing at a section it does not have is
        // cheap to catch (ADR-028 decision 3).
        var host = Host();
        var bundle = EpiBundleReader.Read(DocumentJson("SYNTHETIC - points at nothing"));
        var composition = (Hl7.Fhir.Model.Composition)bundle.Entry[0].Resource!;
        composition.Section[0].Text = new Hl7.Fhir.Model.Narrative
        {
            Status = Hl7.Fhir.Model.Narrative.NarrativeStatus.Generated,
            Div = "<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>See "
                  + "<a href=\"#no-such-section\">section 9</a>.</p></div>",
        };

        using var response = await As(host, "user-anna").PostAsync("/fhir/Bundle",
            new StringContent(EpiBundleReader.Write(bundle), Encoding.UTF8, "application/fhir+json"));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CAP_SCH_002_a_version_that_does_not_exist_is_not_found()
    {
        var host = Host();
        var identifier = await CreateAsync(As(host, "user-anna"), "SYNTHETIC - only version");

        using var response = await As(host, "user-anna").GetAsync($"/fhir/Bundle/{identifier}/versions/7");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task IT_017_an_approved_version_reconstructs_with_what_it_was_approved_against()
    {
        var host = Host();
        var identifier = await CreateAsync(As(host, "user-anna"), "SYNTHETIC - approved label");
        await ApproveAsync(host, identifier);

        using var response = await As(host, "user-anna")
            .GetAsync($"/labels/{identifier}/versions/1/reconstruction");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var record = await response.Content.ReadFromJsonAsync<Reconstruction>();

        Assert.Equal("approved", record!.State);
        Assert.NotNull(record.PinnedContext);
        Assert.Equal("label", record.PinnedContext!.StateModel);
        Assert.StartsWith("sha-256:", record.PinnedContext.ContentHash, StringComparison.Ordinal);
        Assert.Contains(record.PinnedContext.Packages,
            p => p.Name == "hl7.fhir.uv.emedicinal-product-info" && p.Version == "1.0.0");
        Assert.Equal(
            "https://epi.example.org/identifier/document", record.PinnedContext.IdentifierAuthority);
    }

    [Fact]
    public async Task IT_017_the_reconstruction_carries_the_whole_history_and_the_signature_used()
    {
        // Who did what, when, and on the strength of which signature. A record naming an
        // approval without naming what was signed cannot answer an inspection.
        var host = Host();
        var identifier = await CreateAsync(As(host, "user-anna"), "SYNTHETIC - approved label");
        await ApproveAsync(host, identifier);

        using var response = await As(host, "user-anna")
            .GetAsync($"/labels/{identifier}/versions/1/reconstruction");
        var record = await response.Content.ReadFromJsonAsync<Reconstruction>();

        Assert.Equal(["submit", "approve"], record!.History.Select(h => h.Action));
        Assert.Equal("user-anna", record.Author);

        var approval = record.History.Single(h => h.Action == "approve");
        Assert.Equal("user-ben", approval.Actor);
        Assert.NotNull(approval.Signature);
        Assert.Equal("Ben Okafor", approval.Signature!.PrintedName);
        Assert.Equal("Approval", approval.Signature.Meaning);

        // The signature covers the same bytes the context was pinned over.
        Assert.Equal(record.PinnedContext!.ContentHash, approval.Signature.ContentHash);
    }

    [Fact]
    public async Task CAP_LCM_011_a_draft_reconstructs_with_no_pinned_context_rather_than_an_invented_one()
    {
        // Content plus history is all a draft ever was. A context implies a commitment nobody
        // made (ADR-023 decision 7).
        var host = Host();
        var identifier = await CreateAsync(As(host, "user-anna"), "SYNTHETIC - still a draft");

        using var response = await As(host, "user-anna")
            .GetAsync($"/labels/{identifier}/versions/1/reconstruction");

        var record = await response.Content.ReadFromJsonAsync<Reconstruction>();
        Assert.Equal("draft", record!.State);
        Assert.Null(record.PinnedContext);
        Assert.Empty(record.History);
    }

    [Fact]
    public async Task IT_017_the_reconstruction_reports_whether_the_pinned_packages_still_match()
    {
        // Reported, not enforced. Where they match, the context is reproducible from the
        // repository; where they do not, that is a finding the answer must carry rather than
        // a reason to refuse the request (ADR-023 decision 5).
        var host = Host();
        var identifier = await CreateAsync(As(host, "user-anna"), "SYNTHETIC - approved label");
        await ApproveAsync(host, identifier);

        using var response = await As(host, "user-anna")
            .GetAsync($"/labels/{identifier}/versions/1/reconstruction");
        var record = await response.Content.ReadFromJsonAsync<Reconstruction>();

        Assert.True(record!.PackagesStillMatch);
        Assert.Empty(record.PackageDiscrepancies);
    }

    [Fact]
    public async Task CAP_IAM_007_a_reconstruction_the_caller_may_not_see_is_indistinguishable_from_absent()
    {
        // Discriminating on purpose: the same URL, on the same host, answering 200 to one
        // caller and 404 to another. Asserting only the 404 would pass just as well against an
        // endpoint that does not exist, which is how this kind of test comes to prove nothing.
        var host = Host();
        var identifier = await CreateAsync(As(host, "user-anna"), "SYNTHETIC - not for everyone");

        var reconstruction = $"/labels/{identifier}/versions/1/reconstruction";
        var content = $"/fhir/Bundle/{identifier}/versions/1";

        using var visible = await As(host, "user-anna").GetAsync(reconstruction);
        using var visibleContent = await As(host, "user-anna").GetAsync(content);
        Assert.Equal(HttpStatusCode.OK, visible.StatusCode);
        Assert.Equal(HttpStatusCode.OK, visibleContent.StatusCode);

        // user-zed holds no affiliate or market, so the policy covers nothing for them.
        using var hidden = await As(host, "user-zed").GetAsync(reconstruction);
        using var hiddenContent = await As(host, "user-zed").GetAsync(content);
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, hiddenContent.StatusCode);
    }

    [Fact]
    public async Task CAP_LCM_006_reconstruction_is_refused_without_a_token()
    {
        using var response = await TestFixtures.Configured(factory).CreateClient()
            .GetAsync($"/labels/{Guid.NewGuid()}/versions/1/reconstruction");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record CreatedDocument(string Identifier, string System, int Version);

    private sealed record SignatureReceipt(string Reference);

    private sealed record Reconstruction(
        string Identifier,
        int Version,
        string State,
        string Author,
        PinnedContextView? PinnedContext,
        bool PackagesStillMatch,
        IReadOnlyList<string> PackageDiscrepancies,
        IReadOnlyList<HistoryEntry> History);

    private sealed record PinnedContextView(
        string ContentHash,
        string StateModel,
        string IdentifierAuthority,
        string? Template,
        int? TemplateVersion,
        IReadOnlyList<PackageView> Packages);

    private sealed record PackageView(string Name, string Version, string Sha256);

    private sealed record HistoryEntry(
        string From, string To, string Action, string Actor, SignatureView? Signature);

    private sealed record SignatureView(
        string Reference, string Signer, string PrintedName, string Meaning, string ContentHash);

    [Fact]
    public async Task CAP_LCM_011_the_reconstruction_names_the_terminology_it_was_approved_against()
    {
        // ADR-036 put terminology bindings in the pinned context precisely so this question
        // could be answered, and the reconstruction did not report them - which made "what was
        // this approved against" an incomplete answer to the question ADR-023 exists for.
        var host = Host();
        var identifier = await CreateAsync(As(host, "user-anna"), "SYNTHETIC - approved label");
        await ApproveAsync(host, identifier);

        var record = await As(host, "user-anna")
            .GetFromJsonAsync<JsonElement>($"/labels/{identifier}/versions/1/reconstruction");

        var pinned = record.GetProperty("pinnedContext");
        Assert.True(
            pinned.TryGetProperty("terminologyBindings", out var bindings),
            "the pinned context records terminology and the reconstruction must report it");
        Assert.Equal(JsonValueKind.Array, bindings.ValueKind);
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

    /// <summary>
    /// The scope half of the shipped policy. Every caller here holds a role that may read, so
    /// what separates them is the affiliate and market their token asserts.
    /// </summary>
    private sealed class ScopeCoversResource : IPolicyDecisionPoint
    {
        public Task<AuthorizationDecision> DecideAsync(
            AuthorizationQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(
                query.Subject.Affiliates.Contains(query.Resource.Affiliate)
                && query.Subject.Markets.Contains(query.Resource.Market)
                    ? new AuthorizationDecision(true, "stub")
                    : AuthorizationDecision.Deny("out of scope"));
    }

    private sealed class WhoeverAsked(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        Microsoft.Extensions.Logging.ILoggerFactory logger,
        UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        public const string Name = "Test";

        public const string Header = "X-Test-User";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var user = Request.Headers[Header].FirstOrDefault() ?? "user-anna";

            // user-zed is authenticated and holds no scope at all, which is what least
            // privilege means when a token carries no attributes.
            var scoped = user != "user-zed";
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, user),
                    new Claim(SubjectFactory.RolesClaim, "author"),
                    .. scoped
                        ? new[]
                        {
                            new Claim(SubjectFactory.AffiliatesClaim, "uk-affiliate"),
                            new Claim(SubjectFactory.MarketsClaim, "GB"),
                        }
                        : [],
                ],
                Name);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Name)));
        }
    }
}
