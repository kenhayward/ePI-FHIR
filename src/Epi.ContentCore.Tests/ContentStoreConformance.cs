using Epi.ContentCore;
using Hl7.Fhir.Model;
using Xunit;

namespace Epi.ContentCore.Tests;

/// <summary>
/// The behaviour every content store must exhibit, whatever backs it. Run here against the
/// in-memory store; the FHIR REST adapter inherits this same suite when it lands, so the
/// two implementations are held to one contract rather than two sets of assertions.
/// </summary>
/// <remarks>
/// Covers FN-CC-002 (assign a canonical identifier), FN-CC-003 (immutable version snapshot
/// and lineage), FN-CC-005 (retrieve), FN-CC-007 (reject mutation), and the integration
/// scenarios IT-001 and IT-006. Persisting through the FHIR REST API is a separate design
/// function and arrives with the adapter that implements it.
/// </remarks>
public abstract class ContentStoreConformance
{
    protected abstract IContentStore CreateStore();

    private static Bundle MinimalDocument() =>
        EpiBundleReader.Read(File.ReadAllText(TestFixtures.Path("epi", "minimal-epi-document.json")));

    /// <summary>The anchoring Composition, asserted present rather than assumed.</summary>
    private static Composition CompositionOf(Bundle bundle) =>
        Assert.IsType<Composition>(bundle.Entry[0].Resource);

    [Fact]
    public async Task FN_CC_002_assigns_a_canonical_identifier_the_caller_did_not_supply()
    {
        var store = CreateStore();

        var stored = await store.CreateAsync(MinimalDocument());

        Assert.False(string.IsNullOrWhiteSpace(stored.Identity.Value));
        Assert.False(string.IsNullOrWhiteSpace(stored.Identity.System));
        Assert.True(Guid.TryParse(stored.Identity.Value, out _),
            "The identifier should be an opaque UUID (ADR-015), not a derived string.");
    }

    [Fact]
    public async Task FN_CC_002_mints_a_distinct_identifier_for_every_document()
    {
        var store = CreateStore();

        var first = await store.CreateAsync(MinimalDocument());
        var second = await store.CreateAsync(MinimalDocument());

        Assert.NotEqual(first.Identity, second.Identity);
    }

