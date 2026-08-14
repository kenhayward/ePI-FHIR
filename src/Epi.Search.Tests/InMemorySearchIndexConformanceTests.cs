namespace Epi.Search.Tests;

/// <summary>The in-memory index, held to the contract every search implementation must meet.</summary>
/// <remarks>
/// In its own file for the same reason the store conformance subclasses are: the suite is
/// shared source, and declaring the subclass beside it would run the in-memory cases again in
/// any project that compiles the suite against something else.
/// </remarks>
public sealed class InMemorySearchIndexConformanceTests : LabelSearchConformance
{
    protected override Task<(ISearchProjection Projection, ILabelSearch Search)> CreateAsync()
    {
        var index = new InMemorySearchIndex();
        return Task.FromResult<(ISearchProjection, ILabelSearch)>((index, index));
    }
}
