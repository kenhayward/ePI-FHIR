using Epi.ContentCore;
using Epi.Lifecycle;
using Hl7.Fhir.Model;
using Xunit;

namespace Epi.Signature.Tests;

// FN-WFL-003 Require a valid, unused signature at a gate the model says must be signed
//   IT-012 Approval captures a signature binding signer, meaning, time and a hash of the
//          version signed
public sealed class SignatureCheckTests
{
    private const string AnnasPassword = "correct-horse-battery-staple";
    private const string BensPassword = "battery-staple-correct-horse";

    private static readonly DocumentIdentity Identity =
        new(ContentCoreDefaults.DocumentIdentifierSystem, "018f2a10-0000-7000-8000-000000000001");

    private static readonly VersionRef Reference = new(Identity.Value, 1);

    private sealed class KnownUsers : ICredentialVerifier
    {
        public Task<SignerIdentity?> VerifyAsync(
            string identifier, string password, CancellationToken cancellationToken = default) =>
            Task.FromResult(
                (identifier, password) switch
                {
                    ("user-anna", AnnasPassword) => new SignerIdentity("user-anna", "Anna Novak"),
                    ("user-ben", BensPassword) => new SignerIdentity("user-ben", "Ben Okafor"),
                    _ => null,
                });
    }

    private static EpiDocument Version(int version = 1)
    {
        var bundle = EpiBundleReader.Read(
            File.ReadAllText(TestFixtures.Path("epi", "minimal-epi-document.json")));
        return new EpiDocument(Identity, version, ContentIdentity.Stamp(bundle, Identity, version));
    }

    private static (ElectronicSignatureService Signing, SignatureCheck Check) Wired()
    {
        var store = new InMemorySignatureStore();
        return (new ElectronicSignatureService(new KnownUsers(), store), new SignatureCheck(store));
    }

    [Fact]
    public async Task FN_WFL_003_a_signature_by_this_actor_over_this_version_meaning_this_is_valid()
    {
        var (signing, check) = Wired();
        var manifest = await signing.SignAsync(
            Version(), "user-ben", BensPassword, SignatureMeaning.Approval);

        var result = await check.IsValidAsync(manifest.Reference, Reference, "user-ben", "approval");

        Assert.True(result.IsValid);
    }