    [Fact]
    public async Task FN_CC_002_encodes_no_business_meaning_in_the_identifier()
    {
        // ADR-015: product, market, and language are searchable metadata, never identifier
        // substrings, because every one of them is mutable.
        var store = CreateStore();

        var stored = await store.CreateAsync(MinimalDocument());

        Assert.DoesNotContain("Examplinum", stored.Identity.Value, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("leaflet", stored.Identity.Value, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FN_CC_003_starts_at_version_one_and_increments_monotonically()
    {
        var store = CreateStore();

        var first = await store.CreateAsync(MinimalDocument());
        var second = await store.CreateVersionAsync(first.Identity, MinimalDocument());

        Assert.Equal(1, first.Version);
        Assert.Equal(2, second.Version);
        Assert.Equal([1, 2], await store.VersionsAsync(first.Identity));
    }

    [Fact]
    public async Task FN_CC_003_records_the_identifier_on_the_stored_bundle()
    {
        var store = CreateStore();

        var stored = await store.CreateAsync(MinimalDocument());

        var identifier = stored.Bundle.Identifier;
        Assert.NotNull(identifier);
        Assert.Equal(stored.Identity.System, identifier!.System);
        Assert.Equal(stored.Identity.Value, identifier.Value);
    }

    [Fact]
    public async Task FN_CC_005_retrieves_a_specific_version_and_the_latest()
    {
        var store = CreateStore();
        var first = await store.CreateAsync(MinimalDocument());
        await store.CreateVersionAsync(first.Identity, MinimalDocument());

        Assert.Equal(1, (await store.GetAsync(first.Identity, 1))!.Version);
        Assert.Equal(2, (await store.GetLatestAsync(first.Identity))!.Version);
    }

    [Fact]
    public async Task FN_CC_005_returns_nothing_for_an_unknown_document_or_version()
    {
        var store = CreateStore();
        var stored = await store.CreateAsync(MinimalDocument());

        Assert.Null(await store.GetAsync(stored.Identity, 99));
        Assert.Null(await store.GetLatestAsync(new DocumentIdentity(stored.Identity.System, Guid.NewGuid().ToString())));
    }

    [Fact]
    public async Task FN_CC_007_creating_a_new_version_leaves_the_previous_one_untouched()
    {
        var store = CreateStore();
        var first = await store.CreateAsync(MinimalDocument());
        var originalTitle = CompositionOf(first.Bundle).Title;

        var amended = MinimalDocument();
        CompositionOf(amended).Title = "AMENDED SYNTHETIC TEST LABEL";
        await store.CreateVersionAsync(first.Identity, amended);

        var reread = (await store.GetAsync(first.Identity, 1))!;
        Assert.Equal(originalTitle, CompositionOf(reread.Bundle).Title);
    }

    [Fact]
    public async Task FN_CC_007_a_caller_mutating_a_retrieved_document_does_not_change_the_store()
    {
        // Handing out a reference to stored content would make immutability a convention
        // rather than a guarantee.
        var store = CreateStore();
        var stored = await store.CreateAsync(MinimalDocument());

        CompositionOf(stored.Bundle).Title = "TAMPERED";

        var reread = (await store.GetAsync(stored.Identity, 1))!;
        Assert.NotEqual("TAMPERED", CompositionOf(reread.Bundle).Title);
    }

    [Fact]
    public async Task FN_CC_007_rejects_a_bundle_that_already_claims_an_identifier_in_our_namespace()
    {
        // ADR-015: identity is minted by the platform. A submitted bundle cannot claim one,
        // or an external system could collide with, or overwrite, our identity space.
        var store = CreateStore();
        var claimed = MinimalDocument();
        claimed.Identifier = new Identifier(ContentCoreDefaults.DocumentIdentifierSystem, Guid.NewGuid().ToString());

        await Assert.ThrowsAsync<InvalidEpiBundleException>(() => store.CreateAsync(claimed));
    }

    [Fact]
    public async Task FN_CC_003_rejects_a_new_version_of_a_document_that_does_not_exist()
    {
        var store = CreateStore();
        var unknown = new DocumentIdentity(ContentCoreDefaults.DocumentIdentifierSystem, Guid.NewGuid().ToString());

        await Assert.ThrowsAsync<UnknownDocumentException>(() => store.CreateVersionAsync(unknown, MinimalDocument()));
    }

    [Fact]
    public async Task IT_001_a_conformant_bundle_round_trips_through_create_and_read_without_content_loss()
    {
        // The acceptance criterion the canonical-model premise rests on (CAP-SCM-010).
        var store = CreateStore();
        var submitted = MinimalDocument();

        var created = await store.CreateAsync(submitted);
        var retrieved = (await store.GetAsync(created.Identity, created.Version))!;

        // The platform stamps identity and version, and a FHIR server adds its own meta
        // (lastUpdated, versionId). Assert those separately, then compare everything else -
        // the content - byte for byte in structure.
        Assert.Equal(created.Identity.Value, retrieved.Bundle.Identifier?.Value);
        Assert.Equal(1, retrieved.Version);

        var expected = MinimalDocument();
        var actual = (Bundle)retrieved.Bundle.DeepCopy();
        expected.Identifier = actual.Identifier = null;
        expected.Meta = actual.Meta = null;

        Assert.True(expected.IsExactly(actual),
            "Content submitted and content retrieved are not structurally identical.");
    }

    [Fact]
    public async Task IT_006_an_attempt_to_mutate_an_existing_version_is_rejected_and_history_is_reconstructable()
    {
        var store = CreateStore();
        var first = await store.CreateAsync(MinimalDocument());

        var amended = MinimalDocument();
        CompositionOf(amended).Title = "SECOND VERSION";
        await store.CreateVersionAsync(first.Identity, amended);

        // There is no update operation to call: a correction is a new version, and every
        // prior version remains retrievable exactly as it was (CAP-LCM-006).
        Assert.Equal([1, 2], await store.VersionsAsync(first.Identity));
        Assert.Equal("SYNTHETIC TEST LABEL - Examplinum 10 mg film-coated tablets",
            CompositionOf((await store.GetAsync(first.Identity, 1))!.Bundle).Title);
        Assert.Equal("SECOND VERSION",
            CompositionOf((await store.GetAsync(first.Identity, 2))!.Bundle).Title);
    }
}
