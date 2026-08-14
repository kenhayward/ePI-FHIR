using System.Text.Json;
using System.Text.Json.Serialization;

namespace Epi.Validation;

/// <summary>One vendored conformance package, as the manifest pins it (ADR-016).</summary>
public sealed record ManifestPackage(string Name, string Version, string Sha256, string File);

/// <summary>
/// The pinned conformance packages, read from <c>profiles/packages/manifest.json</c>
/// (ADR-016, ADR-023 decision 2).
/// </summary>
/// <remarks>
/// The manifest is an honest substitute for hashing whatever the validator happened to load,
/// because CI already refuses a package whose bytes do not match its recorded digest
/// (tools/verify-profile-packages.py). Without that check this would be a record of what the
/// repository claims rather than of what was used.
/// </remarks>
public sealed record ConformanceManifest(IReadOnlyList<ManifestPackage> Packages)
{
    public const string FileName = "manifest.json";

    /// <summary>Reads the manifest from a directory of vendored packages.</summary>
    /// <exception cref="ConformanceManifestException">
    /// If it is absent or unreadable. A deployment that cannot say what it validates against
    /// cannot pin it either, and an approval with an empty context is worse than no approval
    /// because it looks like a record.
    /// </exception>
    public static ConformanceManifest LoadFrom(string packagesDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagesDirectory);

        var path = Path.Combine(packagesDirectory, FileName);
        if (!File.Exists(path))
        {
            throw new ConformanceManifestException(
                $"{path}: no pinned conformance manifest. A deployment that cannot say what it "
                + "validates against cannot record it at approval either (ADR-016, ADR-023).");
        }

        ManifestFile? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize<ManifestFile>(File.ReadAllText(path), ReadOptions);
        }
        catch (JsonException error)
        {
            throw new ConformanceManifestException(
                $"{path}: not a valid conformance manifest - {error.Message}");
        }

        var packages = (parsed?.Packages ?? new Dictionary<string, PackageFile>())
            .Where(entry => !entry.Key.StartsWith('_'))
            .Select(entry => new ManifestPackage(
                entry.Key,
                entry.Value.Version ?? string.Empty,
                entry.Value.Sha256 ?? string.Empty,
                entry.Value.File ?? string.Empty))
            .OrderBy(package => package.Name, StringComparer.Ordinal)
            .ToList();

        // An empty manifest is refused rather than returned. A pin listing no packages looks
        // like a record of what a version was approved against and is not one.
        return packages.Count > 0
            ? new ConformanceManifest(packages)
            : throw new ConformanceManifestException(
                $"{path}: the manifest pins no packages, so there is nothing to record.");
    }

    /// <summary>
    /// The packages whose vendored bytes no longer match the digest recorded for them, and the
    /// ones the directory no longer holds at all.
    /// </summary>
    /// <remarks>
    /// Reported rather than enforced (ADR-023 decision 5): where a reconstruction finds a
    /// mismatch, that is a material finding and the answer must say so. Refusing to answer
    /// would deny an inspection exactly the information it came for.
    /// </remarks>
    public IReadOnlyList<string> Discrepancies(string packagesDirectory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(packagesDirectory);

        var findings = new List<string>();

        foreach (var package in Packages)
        {
            var path = Path.Combine(packagesDirectory, package.File);
            if (!File.Exists(path))
            {
                findings.Add($"{package.Name} {package.Version}: {package.File} is not present.");
                continue;
            }

            using var stream = File.OpenRead(path);
            var digest = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(stream));
            if (!string.Equals(digest, package.Sha256, StringComparison.OrdinalIgnoreCase))
            {
                findings.Add(
                    $"{package.Name} {package.Version}: {package.File} hashes to {digest}, "
                    + $"and the manifest pins {package.Sha256}.");
            }
        }

        return findings;
    }

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
    };

    private sealed record ManifestFile(
        [property: JsonPropertyName("_comment")] string? Comment,
        string? Retrieved,
        string? Registry,
        IReadOnlyDictionary<string, PackageFile>? Packages,
        object? NotProvidedHere = null);

    private sealed record PackageFile(
        string? Version,
        string? File,
        [property: JsonPropertyName("sha256")] string? Sha256,
        long Bytes = 0,
        IReadOnlyList<string>? FhirVersions = null,
        string? License = null,
        IReadOnlyDictionary<string, string>? Dependencies = null,
        string? Source = null);
}

/// <summary>Raised when the pinned conformance manifest cannot be read.</summary>
public sealed class ConformanceManifestException(string message) : Exception(message);
