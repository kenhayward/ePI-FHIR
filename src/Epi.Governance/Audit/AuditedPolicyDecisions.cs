namespace Epi.Governance.Audit;

/// <summary>
/// Records every access-control decision (CAP-IAM-009, FN-AUD-004).
/// </summary>
/// <remarks>
/// Implemented as a delegating decision point so it cannot be skipped, and it records
/// <em>allow</em> as well as <em>deny</em>. CAP-IAM-009 asks for all access decisions: a trail
/// of refusals alone shows attacks but not misuse by someone entitled to be there, which is
/// the harder case in a regulated system.
/// </remarks>
public sealed class AuditedPolicyDecisions<TQuery, TDecision>(
    Func<TQuery, CancellationToken, Task<TDecision>> decide,
    IAuditSink audit,
    Func<TQuery, string> describeActor,
    Func<TQuery, string> describeAction,
    Func<TQuery, string> describeTarget,
    Func<TDecision, bool> isAllowed,
    Func<TDecision, string> describeReason)
{
    public async Task<TDecision> DecideAsync(TQuery query, CancellationToken cancellationToken = default)
    {
        var decision = await decide(query, cancellationToken);

        await audit.AppendAsync(new AuditRecord(
            describeActor(query),
            $"access.{describeAction(query)}",
            describeTarget(query),
            isAllowed(decision) ? AuditOutcome.Succeeded : AuditOutcome.Denied,
            default,
            Reason: describeReason(decision)), cancellationToken);

        return decision;
    }
}
