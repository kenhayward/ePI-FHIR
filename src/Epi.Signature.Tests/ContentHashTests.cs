using Epi.ContentCore;
using Hl7.Fhir.Model;
using Xunit;

namespace Epi.Signature.Tests;

// FN-AUD-005 Capture an electronic signature over the hash of the pinned version
//
// What the hash covers, and what it deliberately does not, is the part of a signature that has
// to survive operational reality: a restore, a re-index, or a migration between FHIR servers
// must not invalidate signatures made years earlier (ADR-020 decision 5).
public sealed class ContentHashTests
{
    private static readonly DocumentIdentity Document =
        new(ContentCoreDefaults.DocumentIdentifierSystem, "018f2a10-0000-7000-8000-000000000001");

    /// <summary>The anchoring Composition, asserted present rather than assumed.</summary>
    private static Composition CompositionOf(Bundle bundle) =>
        Assert.IsType<Composition>(bundle.Entry[0].Resource);

    private static Bundle Stamped(int version = 1, DocumentIdentity? identity = null) =>
        ContentIdentity.Stamp(
            EpiBundleReader.Read(File.ReadAllText(TestFixtures.Path("epi", "minimal-epi-document.json"))),
            identity ?? Document,
            version);

    [Fact]
    public void FN_AUD_005_names_the_algorithm_it_used()
    {
        // An unprefixed hex string is a hash whose algorithm is whatever the code said at the
        // time. A signature has to be verifiable long after the code has moved on, so the
        // record states how it was computed.
        Assert.StartsWith("sha-256:", ContentHash.Of(Stamped()), StringComparison.Ordinal);
    }

    [Fact]
    public void FN_AUD_005_the_same_content_hashes_the_same_through_a_serialisation_round_trip()
    {
        var bundle = Stamped();

        var direct = ContentHash.Of(bundle);
        var round_tripped = ContentHash.Of(EpiBundleReader.Read(EpiBundleReader.Write(bundle)));

        Assert.Equal(direct, round_tripped);
    }

    [Fact]
    public void FN_AUD_005_content_that_differs_hashes_differently()
    {
        var original = Stamped();
        var amended = Stamped();
        CompositionOf(amended).Title = "SECOND VERSION";

        Assert.NotEqual(ContentHash.Of(original), ContentHash.Of(amended));
    }

    [Fact]
    public void FN_AUD_005_metadata_the_server_assigned_does_not_change_the_hash()
    {
        // Logical id, meta.versionId and meta.lastUpdated belong to whichever FHIR server
        // happens to hold the content. Including them would mean a restore silently
        // invalidating every signature ever made, for reasons unrelated to the content.
        var stored = Stamped();
        var restored = Stamped();

        stored.Id = "hapi-generated-1";
        stored.Meta!.VersionId = "3";
        stored.Meta.LastUpdated = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        restored.Id = "hapi-generated-2";
        restored.Meta!.VersionId = "1";
        restored.Meta.LastUpdated = new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero);

        Assert.Equal(ContentHash.Of(stored), ContentHash.Of(restored));
    }

    [Fact]
    public void FN_AUD_005_the_platform_version_does_change_the_hash()
    {
        // The opposite of the rule above, and the reason the two are tested together: our own
        // version tag is content, because the hash must say which version was signed and not
        // merely what it said. Two versions with identical text are not the same version.
        Assert.NotEqual(ContentHash.Of(Stamped(version: 1)), ContentHash.Of(Stamped(version: 2)));
    }

    [Fact]
    public void FN_AUD_005_the_platform_identity_does_change_the_hash()
    {
        var other = new DocumentIdentity(
            ContentCoreDefaults.DocumentIdentifierSystem, "018f2a10-0000-7000-8000-000000000002");

        Assert.NotEqual(ContentHash.Of(Stamped()), ContentHash.Of(Stamped(identity: other)));
    }

    [Fact]
    public void FN_AUD_005_hashing_leaves_the_content_it_was_given_alone()
    {
        // Stripping server metadata must not strip it from the caller's copy: the bundle passed
        // in is on its way to being stored or returned, and a hash function that mutates its
        // argument would quietly delete the server's own metadata from it.
        var bundle = Stamped();
        bundle.Id = "hapi-generated-1";
        bundle.Meta!.VersionId = "3";

        ContentHash.Of(bundle);

        Assert.Equal("hapi-generated-1", bundle.Id);
        Assert.Equal("3", bundle.Meta.VersionId);
    }
}
