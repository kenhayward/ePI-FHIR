using Epi.Governance.Audit;

namespace Epi.Governance.Tests;

/// <summary>The in-memory sink, held to the contract every sink must meet.</summary>
/// <remarks>
/// In its own file because <see cref="AuditSinkConformance"/> is compiled into the integration
/// test project as shared source. Sharing a file that also declared this class would run the
/// in-memory cases twice, in a project whose whole purpose is to exercise a real database.
/// </remarks>
public sealed class InMemoryAuditSinkConformanceTests : AuditSinkConformance
{
    protected override Task<IAuditSink> CreateSinkAsync(TimeProvider? time = null) =>
        Task.FromResult<IAuditSink>(new InMemoryAuditSink(time));
}
