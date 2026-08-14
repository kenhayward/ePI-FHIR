using Hl7.Fhir.Model;

namespace Epi.Signature;

/// <summary>
/// The hash a signature is made over (ADR-020 decisions 4 and 5).
/// </summary>
public static class ContentHash
{
    /// <summary>The hash of a version's canonical content, prefixed with the algorithm used.</summary>
    public static string Of(Bundle bundle) => throw new NotImplementedException();
}
