using Hl7.Fhir.Specification.Source;

namespace Epi.Validation;

/// <summary>
/// Resolves conformance resources from the pinned, vendored packages and the core R5
/// definitions - offline, always (ADR-016 decision 3).
/// </summary>
public static class ProfileSource
{
    public static IAsyncResourceResolver FromPinnedPackages() => throw new NotImplementedException();
}
