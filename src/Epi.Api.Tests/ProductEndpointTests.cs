using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using Epi.ContentCore;
using Epi.Iam;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Xunit;

namespace Epi.Api.Tests;

// The product directory, over HTTP (FN-MDM-002).
//   CAP-MDM-008 Expose an identifier resolution and association API
//
// ADR-036 built the port and ADR-040 made content able to name a product. Nothing could ask the
// directory anything, so an author had no way to choose one - which is what kept
// Composition.subject a string somebody typed.
public sealed class ProductEndpointTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private WebApplicationFactory<Program> Host() =>
        TestFixtures.Configured(factory, host => host.ConfigureTestServices(services =>
        {
            services.AddAuthentication(WhoeverAsked.Name)
                .AddScheme<AuthenticationSchemeOptions, WhoeverAsked>(WhoeverAsked.Name, _ => { });
            services.AddSingleton<IPolicyDecisionPoint>(new AlwaysAllow());
        }));

    private static HttpClient As(WebApplicationFactory<Program> host, string role = "author")
    {
        var client = host.CreateClient();
        client.DefaultRequestHeaders.Add(WhoeverAsked.RolesHeader, role);
        return client;
    }

    private sealed record ProductView(string Identifier, string Name, IReadOnlyList<string> Markets);

    [Fact]
    public async Task FN_MDM_002_products_can_be_searched_for_by_name()
    {
        // The operation the authoring surface needs, and the reason ADR-037 decision 3 says no
        // identifier is ever typed: an author picks a product and the platform writes its
        // identity.
        var found = await As(Host())
            .GetFromJsonAsync<IReadOnlyList<ProductView>>("/master-data/products?text=examplinum");

        Assert.NotEmpty(found!);
        Assert.All(found!, product => Assert.Contains(
            "Examplinum", product.Name, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task FN_MDM_002_every_product_comes_back_with_the_identity_content_will_carry()
    {
        var found = await As(Host())
            .GetFromJsonAsync<IReadOnlyList<ProductView>>("/master-data/products?text=examplinum");

        Assert.All(found!, product => Assert.False(string.IsNullOrWhiteSpace(product.Identifier)));
    }

    [Fact]
    public async Task FN_MDM_002_a_search_matching_nothing_is_empty_rather_than_not_found()
    {
        // An empty directory answer is an answer. 404 would say the directory is missing, which
        // is a different problem with a different remedy.
        using var response = await As(Host())
            .GetAsync("/master-data/products?text=nothing-matches-this");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Empty((await response.Content.ReadFromJsonAsync<IReadOnlyList<ProductView>>())!);
    }

    [Fact]
    public async Task FN_MDM_002_asking_for_nothing_in_particular_is_refused()
    {
        // A directory that answered everything to an empty query would be a way of enumerating
        // the product catalogue, which is not what a picker needs and not what this is for.
        using var response = await As(Host()).GetAsync("/master-data/products");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task FN_MDM_002_the_directory_is_refused_without_a_token()
    {
        using var response = await TestFixtures.Configured(factory).CreateClient()
            .GetAsync("/master-data/products?text=examplinum");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
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

        public const string RolesHeader = "X-Test-Roles";

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new ClaimsIdentity(
                [
                    new Claim(ClaimTypes.NameIdentifier, "user-anna"),
                    new Claim(SubjectFactory.RolesClaim,
                        Request.Headers[RolesHeader].FirstOrDefault() ?? "author"),
                    new Claim(SubjectFactory.AffiliatesClaim, "uk-affiliate"),
                    new Claim(SubjectFactory.MarketsClaim, "GB"),
                ],
                Name);

            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Name)));
        }
    }
}
