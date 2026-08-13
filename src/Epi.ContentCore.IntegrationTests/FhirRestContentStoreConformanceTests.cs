using Epi.ContentCore.Tests;
using Xunit;

namespace Epi.ContentCore.IntegrationTests;

/// <summary>
/// The FHIR REST adapter, held to exactly the same contract as the in-memory store
/// (FN-CC-004, and IT-001 and IT-006 against a real server rather than a fake).
/// </summary>
/// <remarks>
/// Inheriting the shared suite is the point: two implementations of one interface should not
/// have two sets of assertions, because that is how they drift into behaving differently and
/// nobody notices until content is already stored.
/// </remarks>
[Collection(HapiCollection.Name)]
[Trait("Category", "Container")]
public sealed class FhirRestContentStoreConformanceTests(HapiFhirServer server) : ContentStoreConformance
{
    protected override IContentStore CreateStore() => new FhirRestContentStore(server.CreateClient());
}
