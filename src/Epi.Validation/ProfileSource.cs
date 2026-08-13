using System.Collections.Concurrent;
using System.Formats.Tar;
using System.IO.Compression;
using Hl7.Fhir.Specification.Source;

namespace Epi.Validation;

/// <summary>
/// Resolves conformance resources from the pinned, vendored packages and the core R5
/// definitions - offline, always (ADR-016 decision 3).
/// </summary>
/// <remarks>
/// The vendored packages are npm-style tarballs. They are expanded once into a cache
/// directory and served from there, so nothing reaches the network at validation time and a
/// verdict is reproducible from the repository alone.
/// </remarks>
public static class ProfileSource
{
    private static readonly ConcurrentDictionary<string, Lazy<IAsyncResourceResolver>> Resolvers = new();

    /// <summary>Every conformance resource the platform validates against.</summary>
    /// <remarks>
    /// Built once per process and shared. The resolver is read-only and safe to share, loading
    /// and indexing the packages takes seconds, and building two at once races: the core
    /// definitions are unzipped into a shared cache directory by the SDK, and concurrent
    /// construction collides there. One instance removes the whole class of problem rather
    /// than defending against it in several places.
    /// </remarks>
    public static IAsyncResourceResolver FromPinnedPackages(string? packagesDirectory = null)
    {
        var key = packagesDirectory ?? LocatePackagesDirectory();
        return Resolvers.GetOrAdd(key, path =>
            new Lazy<IAsyncResourceResolver>(() => Build(path), LazyThreadSafetyMode.ExecutionAndPublication)).Value;
    }

    private static IAsyncResourceResolver Build(string packages)
    {
        var expanded = Expand(packages);

        // Core R5 definitions ship with the validator rather than being vendored; see
        // ADR-016 and profiles/packages/manifest.json "notProvidedHere".
        var core = ZipSource.CreateValidationSource();

        var vendored = new DirectorySource(expanded, new DirectorySourceSettings
        {
            IncludeSubDirectories = true,
            // Package tarballs carry examples and supporting files alongside conformance
            // resources; the summary generator skips what it cannot read.
            Mask = "*.json",
        });

        // Index before publishing. The directory scan is lazy, and two callers arriving at an
        // unindexed source at the same time can each get a resolution failure - which this
        // gate would report as a validation error, rejecting content that is perfectly valid.
        // A write gate that rejects on a race is worse than a slow one.
        _ = vendored.ListSummaries().Count();

        // Vendored profiles take precedence over core, so an IG constraint wins where both
        // define the same canonical.
        return new SerialisedResolver(new CachedResolver(new MultiResolver(vendored, core)));
    }

    /// <summary>Expands each vendored tarball once into a per-user cache directory.</summary>
    private static string Expand(string packagesDirectory)
    {
        if (!Directory.Exists(packagesDirectory))
        {
            throw new DirectoryNotFoundException(
                $"No vendored conformance packages at '{packagesDirectory}'. "
                + "They are required for offline validation (ADR-016).");
        }

        var root = Path.Combine(Path.GetTempPath(), "epi-profiles");

        foreach (var archive in Directory.GetFiles(packagesDirectory, "*.tgz").Order(StringComparer.Ordinal))
        {
            // Keyed by file name and length: a re-pinned package is a different file, so it
            // expands into its own directory rather than contaminating the previous one.
            var key = $"{Path.GetFileNameWithoutExtension(archive)}-{new FileInfo(archive).Length}";
            var target = Path.Combine(root, key);
            if (Directory.Exists(Path.Combine(target, "package")))
            {
                continue;
            }

            // Staging is unique per attempt. Several validators can be constructed at once -
            // xUnit builds one fixture per test class in parallel, and a service will have
            // more than one consumer - and a shared staging directory means two of them
            // extracting over each other. That fails on a cold cache and passes on a warm
            // one, which is the worst kind of bug to leave in.
            var staging = $"{target}.{Guid.NewGuid():N}.partial";
            Directory.CreateDirectory(staging);
            try
            {
                using (var file = File.OpenRead(archive))
                using (var gzip = new GZipStream(file, CompressionMode.Decompress))
                {
                    TarFile.ExtractToDirectory(gzip, staging, overwriteFiles: true);
                }

                // Publish by rename, so a half-expanded package is never served. Whoever
                // gets there first wins and the rest discard their copy.
                try
                {
                    Directory.Move(staging, target);
                }
                catch (IOException)
                {
                    // Another writer published first, which is fine: the content is identical.
                }
            }
            finally
            {
                if (Directory.Exists(staging))
                {
                    Directory.Delete(staging, recursive: true);
                }
            }
        }

        return root;
    }

    /// <summary>Finds profiles/packages relative to the running assembly.</summary>
    private static string LocatePackagesDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EpiPlatform.sln")))
        {
            directory = directory.Parent;
        }

        return directory is null
            ? throw new DirectoryNotFoundException(
                "Could not locate the repository root to find profiles/packages.")
            : Path.Combine(directory.FullName, "profiles", "packages");
    }
}
