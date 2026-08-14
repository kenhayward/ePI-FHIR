namespace Epi.Lifecycle;

/// <summary>One version as one market sees it - the subject of a regulatory-approval state.</summary>
public sealed record MarketVersion(VersionRef Version, string Market)
{
    public override string ToString() => $"{Version}/{Market}";
}

/// <summary>
/// One move in a market's regulatory-approval state, recorded rather than applied.
/// </summary>
/// <remarks>
/// Carries no signature reference. A regulatory-approval transition records what a regulator
/// decided, not an assertion by the person entering it, so the Part 11 signature that gates
/// internal approval is not the same control - see the guard in
/// <see cref="MarketApprovalService"/> against a market model that gates on a signature nothing
/// here would check.
/// </remarks>
public sealed record MarketStateTransition(
    MarketVersion Subject,
    string From,
    string To,
    string Action,
    string Actor,
    DateTimeOffset At,
    string? Reason = null);

/// <summary>
/// Per-market regulatory-approval state, held separately from internal lifecycle state
/// (ADR-005, ADR-019 decision 2). No update and no delete, as everywhere else.
/// </summary>
public interface IMarketApprovalStore
{
    /// <summary>
    /// The state this version holds in this market, or null if it has never moved - in which
    /// case it is at the model's initial state, which is where every version starts in every
    /// market without anything being written.
    /// </summary>
    Task<string?> CurrentStateAsync(MarketVersion subject, CancellationToken cancellationToken = default);

    /// <summary>Every transition this version has been through in this market, oldest first.</summary>
    Task<IReadOnlyList<MarketStateTransition>> HistoryAsync(
        MarketVersion subject, CancellationToken cancellationToken = default);

    /// <summary>The state of this version in every market it has moved in.</summary>
    Task<IReadOnlyDictionary<string, string>> StatesForAsync(
        VersionRef version, CancellationToken cancellationToken = default);

    Task AppendAsync(MarketStateTransition transition, CancellationToken cancellationToken = default);
}
