using Epi.ContentCore;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Epi.Signature.Tests;

// FN-AUD-005 Capture an electronic signature over the hash of the pinned version
//   Realises CAP-AUD-003 and iteration-2 acceptance criterion 3.
public sealed class ElectronicSignatureServiceTests
{
    private const string AnnasPassword = "correct-horse-battery-staple";

    private static readonly DocumentIdentity Identity =
        new(ContentCoreDefaults.DocumentIdentifierSystem, "018f2a10-0000-7000-8000-000000000001");

    /// <summary>
    /// Stands in for the identity provider. Records the passwords it was handed, so a test can
    /// assert what the service did and did not send it.
    /// </summary>
    private sealed class KnownUsers : ICredentialVerifier
    {
        private readonly Dictionary<string, (string Password, string PrintedName)> _users = new(StringComparer.Ordinal)
        {
            ["user-anna"] = (AnnasPassword, "Anna Novak"),
            ["user-ben"] = ("battery-staple-correct-horse", "Ben Okafor"),
        };

        public List<string> PasswordsSeen { get; } = [];

        public Task<SignerIdentity?> VerifyAsync(
            string identifier, string password, CancellationToken cancellationToken = default)
        {
            PasswordsSeen.Add(password);
            return Task.FromResult(
                _users.TryGetValue(identifier, out var user)
                && string.Equals(user.Password, password, StringComparison.Ordinal)
                    ? new SignerIdentity(identifier, user.PrintedName)
                    : null);
        }
    }

    /// <summary>The anchoring Composition, asserted present rather than assumed.</summary>
    private static Composition CompositionOf(Bundle bundle) =>
        Assert.IsType<Composition>(bundle.Entry[0].Resource);

    private static EpiDocument Version(int version = 1)
    {
        var bundle = EpiBundleReader.Read(
            File.ReadAllText(TestFixtures.Path("epi", "minimal-epi-document.json")));
        if (version > 1)
        {
            CompositionOf(bundle).Title = $"REVISION {version}";
        }

        return new EpiDocument(Identity, version, ContentIdentity.Stamp(bundle, Identity, version));
    }

    private static (ElectronicSignatureService Service, InMemorySignatureStore Store, KnownUsers Users)
        Signing(TimeProvider? time = null)
    {
        var users = new KnownUsers();
        var store = new InMemorySignatureStore();
        return (new ElectronicSignatureService(users, store, time), store, users);
    }

