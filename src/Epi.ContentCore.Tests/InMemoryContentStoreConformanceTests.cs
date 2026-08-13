using Xunit;

namespace Epi.ContentCore.Tests;

/// <summary>The in-memory store, held to the shared content-store contract.</summary>
public sealed class InMemoryContentStoreConformanceTests : ContentStoreConformance
{
    protected override IContentStore CreateStore() => new InMemoryContentStore();
}
