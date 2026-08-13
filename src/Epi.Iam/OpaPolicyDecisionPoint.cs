using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Epi.Iam;

/// <summary>
/// Asks Open Policy Agent (ADR-012). Authorisation lives in reviewed, tested policy rather
/// than in application code, so a new market or rule is a policy change (capability 21).
/// </summary>
public sealed class OpaPolicyDecisionPoint(HttpClient client, string? decisionPath = null)
    : IPolicyDecisionPoint
{
    /// <summary>The decision rule in policies/authz/example.rego.</summary>
    public const string DefaultDecisionPath = "v1/data/epi/authz/decision";

    private readonly string _decisionPath = decisionPath ?? DefaultDecisionPath;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task<AuthorizationDecision> DecideAsync(
        AuthorizationQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);

        try
        {
            var response = await client.PostAsJsonAsync(_decisionPath, new OpaRequest(Input.From(query)), Json, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return AuthorizationDecision.Deny(
                    $"The policy decision point answered {(int)response.StatusCode}; denying.");
            }

            var body = await response.Content.ReadFromJsonAsync<OpaResponse>(Json, cancellationToken);
            return body?.Result switch
            {
                "allow" => new AuthorizationDecision(true, "Allowed by policy."),
                "deny" => AuthorizationDecision.Deny("Denied by policy."),
                // An undefined rule yields no result. Treating that as anything but a denial
                // would turn a gap in policy into open access.
                _ => AuthorizationDecision.Deny("The policy returned no decision; denying."),
            };
        }
        catch (Exception error) when (error is HttpRequestException or TaskCanceledException or JsonException)
        {
            // Fail closed: an authorisation service that opens the door when its policy engine
            // is unreachable is worse than none, because it looks like one.
            return AuthorizationDecision.Deny($"The policy decision point was unreachable; denying. {error.Message}");
        }
    }

    private sealed record OpaRequest([property: JsonPropertyName("input")] Input Input);

    private sealed record OpaResponse([property: JsonPropertyName("result")] string? Result);

    /// <summary>The policy's input contract; see policies/authz/example.rego.</summary>
    private sealed record Input(SubjectInput Subject, string Action, ResourceInput Resource)
    {
        public static Input From(AuthorizationQuery query) => new(
            new SubjectInput(query.Subject.Id, query.Subject.Roles,
                new ScopeInput(query.Subject.Affiliates, query.Subject.Markets)),
            query.Action,
            new ResourceInput(query.Resource.Affiliate, query.Resource.Market, query.Resource.Author));
    }

    private sealed record SubjectInput(string Id, IReadOnlyList<string> Roles, ScopeInput Scope);

    private sealed record ScopeInput(IReadOnlyList<string> Affiliates, IReadOnlyList<string> Markets);

    private sealed record ResourceInput(string Affiliate, string Market, string? Author);
}
