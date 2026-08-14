using Epi.ContentCore.Tests;
using Hl7.Fhir.Model;
using Xunit;

namespace Epi.ContentCore.IntegrationTests;

/// <summary>
/// Assertions that only make sense against a real server, and that the shared conformance
/// suite deliberately cannot make: the suite passes against an in-memory store, so on its own
/// it cannot tell "stored" from "stored on the FHIR server".
/// </summary>
[Collection(HapiCollection.Name)]
[Trait("Category", "Container")]
public sealed class FhirPersistenceTests(HapiFhirServer server)
{
    private static Bundle MinimalDocument() =>
        EpiBundleReader.Read(File.ReadAllText(TestFixtures.Path("epi", "minimal-epi-document.json")));

    [Fact]
    public async Task FN_CC_004_content_persists_on_the_server_and_is_readable_by_a_new_client()
    {
        // Write with one client, read with another. Nothing in the process can be holding the
        // content: if this passes, it crossed the network and came back.
        var writer = new FhirRestContentStore(server.CreateClient());
        var stored = await writer.CreateAsync(ContentIdentity.Mint(), MinimalDocument());

        var reader = new FhirRestContentStore(server.CreateClient());
        var retrieved = await reader.GetAsync(stored.Identity, stored.Version);

        Assert.NotNull(retrieved);
        Assert.Equal(stored.Identity, retrieved!.Identity);
        Assert.Equal("SYNTHETIC TEST LABEL - Examplinum 10 mg film-coated tablets",
            Assert.IsType<Composition>(retrieved.Bundle.Entry[0].Resource).Title);
    }

    [Fact]
    public async Task FN_CC_004_the_server_assigns_its_own_identifiers_which_we_do_not_use_as_identity()
    {
        // ADR-015 in practice: the server gives the resource a logical id and its own version,
        // and our identity is neither of them. This is what keeps ADR-003 reversible.
        var store = new FhirRestContentStore(server.CreateClient());

        var stored = await store.CreateAsync(ContentIdentity.Mint(), MinimalDocument());

        Assert.NotNull(stored.Bundle.Id);
        Assert.NotEqual(stored.Bundle.Id, stored.Identity.Value);
        Assert.Equal(ContentCoreDefaults.DocumentIdentifierSystem, stored.Identity.System);
    }

    [Fact]
    public async Task FN_CC_004_two_versions_are_two_resources_on_the_server_not_an_overwrite()
    {
        // A correction must never destroy what it corrects (CAP-LCM-002, CAP-LCM-006).
        var store = new FhirRestContentStore(server.CreateClient());
        var first = await store.CreateAsync(ContentIdentity.Mint(), MinimalDocument());

        var amended = MinimalDocument();
        Assert.IsType<Composition>(amended.Entry[0].Resource).Title = "SECOND VERSION";
        var second = await store.CreateVersionAsync(first.Identity, 2, amended);

        Assert.NotEqual(first.Bundle.Id, second.Bundle.Id);
        Assert.Equal([1, 2], await store.VersionsAsync(first.Identity));
    }
}
