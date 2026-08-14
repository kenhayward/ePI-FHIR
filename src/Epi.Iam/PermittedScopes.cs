using Epi.ContentCore;

namespace Epi.Iam;

/// <summary>
/// Which affiliate and market combinations a caller may act within (FN-IAM-005, CAP-SCH-004).
/// </summary>
/// <remarks>
/// The read path decides one document at a time, because a read names one document. A query
/// names none, so something has to bound the search before any result exists to decide about -
/// and bounding it afterwards leaks through counts and paging (ADR-022 decision 1).
/// </remarks>
public interface IPermittedScopes
{
    Task<IReadOnlyCollection<DocumentScope>> ForAsync(
        Subject subject, string action, CancellationToken cancellationToken = default);
}

/// <summary>
/// Resolves permitted scopes by asking the policy decision point, one candidate at a time
/// (ADR-022 decisions 4 and 5).
/// </summary>
/// <remarks>
/// The candidates are the cross product of the affiliates and markets the caller's identity
/// asserts, and the policy is asked about each. Deriving the answer in code instead would be a
/// second implementation of <c>scope_covers_resource</c> living where no Rego test can reach
/// it, and two implementations of one authorisation rule drift - the one that drifts unnoticed
/// being the one that over-returns.
/// <para>
/// The enumeration assumes a policy that narrows the caller's asserted scope rather than
/// widening it. A rule granting sight of everything to an inspector would not be found here,
/// and that caller would see nothing rather than everything: the safe direction, and the fix is
/// to widen the candidate source rather than to loosen the predicate.
/// </para>
/// </remarks>
public sealed class PolicyPermittedScopes(IPolicyDecisionPoint policy) : IPermittedScopes
{
    private readonly IPolicyDecisionPoint _policy =
        policy ?? throw new ArgumentNullException(nameof(policy));

    public async Task<IReadOnlyCollection<DocumentScope>> ForAsync(
        Subject subject, string action, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(subject);
        ArgumentException.ThrowIfNullOrWhiteSpace(action);

        var permitted = new List<DocumentScope>();

        foreach (var affiliate in subject.Affiliates)
        {
            foreach (var market in subject.Markets)
            {
                var decision = await _policy.DecideAsync(
                    new AuthorizationQuery(subject, action, new ResourceScope(affiliate, market)),
                    cancellationToken);

                if (decision.Allowed)
                {
                    permitted.Add(new DocumentScope(affiliate, market));
                }
            }
        }

        return permitted;
    }
}
