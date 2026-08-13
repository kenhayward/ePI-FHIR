namespace Epi.ContentCore;

/// <summary>
/// The identity of an ePI document: a business identifier the platform mints, expressed as a
/// system and value pair (ADR-015). Deliberately not the FHIR server's logical id, which is
/// server-assigned and would not survive a change of FHIR server (ADR-003).
/// </summary>
public sealed record DocumentIdentity(string System, string Value)
{
    public override string ToString() => $"{System}|{Value}";
}
