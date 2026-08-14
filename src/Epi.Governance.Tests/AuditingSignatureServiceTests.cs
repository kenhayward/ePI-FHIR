using Epi.ContentCore;
using Epi.Governance.Audit;
using Epi.Signature;
using Hl7.Fhir.Model;
using Xunit;

namespace Epi.Governance.Tests;

// FN-AUD-005 Capture an electronic signature over the hash of the pinned version
//
// 21 CFR Part 11 Section 11.300(d) requires attempted unauthorised use of a credential to be
// detected and reported. A wrong password at an approval gate is exactly that signal, so
// refusals are recorded as deliberately as signatures (ADR-020 decision 9).
public sealed class AuditingSignatureServiceTests
{
    private const string AnnasPassword = "correct-horse-battery-staple";

    private static readonly DocumentIdentity Identity =
        new(ContentCoreDefaults.DocumentIdentifierSystem, "018f2a10-0000-7000-8000-000000000001");

    private sealed class KnownUsers : ICredentialVerifier
    {
        public Task<SignerIdentity?> VerifyAsync(
            string identifier, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult(
                identifier == "user-anna" && password == AnnasPassword
                    ? new SignerIdentity("user-anna", "Anna Novak")
                    : null);
    }

    private static EpiDocument Version()
    {
        var bundle = EpiBundleReader.Read(
            File.ReadAllText(TestFixtures.Path("epi", "minimal-epi-document.json")));
        return new EpiDocument(Identity, 1, ContentIdentity.Stamp(bundle, Identity, 1));
    }

    private static (IElectronicSignatureService Service, InMemoryAuditSink Audit) Wired()
    {
        var audit = new InMemoryAuditSink();
        var service = new AuditingSignatureService(
            new ElectronicSignatureService(new KnownUsers(), new InMemorySignatureStore()), audit);
        return (service, audit);
    }

    [Fact]
    public async Task FN_AUD_005_a_signature_is_recorded_in_the_audit_trail()
    {
        var (service, audit) = Wired();
        var document = Version();

        var manifest = await service.SignAsync(
            document, "user-anna", AnnasPassword, SignatureMeaning.Approval);

        var record = Assert.Single(await audit.ReadAsync());
        Assert.Equal("user-anna", record.Actor);
        Assert.Equal("signature.sign", record.Action);
        Assert.Equal($"{Identity}@1", record.Target);
        Assert.Equal(AuditOutcome.Succeeded, record.Outcome);
        Assert.Contains(manifest.Reference, record.After);
        Assert.Contains(manifest.ContentHash, record.After);
        Assert.Contains("Approval", record.After, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FN_AUD_005_the_signature_still_reaches_the_caller()
    {
        // A decorator that swallowed its return value would be a control that broke the thing
        // it was watching.
        var (service, _) = Wired();

        var manifest = await service.SignAsync(
            Version(), "user-anna", AnnasPassword, SignatureMeaning.Review);

        Assert.Equal(SignatureMeaning.Review, manifest.Meaning);
    }

    [Fact]
    public async Task FN_AUD_005_a_refused_signature_is_recorded_and_still_refused()
    {
        var (service, audit) = Wired();

        await Assert.ThrowsAsync<SignatureRefusedException>(() => service.SignAsync(
            Version(), "user-anna", "not-annas-password", SignatureMeaning.Approval));

        var record = Assert.Single(await audit.ReadAsync());
        Assert.Equal("signature.sign", record.Action);
        Assert.Equal(AuditOutcome.Denied, record.Outcome);
        Assert.Equal($"{Identity}@1", record.Target);
    }

    [Fact]
    public async Task FN_AUD_005_a_refusal_records_the_identifier_that_was_claimed()
    {
        // The point of recording the attempt is knowing whose credential someone tried to use.
        var (service, audit) = Wired();

        await Assert.ThrowsAsync<SignatureRefusedException>(() => service.SignAsync(
            Version(), "user-anna", "not-annas-password", SignatureMeaning.Approval));

        var record = Assert.Single(await audit.ReadAsync());
        Assert.Equal("user-anna", record.Actor);

        // And the record must not imply the identifier was verified, because it was not.
        Assert.Contains("claimed", record.Reason!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FN_AUD_005_no_password_reaches_the_audit_trail()
    {
        // A password in an append-only store is a credential that by design cannot be purged
        // (ADR-020 decision 8). Asserted on the refusal path as well as the success path,
        // because the refusal path is the one holding a password that was nearly right.
        var (service, audit) = Wired();

        await service.SignAsync(Version(), "user-anna", AnnasPassword, SignatureMeaning.Approval);
        await Assert.ThrowsAsync<SignatureRefusedException>(() => service.SignAsync(
            Version(), "user-anna", "not-annas-password", SignatureMeaning.Approval));

        foreach (var record in await audit.ReadAsync())
        {
            Assert.DoesNotContain(AnnasPassword, record.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("not-annas-password", record.ToString(), StringComparison.Ordinal);
        }
    }
}
