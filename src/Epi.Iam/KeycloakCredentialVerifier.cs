using Epi.Signature;

namespace Epi.Iam;

/// <summary>
/// Checks a signer's identifier and password against Keycloak (ADR-020 decisions 1 and 2).
/// </summary>
public sealed class KeycloakCredentialVerifier(HttpClient client, string realm, string clientId)
    : ICredentialVerifier
{
    private readonly HttpClient _client = client;
    private readonly string _realm = realm;
    private readonly string _clientId = clientId;

    public Task<SignerIdentity?> VerifyAsync(
        string identifier, string password, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
