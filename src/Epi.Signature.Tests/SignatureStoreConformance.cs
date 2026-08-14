using Epi.ContentCore;
using Xunit;

namespace Epi.Signature.Tests;

/// <summary>
/// The behaviour every signature store must exhibit, whatever backs it (FN-AUD-005).
/// </summary>
public abstract class SignatureStoreConformance
{
    private static readonly DocumentIdentity Document =
        new(ContentCoreDefaults.DocumentIdentifierSystem, "018f2a10-0000-7000-8000-000000000001");

    /// <summary>A store ready to use, with its schema in place if it needs one.</summary>
    protected abstract Task<ISignatureStore> CreateStoreAsync();

    private static SignatureManifest Manifest(
        string reference = "sig-1",
        SignatureMeaning meaning = SignatureMeaning.Approval,
        int version = 1,
        string? reason = null) =>
        new(reference, "user-ben", "Ben Okafor", meaning, Document, version,
            "sha-256:9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08",
            new DateTimeOffset(2026, 8, 14, 12, 0, 0, TimeSpan.Zero), reason);

    [Fact]
    public async Task FN_AUD_005_a_reference_that_names_no_signature_finds_nothing()
    {
        var store = await CreateStoreAsync();

        Assert.Null(await store.FindAsync("sig-1"));
    }

    [Fact]
    public async Task FN_AUD_005_every_field_of_a_manifest_survives_being_stored_and_read_back()
    {
        // The manifest is the signature. A store dropping the meaning, the printed name, or the
        // hash would leave a record that cannot answer what 21 CFR Part 11 Section 11.50 asks of
        // it, while looking intact from every other angle.
        var store = await CreateStoreAsync();
        var manifest = Manifest(reason: "reviewed against source");

        await store.AppendAsync(manifest);
        var found = await store.FindAsync("sig-1");

        Assert.NotNull(found);
        Assert.Equal(manifest.Reference, found!.Reference);
        Assert.Equal(manifest.SignerIdentifier, found.SignerIdentifier);
        Assert.Equal(manifest.SignerPrintedName, found.SignerPrintedName);
        Assert.Equal(manifest.Meaning, found.Meaning);
        Assert.Equal(manifest.Document, found.Document);
        Assert.Equal(manifest.Version, found.Version);
        Assert.Equal(manifest.ContentHash, found.ContentHash);
        Assert.Equal(manifest.SignedAt, found.SignedAt);
        Assert.Equal(manifest.Reason, found.Reason);
    }

    [Fact]
    public async Task FN_AUD_005_a_manifest_with_no_reason_reads_back_without_one()
    {
        var store = await CreateStoreAsync();
        await store.AppendAsync(Manifest());

        Assert.Null((await store.FindAsync("sig-1"))!.Reason);
    }

    [Theory]
    [InlineData(SignatureMeaning.Authorship)]
    [InlineData(SignatureMeaning.Review)]
    [InlineData(SignatureMeaning.Approval)]
    [InlineData(SignatureMeaning.Responsibility)]
    public async Task FN_AUD_005_every_meaning_survives_storage(SignatureMeaning meaning)
    {
        // Meaning is what Section 11.50(a)(3) requires to be recorded, and it is what
        // distinguishes an approval gate from a submission gate. A store that flattened it
        // would silently make one signature usable at the other.
        var store = await CreateStoreAsync();
        await store.AppendAsync(Manifest(meaning: meaning));

        Assert.Equal(meaning, (await store.FindAsync("sig-1"))!.Meaning);
    }

    [Fact]
    public async Task FN_AUD_005_signatures_are_kept_apart_by_reference()
    {
        var store = await CreateStoreAsync();
        await store.AppendAsync(Manifest("sig-1", version: 1));
        await store.AppendAsync(Manifest("sig-2", version: 2));

        Assert.Equal(1, (await store.FindAsync("sig-1"))!.Version);
        Assert.Equal(2, (await store.FindAsync("sig-2"))!.Version);
    }

    [Fact]
    public async Task FN_AUD_005_a_reference_cannot_be_recorded_twice()
    {
        // Accepting it would replace one signature with another under the same reference, which
        // is an amendment however it is spelled (ADR-020 decision 7).
        var store = await CreateStoreAsync();
        await store.AppendAsync(Manifest("sig-1", SignatureMeaning.Approval));

        await Assert.ThrowsAnyAsync<Exception>(
            () => store.AppendAsync(Manifest("sig-1", SignatureMeaning.Review)));

        Assert.Equal(SignatureMeaning.Approval, (await store.FindAsync("sig-1"))!.Meaning);
    }
}
