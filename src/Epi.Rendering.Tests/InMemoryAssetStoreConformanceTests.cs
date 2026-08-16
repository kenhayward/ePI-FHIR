namespace Epi.Rendering.Tests;

/// <summary>The in-memory store, held to the contract every asset store must meet.</summary>
/// <remarks>
/// Kept apart from the suite itself so the suite can be shared as source with the integration
/// tests without dragging this along: the durable store answers the same questions, and running
/// the reference implementation twice would only make the run longer.
/// </remarks>
public sealed class InMemoryAssetStoreConformanceTests : AssetStoreConformance
{
    protected override Task<IAssetStore> CreateStoreAsync() =>
        Task.FromResult<IAssetStore>(new InMemoryAssetStore());
}
