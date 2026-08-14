using Epi.ContentCore;

namespace Epi.Search;

/// <summary>
/// What a caller is looking for (CAP-SCH-001, CAP-SCH-003). Every field is optional; a query
/// with none is "everything I may see", which is a legitimate first screen.
/// </summary>
/// <param name="Text">Free text matched against title and section narrative.</param>
/// <param name="Product">The product the label is about, as the content names its subject.</param>
/// <param name="Market">The market the content belongs to, within those the caller may see.</param>
/// <param name="Language">The language the content is written in.</param>
/// <param name="State">The internal lifecycle state, as spelled by the state model.</param>
/// <param name="Identifier">A specific document identifier.</param>
/// <remarks>
/// Effective date is a CAP-SCH-001 parameter and is deliberately absent: effective dating does
/// not exist yet, and a parameter that silently matches nothing is worse than one that is
/// honestly missing.
/// </remarks>
public sealed record SearchCriteria(
    string? Text = null,
    string? Product = null,
    string? Market = null,
    string? Language = null,
    string? State = null,
    string? Identifier = null,
    int Page = 1,
    int PageSize = SearchCriteria.DefaultPageSize)
{
    public const int DefaultPageSize = 20;

    /// <summary>
    /// The largest page anyone may ask for (CAP-SCH-006). An unbounded query against a
    /// regulated corpus is an outage waiting for its first large tenant.
    /// </summary>
    public const int MaximumPageSize = 100;

    /// <summary>Pages are one-based; anything less is the first page.</summary>
    public int Page { get; init; } = Page < 1 ? 1 : Page;

    /// <summary>
    /// Clamped rather than refused. A caller asking for more than the platform will serve has
    /// made no error worth failing a request over, and gets what the platform will serve.
    /// </summary>
    public int PageSize { get; init; } = PageSize switch
    {
        < 1 => DefaultPageSize,
        > MaximumPageSize => MaximumPageSize,
        _ => PageSize,
    };
}

/// <summary>
/// A query and the scopes it may run within - the only form of query the platform can execute
/// (ADR-022 decisions 1 and 2).
/// </summary>
/// <remarks>
/// There is no unscoped overload and no flag that disables scoping. Search that could be run
/// without a permitted-scope set would eventually be run without one, and the failure would be
/// silent: the results would look right to everyone except the person they were leaked to.
/// </remarks>
public sealed record ScopedSearchQuery(
    SearchCriteria Criteria, IReadOnlyCollection<DocumentScope> PermittedScopes)
{
    public SearchCriteria Criteria { get; } =
        Criteria ?? throw new ArgumentNullException(nameof(Criteria));

    /// <summary>
    /// The scopes the caller may read within. Empty is meaningful and means nothing is
    /// visible - never "no restriction" (ADR-022 decision 3).
    /// </summary>
    public IReadOnlyCollection<DocumentScope> PermittedScopes { get; } =
        PermittedScopes ?? throw new ArgumentNullException(nameof(PermittedScopes));
}
