namespace Epi.Iam;

/// <summary>What the subject wants to do, to what (FN-IAM-002).</summary>
/// <remarks>
/// The shape is the policy's input contract (policies/authz). Keeping it in one type means the
/// platform and the policy cannot drift apart silently: a change here is a change to a
/// reviewed, tested Rego contract.
/// </remarks>
public sealed record AuthorizationQuery(Subject Subject, string Action, ResourceScope Resource);

/// <summary>The attributes a decision is made against (CAP-IAM-003).</summary>
public sealed record ResourceScope(string Affiliate, string Market, string? Author = null)
{
    /// <summary>
    /// A resource with no affiliate and no market, for an action that has none to compare
    /// against (FN-LCM-008).
    /// </summary>
    /// <remarks>
    /// Named rather than written as a pair of empty strings at each call site, so what it means
    /// is stated once: not "any affiliate" and not "the affiliate is empty", but a subject
    /// matter that has no affiliate at all. Reconciliation is the case that needs it - it
    /// reports governance records with no content behind them, and scope is decided on the
    /// content (ADR-025), so a scoped version would return nothing for exactly the reason the
    /// record is worth reporting. The policy decides these on the role alone, and the set of
    /// actions that may be decided that way is enumerated in the policy, not here.
    /// </remarks>
    public static ResourceScope PlatformWide { get; } = new(string.Empty, string.Empty);
}

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
