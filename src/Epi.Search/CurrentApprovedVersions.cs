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
    private readonly ILabelSearch _search = search ?? throw new ArgumentNullException(nameof(search));

    private readonly IMarketApprovalStore _approvals =
        approvals ?? throw new ArgumentNullException(nameof(approvals));

    /// <summary>
    /// Which state means approved. Required rather than defaulted: a resolver guessing at
    /// "approved" would answer confidently and wrongly for any organisation that spells it
    /// otherwise (ADR-022 decision 7).
    /// </summary>
    private readonly string _approvedState = string.IsNullOrWhiteSpace(approvedState)
        ? throw new ArgumentException(
            "The state that means approved must be named, or no version could ever be found to "
            + "be the current-approved one.",
            nameof(approvedState))
        : approvedState;

    /// <summary>
    /// The approved version of this document in this market, or null where the market has
    /// approved none - which is also the answer when the caller may not see the document at
    /// all, so that search cannot be used to discover that it exists (CAP-SCH-004).
    /// </summary>
    public async Task<SearchHit?> ForAsync(
        string documentIdentifier,
        string market,
        IReadOnlyCollection<DocumentScope> permittedScopes,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(documentIdentifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(market);
        ArgumentNullException.ThrowIfNull(permittedScopes);

        // The candidates come from search, so the caller's scope bounds this exactly as it
        // bounds any other query, and a document outside it produces no candidates at all.
        var candidates = await _search.SearchAsync(
            new ScopedSearchQuery(
                new SearchCriteria(Identifier: documentIdentifier, PageSize: SearchCriteria.MaximumPageSize),
                permittedScopes),
            cancellationToken);

        // Newest first, so the most recently approved version wins where a market has approved
        // several - the version in force is the last one the regulator agreed to.
        foreach (var candidate in candidates.Hits.OrderByDescending(hit => hit.Version))
        {
            var state = await _approvals.CurrentStateAsync(
                new MarketVersion(new VersionRef(documentIdentifier, candidate.Version), market),
                cancellationToken);

            if (string.Equals(state, _approvedState, StringComparison.Ordinal))
            {
                return candidate;
            }
        }

        return null;
    }
}
