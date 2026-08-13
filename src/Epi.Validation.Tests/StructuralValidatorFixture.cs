namespace Epi.Validation.Tests;

/// <summary>
/// Builds the validator once per test class. Loading the pinned packages and the core
/// definitions takes a moment, and it is the same work for every test.
/// </summary>
public sealed class StructuralValidatorFixture : IDisposable
{
    public StructuralValidator Validator { get; } = new(ProfileSource.FromPinnedPackages());

    public void Dispose() { }
}
