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

// The governed flow over HTTP: author submits, someone else signs and approves.
//   IT-010 An unpermitted transition is rejected; a permitted one records actor and timestamp
//   IT-011 The author of a version cannot approve it
//   IT-012 Approval captures a signature binding signer, meaning, time and a hash of the
//          version signed
public sealed class SigningEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string AnnasPassword = "correct-horse-battery-staple";
    private const string BensPassword = "battery-staple-correct-horse";

    private static string DocumentJson() => EpiBundleReader.Write(ContentScope.Stamp(
        EpiBundleReader.Read(File.ReadAllText(TestFixtures.Path("epi", "minimal-epi-document.json"))),
        new DocumentScope("uk-affiliate", "GB")));

    /// <summary>
    /// One host shared by every caller in a test, so the in-memory stores are the same ones.
    /// Callers differ only by the identity their token carries.
    /// </summary>
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
            services.AddSingleton<IPolicyDecisionPoint>(new AlwaysAllow());
            services.AddSingleton<ICredentialVerifier>(new KnownUsers());
        });
    });

    /// <summary>A caller identified by the header the stand-in scheme reads.</summary>
    private static HttpClient As(WebApplicationFactory<Program> host, string user)
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(WhoeverAsked.Header, user);
        return client;
    }

    private static async Task<string> CreateAsync(HttpClient client)
    {
        using var response = await client.PostAsync("/fhir/Bundle",
            new StringContent(DocumentJson(), Encoding.UTF8, "application/fhir+json"));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreatedDocument>())!.Identifier;
    }

    private static Task<HttpResponseMessage> TransitionAsync(
        HttpClient client, string id, string action, string? signature = null) =>
        client.PostAsJsonAsync($"/labels/{id}/versions/1/transitions",
            new { action, reason = "test", signatureReference = signature });

    private static Task<HttpResponseMessage> SignAsync(
        HttpClient client, string id, string password, string meaning = "Approval") =>
        client.PostAsJsonAsync("/signatures", new
        {
            documentIdentifier = id,
            version = 1,
            meaning,
            password,
            reason = "checked against source",
        });

    [Fact]
    public async Task IT_012_an_author_submits_and_someone_else_signs_and_approves()
    {
        // The whole governed flow, end to end over HTTP. Every part of this has been proven in
        // isolation; this is the assertion that they are wired to each other.
        var host = Host();
        var anna = As(host, "user-anna");
        var ben = As(host, "user-ben");

        var id = await CreateAsync(anna);
        (await TransitionAsync(anna, id, "submit")).EnsureSuccessStatusCode();

        using var signed = await SignAsync(ben, id, BensPassword);
        signed.EnsureSuccessStatusCode();
        var signature = await signed.Content.ReadFromJsonAsync<SignatureReceipt>();

        Assert.Equal("user-ben", signature!.Signer);
        Assert.Equal("Ben Okafor", signature.PrintedName);
        Assert.StartsWith("sha-256:", signature.ContentHash, StringComparison.Ordinal);

        using var approved = await TransitionAsync(ben, id, "approve", signature.Reference);
        Assert.Equal(HttpStatusCode.OK, approved.StatusCode);

        using var state = await anna.GetAsync($"/labels/{id}/versions/1/state");
        Assert.Equal("approved", (await state.Content.ReadFromJsonAsync<VersionState>())!.State);
    }

    [Fact]
    public async Task IT_011_the_author_cannot_approve_their_own_version_over_http()
    {
        // Criterion 2, at the surface. A valid signature does not excuse segregation of duties,
        // and the author signing their own work is exactly what someone would try.
        var host = Host();
        var anna = As(host, "user-anna");

        var id = await CreateAsync(anna);
        (await TransitionAsync(anna, id, "submit")).EnsureSuccessStatusCode();

        using var signed = await SignAsync(anna, id, AnnasPassword);
        var signature = await signed.Content.ReadFromJsonAsync<SignatureReceipt>();

        using var refused = await TransitionAsync(anna, id, "approve", signature!.Reference);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("may not approve", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task IT_010_an_approval_without_a_signature_is_refused()
    {
        var host = Host();
        var anna = As(host, "user-anna");
        var id = await CreateAsync(anna);
        (await TransitionAsync(anna, id, "submit")).EnsureSuccessStatusCode();

        using var refused = await TransitionAsync(As(host, "user-ben"), id, "approve");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("signature", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task IT_010_a_transition_the_model_does_not_permit_is_refused()
    {
        var host = Host();
        var anna = As(host, "user-anna");
        var id = await CreateAsync(anna);

        using var refused = await TransitionAsync(anna, id, "supersede");

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("permits no", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task IT_012_a_signature_cannot_be_spent_twice()
    {
        // The reference is unique platform-wide, so a signature that carried an approval
        // cannot then carry a withdrawal.
        var host = Host();
        var anna = As(host, "user-anna");
        var ben = As(host, "user-ben");

        var id = await CreateAsync(anna);
        (await TransitionAsync(anna, id, "submit")).EnsureSuccessStatusCode();

        var signature = (await (await SignAsync(ben, id, BensPassword)).Content
            .ReadFromJsonAsync<SignatureReceipt>())!;
        (await TransitionAsync(ben, id, "approve", signature.Reference)).EnsureSuccessStatusCode();

        using var refused = await TransitionAsync(ben, id, "withdraw", signature.Reference);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Contains("already", await refused.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task IT_012_a_wrong_password_is_refused_without_saying_what_was_wrong()
    {
        var host = Host();
        var anna = As(host, "user-anna");
        var id = await CreateAsync(anna);

        using var refused = await SignAsync(anna, id, "not-annas-password");

        Assert.Equal(HttpStatusCode.Forbidden, refused.StatusCode);

        var body = await refused.Content.ReadAsStringAsync();
        Assert.DoesNotContain("not-annas-password", body, StringComparison.Ordinal);
        Assert.DoesNotContain("password was", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IT_012_a_signature_may_only_be_made_over_content_the_signer_may_see()
    {
        // Otherwise signing becomes a way of discovering that a document exists, and of
        // attesting to content the signer was never allowed to read.
        var host = factory.WithWebHostBuilder(host =>
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
                services.AddSingleton<IPolicyDecisionPoint>(new AlwaysDeny());
                services.AddSingleton<ICredentialVerifier>(new KnownUsers());
            });
        });

        using var refused = await SignAsync(As(host, "user-anna"), Guid.NewGuid().ToString(), AnnasPassword);

        Assert.Equal(HttpStatusCode.NotFound, refused.StatusCode);
    }

    [Fact]
    public async Task IT_010_a_transition_is_refused_without_a_token()
    {
        using var response = await TestFixtures.Configured(factory).CreateClient().PostAsJsonAsync(
            $"/labels/{Guid.NewGuid()}/versions/1/transitions", new { action = "submit" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private sealed record CreatedDocument(string Identifier, string System, int Version);

    private sealed record VersionState(string State, string Author);

    private sealed record SignatureReceipt(
        string Reference, string Signer, string PrintedName, string Meaning, string ContentHash);

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

    private sealed class AlwaysDeny : IPolicyDecisionPoint
    {
        public Task<AuthorizationDecision> DecideAsync(
            AuthorizationQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(AuthorizationDecision.Deny("stub"));
    }

    /// <summary>
    /// Signs the caller in as whoever the request header names, so one host can serve an
    /// author and an approver - which is the only way to test segregation of duties over HTTP.
    /// </summary>
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
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, user),
                    new Claim("affiliate", "uk-affiliate"),
                    new Claim("market", "GB"),
                ],
                Name);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Name)));
        }
    }
}
