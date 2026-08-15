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
        //
        // Built under a lock file, because the SDK expands the core specification into one
        // cache directory under the temporary path and two processes doing that at once
        // collide - "Directory not empty", reported as a validation error against every
        // document, which reads as content being invalid rather than as a race. A lock inside
        // the process is not enough: the test run puts several assemblies in separate
        // processes, which is where CI found it.
        var core = UnderCacheLock(() =>
        {
            var source = ZipSource.CreateValidationSource();

            // Forced inside the lock, because the SDK expands the specification on first use
            // rather than on construction - so a lock around construction alone protects
            // nothing, which is what the first attempt at this did.
            _ = source.ListSummaries().Count();
            return source;
        });

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

    /// <summary>
    /// Runs a build of the core source with an exclusive lock held across every process.
    /// </summary>
    /// <remarks>
    /// A lock file rather than a named mutex: named mutexes are not shared between processes on
    /// Unix, which is the platform CI runs on and therefore the only one where this matters.
    /// Serialising a few seconds of unzip is cheap; a validation error that appears only when
    /// two assemblies start together is not.
    /// </remarks>
    private static T UnderCacheLock<T>(Func<T> build)
    {
        var lockFile = Path.Combine(Path.GetTempPath(), "epi-fhir-artifact-cache.lock");
        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);

        while (true)
        {
            try
            {
                using var held = new FileStream(
                    lockFile, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
                return build();
            }
            catch (IOException) when (DateTimeOffset.UtcNow < deadline)
            {
                // Somebody else is expanding the cache. Waiting is the whole point.
                Thread.Sleep(250);
            }
        }
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
    /// <summary>
    /// Where the vendored packages are: what configuration names, or the repository's own
    /// directory when this runs from a checkout.
    /// </summary>
    /// <remarks>
    /// Public because pinning a validating context at approval has to record the same packages
    /// the validator loaded, and it cannot do that while only the validator knows where they
    /// are (ADR-023 decision 2).
    /// </remarks>
    public static string PackagesDirectory(string? configured = null) =>
        string.IsNullOrWhiteSpace(configured) ? LocatePackagesDirectory() : configured;

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
