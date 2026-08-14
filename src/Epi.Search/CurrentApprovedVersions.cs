using Epi.ContentCore;
using Epi.Lifecycle;

namespace Epi.Search;

/// <summary>
/// Resolves the version of a label a market currently has approved (CAP-SCH-002, FN-SCH-002).
/// </summary>
/// <remarks>
/// Two things make this more than a query. It is answered from <em>per-market</em> approval
/// state, never from internal lifecycle state and never from a field on the content: a version
/// approved in Great Britain and under assessment in the European Union is the normal case
/// (ADR-005). And the state is read from the store that owns it rather than from the
/// projection, because state changes and a projection can lag - search finds the candidates,
/// the owner of the fact answers for it (ADR-022 decision 8).
/// </remarks>
public sealed class CurrentApprovedVersions(
    ILabelSearch search, IMarketApprovalStore approvals, string approvedState)
{
    /// <summary>
    /// The approved version of this document in this market, or null where the market has
    /// approved none - which is also the answer when the caller may not see the document at
    /// all, so that search cannot be used to discover that it exists (CAP-SCH-004).
    /// </summary>
    public Task<SearchHit?> ForAsync(
        string documentIdentifier,
        string market,
        IReadOnlyCollection<DocumentScope> permittedScopes,
        CancellationToken cancellationToken = default)
    {
        _ = (search, approvals, approvedState);
        throw new NotImplementedException();
    }
}
