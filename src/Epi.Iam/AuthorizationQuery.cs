namespace Epi.Iam;

/// <summary>What the subject wants to do, to what (FN-IAM-002).</summary>
/// <remarks>
/// The shape is the policy's input contract (policies/authz). Keeping it in one type means the
/// platform and the policy cannot drift apart silently: a change here is a change to a
/// reviewed, tested Rego contract.
/// </remarks>
public sealed record AuthorizationQuery(Subject Subject, string Action, ResourceScope Resource);

/// <summary>The attributes a decision is made against (CAP-IAM-003).</summary>
public sealed record ResourceScope(string Affiliate, string Market, string? Author = null);

/// <summary>The policy's answer, with the reason it gave.</summary>
public sealed record AuthorizationDecision(bool Allowed, string Reason)
{
    public static AuthorizationDecision Deny(string reason) => new(false, reason);
}

/// <summary>The policy decision point (CAP-IAM-008, ADR-012).</summary>
public interface IPolicyDecisionPoint
{
    Task<AuthorizationDecision> DecideAsync(AuthorizationQuery query, CancellationToken cancellationToken = default);
}
