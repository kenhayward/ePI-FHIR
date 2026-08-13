namespace Epi.Governance.Configuration;

/// <summary>
/// One market the platform operates in: a regulator, the languages its labels are authored in,
/// and the affiliates scoped to it (capability 21; the scoping attributes are consumed by
/// capability 17 authorisation).
/// </summary>
/// <remarks>
/// The binding to a conformance profile package and version (ADR-016) is deliberately absent
/// until the exact published IG release is confirmed. See ADR-016 open points.
/// </remarks>
public sealed record MarketDefinition(
    string Code,
    string Name,
    string Regulator,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Affiliates);