    [Fact]
    public async Task FN_WFL_003_a_reference_that_names_no_signature_is_not_valid()
    {
        // The failure a forged or mistyped reference produces. Nothing about it should look
        // like a signature the platform issued.
        var (_, check) = Wired();

        var result = await check.IsValidAsync("not-a-signature", Reference, "user-ben", "approval");

        Assert.False(result.IsValid);
        Assert.Contains("no such signature", result.Problem!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FN_WFL_003_a_signature_by_someone_else_is_not_valid_for_this_actor()
    {
        // Otherwise one person's signature would let another person's transition through, and
        // segregation of duties would be enforced on the actor while the signature said someone
        // else entirely.
        var (signing, check) = Wired();
        var manifest = await signing.SignAsync(
            Version(), "user-anna", AnnasPassword, SignatureMeaning.Approval);

        var result = await check.IsValidAsync(manifest.Reference, Reference, "user-ben", "approval");

        Assert.False(result.IsValid);
        Assert.Contains("other than the actor", result.Problem!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FN_WFL_003_a_signature_over_another_version_is_not_valid_here()
    {
        var (signing, check) = Wired();
        var manifest = await signing.SignAsync(
            Version(2), "user-ben", BensPassword, SignatureMeaning.Approval);

        var result = await check.IsValidAsync(manifest.Reference, Reference, "user-ben", "approval");

        Assert.False(result.IsValid);
        Assert.Contains("different version", result.Problem!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FN_WFL_003_a_signature_over_another_document_is_not_valid_here()
    {
        var (signing, check) = Wired();
        var manifest = await signing.SignAsync(
            Version(), "user-ben", BensPassword, SignatureMeaning.Approval);

        var elsewhere = new VersionRef("018f2a10-0000-7000-8000-000000000009", 1);
        var result = await check.IsValidAsync(manifest.Reference, elsewhere, "user-ben", "approval");

        Assert.False(result.IsValid);
        Assert.Contains("different version", result.Problem!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FN_WFL_003_a_signature_meaning_review_is_not_valid_at_an_approval_gate()
    {
        // The whole reason the model states a meaning. A reviewer's signature standing in for
        // an approver's would record assent that was never given.
        var (signing, check) = Wired();
        var manifest = await signing.SignAsync(
            Version(), "user-ben", BensPassword, SignatureMeaning.Review);

        var result = await check.IsValidAsync(manifest.Reference, Reference, "user-ben", "approval");

        Assert.False(result.IsValid);
        Assert.Contains("Review", result.Problem!, StringComparison.Ordinal);
    }

    [Fact]
    public async Task IT_012_approval_captures_a_signature_over_the_exact_version_signed()
    {
        // Iteration-2 acceptance criterion 3, end to end: the author submits, someone else
        // signs and approves, and the signature the transition cites binds signer, meaning,
        // time, and a hash of exactly the content approved.
        var signatures = new InMemorySignatureStore();
        var signing = new ElectronicSignatureService(new KnownUsers(), signatures);
        var lifecycle = new InMemoryLifecycleStore();
        var service = new LifecycleService(
            LifecycleModelConfiguration.LoadFrom(RepositoryPath("config", "lifecycle", "label-states.json")),
            lifecycle,
            time: null,
            signatureCheck: new SignatureCheck(signatures));

        var document = Version();
        await service.RegisterAsync(Reference, "user-anna");
        await service.TransitionAsync(Reference, "submit", "user-anna");

        var manifest = await signing.SignAsync(
            document, "user-ben", BensPassword, SignatureMeaning.Approval, reason: "checked against source");
        var transition = await service.TransitionAsync(
            Reference, "approve", "user-ben", signatureReference: manifest.Reference,
            approvalContext: new ApprovalContext(
                manifest.ContentHash,
                [new PinnedPackage("hl7.fhir.uv.emedicinal-product-info", "1.0.0", "c99767")],
                "https://epi.example.org/identifier/document"));

        Assert.Equal("approved", await lifecycle.CurrentStateAsync(Reference));
        Assert.Equal(manifest.Reference, transition.SignatureReference);

        var signed = await signatures.FindAsync(transition.SignatureReference!);
        Assert.Equal("user-ben", signed!.SignerIdentifier);
        Assert.Equal("Ben Okafor", signed.SignerPrintedName);
        Assert.Equal(SignatureMeaning.Approval, signed.Meaning);
        Assert.NotEqual(default, signed.SignedAt);
        Assert.True(signed.Covers(document));

        // And the discriminating half, without which everything above would pass on a gate
        // that never consulted the signature at all: a signature over a different version of
        // the same document does not open this one.
        var elsewhere = new VersionRef(Identity.Value, 2);
        await service.RegisterAsync(elsewhere, "user-anna");
        await service.TransitionAsync(elsewhere, "submit", "user-anna");
        var strayManifest = await signing.SignAsync(
            Version(3), "user-ben", BensPassword, SignatureMeaning.Approval);

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(
                elsewhere, "approve", "user-ben", signatureReference: strayManifest.Reference));

        Assert.Contains("different version", refused.Reason);
    }

    [Fact]
    public async Task IT_012_a_signature_by_the_author_cannot_approve_their_own_version()
    {
        // Two controls that must hold together: a valid signature does not excuse segregation
        // of duties, and the author signing their own work is exactly the case someone would
        // try. Asserted end to end because each control passing alone proves nothing about the
        // pair.
        var signatures = new InMemorySignatureStore();
        var signing = new ElectronicSignatureService(new KnownUsers(), signatures);
        var service = new LifecycleService(
            LifecycleModelConfiguration.LoadFrom(RepositoryPath("config", "lifecycle", "label-states.json")),
            new InMemoryLifecycleStore(),
            time: null,
            signatureCheck: new SignatureCheck(signatures));

        await service.RegisterAsync(Reference, "user-anna");
        await service.TransitionAsync(Reference, "submit", "user-anna");

        var manifest = await signing.SignAsync(
            Version(), "user-anna", AnnasPassword, SignatureMeaning.Approval);

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(
                Reference, "approve", "user-anna", signatureReference: manifest.Reference));

        Assert.Contains("may not approve", refused.Reason);
    }

    private static string RepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null &&
               !File.Exists(System.IO.Path.Combine(directory.FullName, "EpiPlatform.sln")))
        {
            directory = directory.Parent;
        }

        return System.IO.Path.Combine([directory!.FullName, .. segments]);
    }
}
