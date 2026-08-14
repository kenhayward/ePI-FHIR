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
/// Signed on submission and unsigned on recording a decision (CAP-LCM-012). Submitting to a
/// regulator is an act of the organisation by an accountable person; recording what the regulator
/// then decided is a factual entry about an event outside the organisation's control, where the
/// person entering it asserts only that they transcribed it correctly. Both are audited; the
/// signature is what distinguishes them.
/// </remarks>
public sealed record MarketStateTransition(
    MarketVersion Subject,
    string From,
    string To,
    string Action,
    string Actor,
    DateTimeOffset At,
    string? Reason = null,
    string? SignatureReference = null);

/// <summary>
/// Per-market regulatory-approval state, held separately from internal lifecycle state
/// (ADR-005, ADR-019 decision 2). No update and no delete, as everywhere else.
/// </summary>
public interface IMarketApprovalStore : ISpentSignatures
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
