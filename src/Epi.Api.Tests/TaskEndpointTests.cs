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

// What routing has asked, over HTTP (ADR-031).
//   CAP-WFL-001 Configurable review and approval workflows
public sealed class TaskEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static string DocumentJson() => EpiBundleReader.Write(ContentScope.Stamp(
        EpiBundleReader.Read(File.ReadAllText(TestFixtures.Path("epi", "minimal-epi-document.json"))),
        new DocumentScope("uk-affiliate", "GB")));

    private WebApplicationFactory<Program> Host() => factory.WithWebHostBuilder(host =>
    {
        host.UseSetting("Epi:MarketsPath", TestFixtures.RepositoryPath("config", "markets"));
        host.UseSetting("Epi:IdentifiersPath",
            TestFixtures.RepositoryPath("config", "identifiers.json"));
        host.UseSetting("Epi:Lifecycle:StatesPath",
            TestFixtures.RepositoryPath("config", "lifecycle", "label-states.json"));
        host.UseSetting("Epi:Lifecycle:MarketStatesPath",
            TestFixtures.RepositoryPath("config", "lifecycle", "market-approval-states.json"));
        host.UseSetting("Epi:Workflow:RoutingPath",
            TestFixtures.RepositoryPath("config", "workflow", "label-routing.json"));
        host.ConfigureTestServices(services =>
        {
            services.AddAuthentication(WhoeverAsked.Name)
                .AddScheme<AuthenticationSchemeOptions, WhoeverAsked>(WhoeverAsked.Name, _ => { });
            services.AddSingleton<IPolicyDecisionPoint>(new AlwaysAllow());
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

    /// <summary>Creates a version and submits it, which is what raises a review task.</summary>
    private static async Task<string> SubmittedAsync(WebApplicationFactory<Program> host)
    {
        var anna = As(host, "user-anna", "author");
        using var created = await anna.PostAsync("/fhir/Bundle",
            new StringContent(DocumentJson(), Encoding.UTF8, "application/fhir+json"));
        created.EnsureSuccessStatusCode();
        var identifier = (await created.Content.ReadFromJsonAsync<CreatedDocument>())!.Identifier;

        using var submitted = await anna.PostAsJsonAsync(
            $"/labels/{identifier}/versions/1/transitions",
            new { action = "submit", reason = "ready for review" });
        submitted.EnsureSuccessStatusCode();

        return identifier;
    }

    [Fact]
    public async Task CAP_WFL_001_a_submitted_version_is_waiting_for_an_approver()
    {
        var host = Host();
        var identifier = await SubmittedAsync(host);

        var waiting = await As(host, "user-ben", "approver")
            .GetFromJsonAsync<IReadOnlyList<TaskView>>("/tasks");

        var task = Assert.Single(waiting!);
        Assert.Equal(identifier, task.DocumentIdentifier);
        Assert.Equal("approve", task.Action);
        Assert.Equal("approver", task.Assignee);
    }

    [Fact]
    public async Task CAP_WFL_001_a_caller_holding_another_role_is_asked_nothing()
    {
        var host = Host();
        await SubmittedAsync(host);

        var waiting = await As(host, "user-anna", "author")
            .GetFromJsonAsync<IReadOnlyList<TaskView>>("/tasks");

        Assert.Empty(waiting!);
    }

    [Fact]
    public async Task CAP_WFL_001_making_the_transition_takes_the_task_off_the_list()
    {
        var host = Host();
        var identifier = await SubmittedAsync(host);
        var ben = As(host, "user-ben", "approver");

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

        Assert.Empty(await ben.GetFromJsonAsync<IReadOnlyList<TaskView>>("/tasks") ?? []);
    }

    [Fact]
    public async Task CAP_WFL_001_a_reassigned_task_moves_to_whoever_now_holds_it()
    {
        var host = Host();
        await SubmittedAsync(host);
        var ben = As(host, "user-ben", "approver");
        var task = (await ben.GetFromJsonAsync<IReadOnlyList<TaskView>>("/tasks"))![0];

        using var moved = await ben.PostAsJsonAsync(
            $"/tasks/{task.Identifier}/assignment",
            new { assignee = "user-rae", reason = "Ben is on leave" });
        moved.EnsureSuccessStatusCode();

        Assert.Empty(await ben.GetFromJsonAsync<IReadOnlyList<TaskView>>("/tasks") ?? []);
        Assert.Single(await As(host, "user-rae", "regulatory")
            .GetFromJsonAsync<IReadOnlyList<TaskView>>("/tasks") ?? []);
    }

    [Fact]
    public async Task CAP_WFL_001_a_task_nobody_raised_cannot_be_reassigned()
    {
        var host = Host();

        using var response = await As(host, "user-ben", "approver")
            .PostAsJsonAsync("/tasks/never-raised/assignment", new { assignee = "user-rae" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CAP_WFL_001_a_reassignment_must_name_somebody()
    {
        var host = Host();
        await SubmittedAsync(host);
        var ben = As(host, "user-ben", "approver");
        var task = (await ben.GetFromJsonAsync<IReadOnlyList<TaskView>>("/tasks"))![0];

        using var response = await ben.PostAsJsonAsync(
            $"/tasks/{task.Identifier}/assignment", new { assignee = "  " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task CAP_WFL_001_tasks_are_refused_without_a_token()
    {
        using var listed = await factory.CreateClient().GetAsync("/tasks");
        using var moved = await factory.CreateClient()
            .PostAsJsonAsync("/tasks/anything/assignment", new { assignee = "user-rae" });

        Assert.Equal(HttpStatusCode.Unauthorized, listed.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, moved.StatusCode);
    }

    private const string BensPassword = "battery-staple-correct-horse";

    private sealed record CreatedDocument(string Identifier, string System, int Version);

    private sealed record SignatureReceipt(string Reference);

    private sealed record TaskView(
        string Identifier, string DocumentIdentifier, int Version, string Action, string Assignee);

    private sealed class KnownUsers : ICredentialVerifier
    {
        public Task<SignerIdentity?> VerifyAsync(
            string identifier, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult((identifier, password) switch
            {
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
