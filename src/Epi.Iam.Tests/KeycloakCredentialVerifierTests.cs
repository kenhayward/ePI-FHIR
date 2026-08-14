using System.Net;
using System.Text;
using Epi.Signature;
using Xunit;

namespace Epi.Iam.Tests;

// FN-AUD-005 Capture an electronic signature over the hash of the pinned version
//
// The credential half of ADR-020: the port that checks the two identification components at
// the signing gate, implemented against the identity provider the demonstration already runs.
// Everything else about signing is unchanged when this is swapped for PKI, which is the whole
// reason it is a port.
public sealed class KeycloakCredentialVerifierTests
{
    private const string TokenResponse = """
        {"access_token":"a-token","token_type":"Bearer","expires_in":300}
        """;

    private const string UserInfoResponse = """
        {"preferred_username":"user-anna","name":"Anna Novak","email":"anna@example.org"}
        """;

    private static KeycloakCredentialVerifier Verifier(
        RouteHandler handler, string realm = "epi") =>
        new(new HttpClient(handler) { BaseAddress = new Uri("http://keycloak.example.org") },
            realm, "epi-signing");

    [Fact]
    public async Task FN_AUD_005_correct_credentials_identify_the_signer()
    {
        var handler = new RouteHandler(TokenResponse, UserInfoResponse);

        var signer = await Verifier(handler).VerifyAsync("user-anna", "correct-horse");

        Assert.NotNull(signer);
        Assert.Equal("user-anna", signer!.Identifier);
        Assert.Equal("Anna Novak", signer.PrintedName);
    }

    [Fact]
    public async Task FN_AUD_005_the_printed_name_falls_back_to_the_username_when_the_provider_has_none()
    {
        // A signer with no profile name still has to be signable, and Section 11.50 wants a
        // printed name recorded. The username is a worse name than a real one and a much
        // better one than nothing.
        var handler = new RouteHandler(TokenResponse, """{"preferred_username":"user-ben"}""");

        var signer = await Verifier(handler).VerifyAsync("user-ben", "battery-staple");

        Assert.Equal("user-ben", signer!.PrintedName);
    }

    [Fact]
    public async Task FN_AUD_005_wrong_credentials_identify_nobody()
    {
        // Keycloak answers a bad password with 401 and invalid_grant. That is a refusal, not a
        // fault, and the service above turns it into the one refusal message it gives for
        // everything.
        var handler = new RouteHandler(
            """{"error":"invalid_grant","error_description":"Invalid user credentials"}""",
            UserInfoResponse,
            tokenStatus: HttpStatusCode.Unauthorized);

        Assert.Null(await Verifier(handler).VerifyAsync("user-anna", "not-annas-password"));
    }

    [Fact]
    public async Task FN_AUD_005_an_identity_provider_that_is_down_is_not_a_wrong_password()
    {
        // The distinction matters in the audit trail. Returning null here would record an
        // outage as an attempted unauthorised use, which is both wrong and the kind of thing
        // that sends someone looking for an intruder who was never there.
        var handler = new RouteHandler("""{"error":"server_error"}""", UserInfoResponse,
            tokenStatus: HttpStatusCode.InternalServerError);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => Verifier(handler).VerifyAsync("user-anna", "correct-horse"));
    }

    [Fact]
    public async Task FN_AUD_005_the_password_goes_to_the_identity_provider_and_nowhere_else()
    {
        var handler = new RouteHandler(TokenResponse, UserInfoResponse);

        await Verifier(handler).VerifyAsync("user-anna", "correct-horse");

        Assert.Contains("correct-horse", handler.TokenRequestBody!, StringComparison.Ordinal);
        Assert.DoesNotContain("correct-horse", handler.UserInfoRequestBody ?? "", StringComparison.Ordinal);
        Assert.DoesNotContain("correct-horse", handler.RequestedUris, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FN_AUD_005_the_configured_realm_is_the_one_asked()
    {
        // A verifier pointed at the wrong realm would authenticate against the wrong set of
        // people, and would look like it was working.
        var handler = new RouteHandler(TokenResponse, UserInfoResponse);

        await Verifier(handler, realm: "epi-production").VerifyAsync("user-anna", "correct-horse");

        Assert.Contains("/realms/epi-production/", handler.RequestedUris, StringComparison.Ordinal);
        Assert.DoesNotContain("/realms/master/", handler.RequestedUris, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FN_AUD_005_the_token_is_what_fetches_the_profile()
    {
        // The printed name has to come from the provider on the strength of the credentials
        // just proven, not from an unauthenticated lookup anyone could make.
        var handler = new RouteHandler(TokenResponse, UserInfoResponse);

        await Verifier(handler).VerifyAsync("user-anna", "correct-horse");

        Assert.Equal("Bearer a-token", handler.UserInfoAuthorization);
    }

    /// <summary>Answers the token endpoint and the userinfo endpoint differently.</summary>
    private sealed class RouteHandler(
        string tokenBody, string userInfoBody, HttpStatusCode tokenStatus = HttpStatusCode.OK)
        : HttpMessageHandler
    {
        public string RequestedUris { get; private set; } = "";

        public string? TokenRequestBody { get; private set; }

        public string? UserInfoRequestBody { get; private set; }

        public string? UserInfoAuthorization { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var uri = request.RequestUri!.ToString();
            RequestedUris += uri + "\n";
            var body = await (request.Content?.ReadAsStringAsync(cancellationToken)
                              ?? Task.FromResult(""));

            if (uri.Contains("/userinfo", StringComparison.Ordinal))
            {
                UserInfoRequestBody = body;
                UserInfoAuthorization = request.Headers.Authorization?.ToString();
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(userInfoBody, Encoding.UTF8, "application/json"),
                };
            }

            TokenRequestBody = body;
            return new HttpResponseMessage(tokenStatus)
            {
                Content = new StringContent(tokenBody, Encoding.UTF8, "application/json"),
            };
        }
    }
}
