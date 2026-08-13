using Epi.ContentCore;
using Hl7.Fhir.Model;
using Xunit;

namespace Epi.Validation.Tests;

// The write gate: content is validated before it is stored, never after.
//   IT-005 Malformed content is rejected with itemised located errors and leaves no partial state
public sealed class ValidatingContentStoreTests : IClassFixture<StructuralValidatorFixture>
{
    private readonly StructuralValidator _validator;

    public ValidatingContentStoreTests(StructuralValidatorFixture fixture) => _validator = fixture.Validator;

    private static Bundle MinimalDocument() =>
        EpiBundleReader.Read(File.ReadAllText(TestFixtures.Path("epi", "minimal-epi-document.json")));

    private static Composition CompositionOf(Bundle bundle) =>
        Assert.IsType<Composition>(bundle.Entry[0].Resource);

    private (ValidatingContentStore Gate, InMemoryContentStore Inner) Build()
    {
        var inner = new InMemoryContentStore();
        return (new ValidatingContentStore(inner, _validator), inner);
    }

    [Fact]
    public async Task IT_005_valid_content_passes_the_gate_and_is_stored()
    {
        var (gate, _) = Build();

        var stored = await gate.CreateAsync(MinimalDocument());

        Assert.Equal(1, stored.Version);
        Assert.NotNull(await gate.GetAsync(stored.Identity, 1));
    }

    [Fact]
    public async Task IT_005_malformed_content_is_rejected_with_itemised_located_errors()
    {
        var (gate, _) = Build();
        var bundle = MinimalDocument();
        CompositionOf(bundle).Status = null;

        var rejected = await Assert.ThrowsAsync<ContentRejectedException>(() => gate.CreateAsync(bundle));

        Assert.NotEmpty(rejected.Issues);
        Assert.All(rejected.Issues.Where(i => i.Severity == ValidationSeverity.Error), issue =>
        {
            Assert.False(string.IsNullOrWhiteSpace(issue.Message));
            Assert.False(string.IsNullOrWhiteSpace(issue.Location));
        });
    }

    [Fact]
    public async Task IT_005_rejected_content_leaves_no_partial_state()
    {
        // The gate must reject before the store is touched. A document that failed validation
        // but left a version behind would be worse than no gate at all.
        var (gate, inner) = Build();
        var bundle = MinimalDocument();
        CompositionOf(bundle).Status = null;

        await Assert.ThrowsAsync<ContentRejectedException>(() => gate.CreateAsync(bundle));

        Assert.Empty(inner.KnownIdentities);
    }

    [Fact]
    public async Task IT_005_a_rejected_new_version_leaves_the_previous_version_intact()
    {
        var (gate, _) = Build();
        var first = await gate.CreateAsync(MinimalDocument());

        var broken = MinimalDocument();
        CompositionOf(broken).Status = null;
        await Assert.ThrowsAsync<ContentRejectedException>(
            () => gate.CreateVersionAsync(first.Identity, broken));

        Assert.Equal([1], await gate.VersionsAsync(first.Identity));
        Assert.Equal("SYNTHETIC TEST LABEL - Examplinum 10 mg film-coated tablets",
            CompositionOf((await gate.GetAsync(first.Identity, 1))!.Bundle).Title);
    }
}
