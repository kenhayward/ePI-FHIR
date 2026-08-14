namespace Epi.Lifecycle.Tests;

/// <summary>The in-memory lifecycle store, held to the contract every one must meet.</summary>
/// <remarks>
/// In its own file because the conformance suites are compiled into the integration test
/// project as shared source. Declaring these subclasses alongside them would run the in-memory
/// cases again there, in a project whose whole purpose is to exercise a real database.
/// </remarks>
public sealed class InMemoryLifecycleStoreConformanceTests : LifecycleStoreConformance
{
    protected override Task<ILifecycleStore> CreateStoreAsync() =>
        Task.FromResult<ILifecycleStore>(new InMemoryLifecycleStore());
}

/// <summary>The in-memory market approval store, held to the same contract.</summary>
public sealed class InMemoryMarketApprovalStoreConformanceTests : MarketApprovalStoreConformance
{
    protected override Task<IMarketApprovalStore> CreateStoreAsync() =>
        Task.FromResult<IMarketApprovalStore>(new InMemoryMarketApprovalStore());
}
