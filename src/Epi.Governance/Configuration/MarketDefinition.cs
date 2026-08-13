namespace Epi.Governance.Configuration;

/// <summary>
/// One market the platform operates in: a regulator, the languages its labels are authored in,
/// the affiliates scoped to it (capability 21; the scoping attributes are consumed by
/// capability 17 authorisation), and the conformance profile its content is validated against.
/// </summary>
public sealed record MarketDefinition(
    string Code,
    string Name,
    string Regulator,
    IReadOnlyList<string> Languages,
    IReadOnlyList<string> Affiliates,
    ProfileBinding Profile);

/// <summary>
/// The conformance package a market's content is validated against (ADR-016 decision 7).
/// A market names its own package and version so it can adopt a release on its own timetable,
/// without a platform release.
/// </summary>
public sealed record ProfileBinding(string Package, string Version);
