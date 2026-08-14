using Xunit;

namespace Epi.Validation.Tests;

// FN-LCM-005 Reading the pinned conformance packages, so an approval can record them
//   CAP-LCM-011 Pin the content snapshot and its validating context at approval
public sealed class ConformanceManifestTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("epi-manifest-").FullName;

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private string WithManifest(string content)
    {
        File.WriteAllText(Path.Combine(_directory, ConformanceManifest.FileName), content);
        return _directory;
    }

    private static string RepositoryPath(params string[] segments)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EpiPlatform.sln")))
        {
            directory = directory.Parent;
        }

        return Path.Combine([directory!.FullName, .. segments]);
    }

    [Fact]
    public void FN_LCM_005_the_pinned_packages_are_read_with_their_versions_and_digests()
    {
        var packages = ConformanceManifest.LoadFrom(WithManifest("""
            {
              "retrieved": "2026-08-13",
              "registry": "https://packages.fhir.org",
              "packages": {
                "hl7.fhir.uv.emedicinal-product-info": {
                  "version": "1.0.0",
                  "file": "hl7.fhir.uv.emedicinal-product-info-1.0.0.tgz",
                  "sha256": "c997673388d2c53bd7dad43777a7589443e4d27f56a661a90aa0638bbce1cf3a"
                }
              }
            }
            """)).Packages;

        var package = Assert.Single(packages);
        Assert.Equal("hl7.fhir.uv.emedicinal-product-info", package.Name);
        Assert.Equal("1.0.0", package.Version);
        Assert.StartsWith("c99767", package.Sha256, StringComparison.Ordinal);
    }

    [Fact]
    public void CAP_LCM_011_a_manifest_that_cannot_be_read_is_refused_rather_than_assumed_empty()
    {
        // A deployment that cannot say what it validates against cannot pin it either, and an
        // approval carrying an empty context is worse than no approval: it looks like a record.
        Assert.Throws<ConformanceManifestException>(
            () => ConformanceManifest.LoadFrom(_directory));

        Assert.Throws<ConformanceManifestException>(
            () => ConformanceManifest.LoadFrom(WithManifest("{ not json")));

        Assert.Throws<ConformanceManifestException>(
            () => ConformanceManifest.LoadFrom(WithManifest("""{"packages": {}}""")));
    }

    [Fact]
    public void FN_LCM_005_the_shipped_manifest_names_the_pinned_implementation_guide()
    {
        // The pin ADR-016 made, read the way an approval will read it. If this file is ever
        // reorganised, the pinning path finds out here rather than at the next approval.
        var manifest = ConformanceManifest.LoadFrom(RepositoryPath("profiles", "packages"));

        var ig = Assert.Single(
            manifest.Packages, p => p.Name == "hl7.fhir.uv.emedicinal-product-info");
        Assert.Equal("1.0.0", ig.Version);
        Assert.Equal(64, ig.Sha256.Length);
    }

    [Fact]
    public void CAP_LCM_011_the_shipped_packages_match_the_digests_recorded_for_them()
    {
        // The same check CI runs, from inside the code that depends on it: reading the manifest
        // is only an honest substitute for hashing what the validator loaded while the vendored
        // bytes are known to match (ADR-023 decision 5).
        var packages = RepositoryPath("profiles", "packages");

        Assert.Empty(ConformanceManifest.LoadFrom(packages).Discrepancies(packages));
    }

    [Fact]
    public void CAP_LCM_011_a_package_whose_bytes_have_changed_is_reported_not_hidden()
    {
        var directory = WithManifest("""
            {
              "packages": {
                "example.package": {
                  "version": "1.0.0",
                  "file": "example.package-1.0.0.tgz",
                  "sha256": "0000000000000000000000000000000000000000000000000000000000000000"
                }
              }
            }
            """);
        File.WriteAllText(Path.Combine(directory, "example.package-1.0.0.tgz"), "not the pinned bytes");

        var discrepancies = ConformanceManifest.LoadFrom(directory).Discrepancies(directory);

        Assert.Contains(discrepancies, d => d.Contains("example.package", StringComparison.Ordinal));
    }

    [Fact]
    public void CAP_LCM_011_a_package_that_is_no_longer_there_is_reported_too()
    {
        var directory = WithManifest("""
            {
              "packages": {
                "missing.package": {
                  "version": "2.0.0",
                  "file": "missing.package-2.0.0.tgz",
                  "sha256": "0000000000000000000000000000000000000000000000000000000000000000"
                }
              }
            }
            """);

        var discrepancies = ConformanceManifest.LoadFrom(directory).Discrepancies(directory);

        Assert.Contains(discrepancies, d => d.Contains("missing.package", StringComparison.Ordinal));
    }
}
