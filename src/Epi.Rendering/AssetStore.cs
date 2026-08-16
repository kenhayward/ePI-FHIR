namespace Epi.Rendering;

/// <summary>
/// Where a stored artefact lives, and what it is (D3 Section 3.2, ADR-033 decisions 5 and 6).
/// </summary>
/// <remarks>
/// The lineage is the first component of the key, so the two never share a namespace and a
/// listing of one cannot return the other. That is belt to the braces of their being separate
/// types: the type stops a mix-up in code, and the key stops one in the bucket.
/// </remarks>
public sealed record AssetKey(string Lineage, string Path)
{
    public const string RenderedLineage = "rendered";

    public const string ArtworkLineage = "artwork";

    /// <summary>
    /// Where a render lives: under the label version that produced it and the template version
    /// that shaped it.
    /// </summary>
    /// <remarks>
    /// Both versions are in the key because both are inputs to the bytes (ADR-033 decision 1).
    /// A key naming only the label version would collide the moment a template was revised, and
    /// the second render would either overwrite the first or be refused - neither of which is
    /// what anybody meant.
    /// </remarks>
    public static AssetKey For(RenderedDocument rendered)
    {
        ArgumentNullException.ThrowIfNull(rendered);

        var extension = rendered.MediaType.StartsWith("application/pdf", StringComparison.Ordinal)
            ? "pdf"
            : "html";

        return new AssetKey(
            RenderedLineage,
            $"{rendered.Label.Value}/{rendered.LabelVersion}/"
            + $"{rendered.RenderTemplate}/{rendered.RenderTemplateVersion}/"
            + $"{(rendered.Draft ? "draft" : "final")}.{extension}");
    }

    /// <summary>
    /// Where artwork lives: under whoever produced it and their own reference for it.
    /// </summary>
    /// <remarks>
    /// No label version and no template, because nothing here produced it. Its identity is the
    /// agency's, which is exactly what makes it a different lineage rather than a different flag.
    /// </remarks>
    public static AssetKey For(ArtworkDocument artwork)
    {
        ArgumentNullException.ThrowIfNull(artwork);
        return new AssetKey(ArtworkLineage, $"{artwork.Source}/{artwork.Reference}");
    }

    public override string ToString() => $"{Lineage}/{Path}";
}

/// <summary>
/// The asset store: rendered output and ingested artwork, kept apart (CAP-RND-002).
/// </summary>
/// <remarks>
/// Write-once. An artefact that could be replaced is an artefact nobody can cite, and a render
/// that needs to change is a new render of a new version.
///
/// This used to say the durable implementation would get write-once "from object-lock rather
/// than from a check in application code". Half right, and the wrong half was load-bearing:
/// object-lock protects a version, not a key, and an unconditional overwrite of a retained
/// object is accepted and becomes what a read returns. Write-once at a key comes from the
/// conditional write; object-lock is what stops the accepted version being destroyed
/// afterwards. Both, and neither substitutes for the other - see ADR-034, which records the
/// measurement.
/// </remarks>
public interface IAssetStore
{
    /// <summary>Stores an artefact under the key its own lineage gives it.</summary>
    /// <exception cref="AssetAlreadyStoredException">
    /// If something is already there. Write-once, and silence would be worse than a refusal: a
    /// replaced render is one that no longer matches what was filed against it.
    /// </exception>
    Task PutAsync(AssetKey key, LabelDocument document, CancellationToken cancellationToken = default);

    /// <summary>The artefact at this key, or null if there is none.</summary>
    Task<LabelDocument?> GetAsync(AssetKey key, CancellationToken cancellationToken = default);

    /// <summary>Every key held under one lineage, so a listing cannot cross into the other.</summary>
    Task<IReadOnlyList<AssetKey>> ListAsync(
        string lineage, CancellationToken cancellationToken = default);
}

/// <summary>Raised when something is already stored where an artefact would go.</summary>
public sealed class AssetAlreadyStoredException(AssetKey key)
    : Exception($"An artefact is already stored at {key}, and the asset store is write-once.")
{
    public AssetKey Key { get; } = key;
}

/// <summary>
/// An in-memory asset store: the reference implementation the conformance suite holds every
/// implementation to. Real storage is <see cref="ObjectStoreAssetStore"/>, which answers the
/// same suite against MinIO and gets its refusals from the object store rather than from here.
/// </summary>
public sealed class InMemoryAssetStore : IAssetStore
{
    private readonly Dictionary<string, LabelDocument> _assets = [];
    private readonly Lock _gate = new();

    public Task PutAsync(
        AssetKey key, LabelDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(document);

        lock (_gate)
        {
            if (!_assets.TryAdd(key.ToString(), document))
            {
                throw new AssetAlreadyStoredException(key);
            }
        }

        return Task.CompletedTask;
    }

    public Task<LabelDocument?> GetAsync(AssetKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        lock (_gate)
        {
            return Task.FromResult(_assets.GetValueOrDefault(key.ToString()));
        }
    }

    public Task<IReadOnlyList<AssetKey>> ListAsync(
        string lineage, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineage);

        var prefix = lineage + "/";

        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<AssetKey>>(
            [
                .. _assets.Keys
                    .Where(k => k.StartsWith(prefix, StringComparison.Ordinal))
                    .Order(StringComparer.Ordinal)
                    .Select(k => new AssetKey(lineage, k[prefix.Length..])),
            ]);
        }
    }
}