    [Fact]
    public async Task FN_AUD_005_a_signature_records_signer_meaning_time_and_what_was_signed()
    {
        // The four things 21 CFR Part 11 Section 11.50 requires to be recorded, plus the link
        // to the record Section 11.70 requires. If this test is ever weakened, the platform
        // stops being able to answer the only question an inspection asks of a signature.
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 14, 9, 30, 0, TimeSpan.Zero));
        var (service, _, _) = Signing(clock);
        var document = Version();

        var manifest = await service.SignAsync(
            document, "user-anna", AnnasPassword, SignatureMeaning.Approval, reason: "reviewed against source");

        Assert.Equal("user-anna", manifest.SignerIdentifier);
        Assert.Equal("Anna Novak", manifest.SignerPrintedName);
        Assert.Equal(SignatureMeaning.Approval, manifest.Meaning);
        Assert.Equal(clock.GetUtcNow(), manifest.SignedAt);
        Assert.Equal(Identity, manifest.Document);
        Assert.Equal(1, manifest.Version);
        Assert.Equal(ContentHash.Of(document.Bundle), manifest.ContentHash);
        Assert.Equal("reviewed against source", manifest.Reason);
    }

    [Fact]
    public async Task FN_AUD_005_the_printed_name_comes_from_the_identity_provider()
    {
        // There is deliberately no parameter for it. A caller able to state the signer's name
        // could sign in someone else's, which is the same reasoning that makes the lifecycle
        // service read a version's author from the store rather than take it as an argument.
        var (service, _, _) = Signing();

        var manifest = await service.SignAsync(
            Version(), "user-anna", AnnasPassword, SignatureMeaning.Approval);

        Assert.Equal("Anna Novak", manifest.SignerPrintedName);
    }

    [Fact]
    public async Task FN_AUD_005_a_wrong_password_is_refused_and_nothing_is_recorded()
    {
        var (service, store, _) = Signing();

        await Assert.ThrowsAsync<SignatureRefusedException>(() => service.SignAsync(
            Version(), "user-anna", "not-annas-password", SignatureMeaning.Approval));

        Assert.Null(await store.FindAsync("any"));
    }

    [Fact]
    public async Task FN_AUD_005_a_refusal_does_not_say_whether_the_user_or_the_password_was_wrong()
    {
        // Otherwise the signing gate becomes a way of enumerating who holds an account, which
        // is a worse thing to leak from an approval screen than from a login screen.
        var (service, _, _) = Signing();

        var wrongPassword = await Assert.ThrowsAsync<SignatureRefusedException>(() => service.SignAsync(
            Version(), "user-anna", "not-annas-password", SignatureMeaning.Approval));
        var unknownUser = await Assert.ThrowsAsync<SignatureRefusedException>(() => service.SignAsync(
            Version(), "user-nobody", AnnasPassword, SignatureMeaning.Approval));

        Assert.Equal(wrongPassword.Reason, unknownUser.Reason);
    }

    [Fact]
    public async Task FN_AUD_005_a_blank_password_is_refused_without_reaching_the_identity_provider()
    {
        // Some directory servers treat a bind with an empty password as an anonymous success.
        // Whether ours does is not something a signing gate should depend on.
        var (service, _, users) = Signing();

        await Assert.ThrowsAsync<SignatureRefusedException>(() => service.SignAsync(
            Version(), "user-anna", "   ", SignatureMeaning.Approval));

        Assert.Empty(users.PasswordsSeen);
    }

    [Fact]
    public async Task FN_AUD_005_the_password_is_not_written_into_the_record()
    {
        // A password in an append-only store is a credential that by design cannot be purged
        // (ADR-020 decision 8).
        var (service, store, _) = Signing();

        var manifest = await service.SignAsync(
            Version(), "user-anna", AnnasPassword, SignatureMeaning.Approval);
        var stored = await store.FindAsync(manifest.Reference);

        Assert.DoesNotContain(AnnasPassword, stored!.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FN_AUD_005_a_signature_is_retrievable_by_its_reference()
    {
        var (service, store, _) = Signing();

        var manifest = await service.SignAsync(
            Version(), "user-anna", AnnasPassword, SignatureMeaning.Approval);

        Assert.Equal(manifest, await store.FindAsync(manifest.Reference));
    }

    [Fact]
    public async Task FN_AUD_005_each_signature_has_its_own_reference()
    {
        var (service, _, _) = Signing();
        var document = Version();

        var first = await service.SignAsync(document, "user-anna", AnnasPassword, SignatureMeaning.Review);
        var second = await service.SignAsync(document, "user-ben", "battery-staple-correct-horse",
            SignatureMeaning.Approval);

        Assert.NotEqual(first.Reference, second.Reference);
    }

    [Fact]
    public async Task FN_AUD_005_a_signature_covers_the_version_it_was_made_over_and_no_other()
    {
        // The Section 11.70 link, asserted rather than assumed: a manifest transplanted onto a
        // different version does not verify against it.
        var (service, _, _) = Signing();
        var signed = Version(1);

        var manifest = await service.SignAsync(
            signed, "user-anna", AnnasPassword, SignatureMeaning.Approval);

        Assert.True(manifest.Covers(signed));
        Assert.False(manifest.Covers(Version(2)));
    }

    [Fact]
    public async Task FN_AUD_005_a_signature_does_not_cover_content_altered_after_signing()
    {
        var (service, _, _) = Signing();
        var document = Version();

        var manifest = await service.SignAsync(
            document, "user-anna", AnnasPassword, SignatureMeaning.Approval);

        var altered = (Bundle)document.Bundle.DeepCopy();
        CompositionOf(altered).Title = "ALTERED AFTER APPROVAL";

        Assert.False(manifest.Covers(document with { Bundle = altered }));
    }

    [Fact]
    public void FN_AUD_005_the_store_offers_no_way_to_amend_a_signature()
    {
        Assert.DoesNotContain(typeof(ISignatureStore).GetMethods(),
            m => m.Name.Contains("Update", StringComparison.Ordinal)
                 || m.Name.Contains("Delete", StringComparison.Ordinal));
    }
}
