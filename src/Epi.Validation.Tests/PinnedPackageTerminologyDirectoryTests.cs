using Epi.ContentCore;
using Xunit;

namespace Epi.Validation.Tests;

// The terminology binding point, over what actually answers today (FN-TRM-002).
//   CAP-TRM-007 Track terminology source versions
public sealed class PinnedPackageTerminologyDirectoryTests
{
    private static ConformanceManifest Manifest() => new(
    [
        new ManifestPackage("hl7.fhir.uv.emedicinal-product-info", "1.0.0", "c99767", "a.tgz"),
        new ManifestPackage("hl7.terminology.r5", "5.0.0", "071645", "b.tgz"),
    ]);

    [Fact]
    public async Task FN_TRM_002_a_binding_names_the_terminology_package_and_its_version()
    {
        var bindings = await new PinnedPackageTerminologyDirectory(Manifest()).BindingsAsync();

        var binding = Assert.Single(bindings);
        Assert.Equal("hl7.terminology.r5", binding.System);
        Assert.Equal("5.0.0", binding.Version);
        Assert.True(binding.IsVersioned);
    }

    [Fact]
    public async Task FN_TRM_002_a_structural_profile_package_is_not_reported_as_terminology()
    {
        // A profile package is not something a code came from, and listing it as one would make
        // the pinned record say something untrue in a place whose whole value is that it does
        // not (ADR-036 decision 2).
        var bindings = await new PinnedPackageTerminologyDirectory(Manifest()).BindingsAsync();

        Assert.DoesNotContain(
            bindings, b => b.System.Contains("emedicinal", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FN_TRM_002_a_deployment_with_no_terminology_package_binds_to_nothing()
    {
        // Empty, and honestly so. An approval then records that it was asked and had none,
        // which is what distinguishes it from a pin taken before bindings existed.
        var bindings = await new PinnedPackageTerminologyDirectory(
            new ConformanceManifest([
                new ManifestPackage("hl7.fhir.uv.emedicinal-product-info", "1.0.0", "c9", "a.tgz"),
            ])).BindingsAsync();

        Assert.Empty(bindings);
    }

    [Fact]
    public async Task FN_TRM_002_an_unrecognised_code_resolves_to_nothing_rather_than_throwing()
    {
        // Null means "this directory does not recognise it", which a caller has to handle
        // anyway - so a caller written against this port keeps working when a real terminology
        // source is configured behind it (ADR-036 decision 4).
        var directory = new PinnedPackageTerminologyDirectory(Manifest());

        Assert.Null(await directory.LookupAsync("http://snomed.info/sct", "73211009"));
    }

    [Fact]
    public async Task FN_TRM_002_the_shipped_packages_bind_to_a_versioned_terminology()
    {
        // The one that notices if the vendored terminology package is removed or renamed, which
        // would silently empty every pin taken afterwards.
        var manifest = ConformanceManifest.LoadFrom(ProfileSource.PackagesDirectory(null));

        var bindings = await new PinnedPackageTerminologyDirectory(manifest).BindingsAsync();

        Assert.NotEmpty(bindings);
        Assert.All(bindings, binding => Assert.True(binding.IsVersioned));
    }
}
