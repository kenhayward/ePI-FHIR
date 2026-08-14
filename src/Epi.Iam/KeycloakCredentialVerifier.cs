using System.Net;
using System.Text.Json;
using System.Text.Json.Serialization;
using Epi.Signature;

namespace Epi.Iam;

/// <summary>
/// Checks a signer's identifier and password against Keycloak (ADR-020 decisions 1 and 2).
/// </summary>
/// <remarks>
/// <para>
/// Uses the OAuth 2.0 resource owner password credentials grant, which is precisely the thing
/// that grant is unsuitable for in most applications and suitable for here: the point is to
/// prove that the person at the keyboard knows the password *now*, rather than to obtain a
/// token to act with. The token is used once, to read the signer's profile, and then discarded.
/// </para>
/// <para>
/// Production is expected to implement this port against PKI instead. Nothing else about
/// signing changes when it does - not the manifest, not the hash, not the gate.
/// </para>
/// </remarks>
public sealed class KeycloakCredentialVerifier(HttpClient client, string realm, string clientId)
    : ICredentialVerifier
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _client = client ?? throw new ArgumentNullException(nameof(client));

    private readonly string _realm = string.IsNullOrWhiteSpace(realm)
        ? throw new ArgumentException("A realm is required.", nameof(realm))
        : realm;

    private readonly string _clientId = string.IsNullOrWhiteSpace(clientId)
        ? throw new ArgumentException("A client identifier is required.", nameof(clientId))
        : clientId;

    public async Task<SignerIdentity?> VerifyAsync(
        string identifier, string password, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        using var request = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["grant_type"] = "password",
            ["client_id"] = _clientId,
            ["username"] = identifier,
            ["password"] = password,
            ["scope"] = "openid profile",
        });

        using var response = await _client.PostAsync(
            $"/realms/{_realm}/protocol/openid-connect/token", request, cancellationToken);

        // A rejected credential is a refusal, and the caller turns it into the single message
        // it gives for every refusal. Anything else is a fault, and must not be mistaken for a
        // wrong password: an outage recorded as an attempted unauthorised use would send
        // someone looking for an intruder who was never there.
        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest)
        {
            return null;
        }

        response.EnsureSuccessStatusCode();

        var token = JsonSerializer.Deserialize<TokenResponse>(
            await response.Content.ReadAsStringAsync(cancellationToken), Json);

        if (string.IsNullOrWhiteSpace(token?.AccessToken))
        {
            throw new HttpRequestException(
                "The identity provider accepted the credentials but returned no access token.");
        }

        return await ProfileAsync(token.AccessToken, cancellationToken);
    }

    /// <summary>
    /// The signer as the provider describes them, read with the token those credentials just
    /// earned rather than by an unauthenticated lookup anyone could make.
    /// </summary>
    private async Task<SignerIdentity?> ProfileAsync(string accessToken, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Get, $"/realms/{_realm}/protocol/openid-connect/userinfo");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue(
            "Bearer", accessToken);

        using var response = await _client.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var profile = JsonSerializer.Deserialize<UserInfo>(
            await response.Content.ReadAsStringAsync(cancellationToken), Json);

        if (string.IsNullOrWhiteSpace(profile?.PreferredUsername))
        {
            throw new HttpRequestException(
                "The identity provider returned a profile with no username to attribute the "
                + "signature to.");
        }

        // Section 11.50 wants a printed name recorded. A username is a worse name than a real
        // one and a far better one than nothing.
        return new SignerIdentity(
            profile.PreferredUsername,
            string.IsNullOrWhiteSpace(profile.Name) ? profile.PreferredUsername : profile.Name);
    }

    // Named explicitly rather than left to a naming policy. These are OAuth 2.0 and OIDC
    // field names, fixed by specification in snake_case, and the web defaults expect camelCase -
    // so a policy would silently deserialise every field to null and report a provider that
    // returned nothing useful.
    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken);

    private sealed record UserInfo(
        [property: JsonPropertyName("preferred_username")] string? PreferredUsername,
        [property: JsonPropertyName("name")] string? Name);
}
