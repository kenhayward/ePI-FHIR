namespace Epi.Signature.Tests;

/// <summary>The in-memory signature store, held to the contract every one must meet.</summary>
/// <remarks>
/// In its own file because <see cref="SignatureStoreConformance"/> is compiled into the
/// integration test project as shared source, and declaring this subclass beside it would run
/// the in-memory cases again there.
/// </remarks>
public sealed class InMemorySignatureStoreConformanceTests : SignatureStoreConformance
{
    protected override Task<ISignatureStore> CreateStoreAsync() =>
        Task.FromResult<ISignatureStore>(new InMemorySignatureStore());
}
