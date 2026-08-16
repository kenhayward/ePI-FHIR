using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Amazon.S3;
using Amazon.S3.Model;

namespace Epi.Rendering;

/// <summary>
/// The asset store on an S3-compatible object store, which in this platform is MinIO (ADR-013,
/// ADR-034). Write-once comes from the object store, not from here.
/// </summary>
/// <remarks>
/// <para>
/// Two mechanisms, answering two different questions (ADR-034 decision 1). Every write carries
/// <c>If-None-Match: *</c>, so a second write of a key is refused by the object store with a 412
/// whether or not the caller came through this class. Every write also carries COMPLIANCE
/// retention, so the version that was accepted cannot afterwards be deleted or altered by
/// anybody, including the credential that wrote it.
/// </para>
/// <para>
/// Object-lock alone would not do. It protects a version, not a key: an unconditional overwrite
/// is accepted, creates a new version and becomes what an ordinary read returns, leaving the
/// retained original undamaged and unreachable to anyone not asking for it by version. That was
/// measured, and it is recorded in ADR-034 because the design had assumed otherwise.
/// </para>
/// <para>
/// This speaks S3 rather than MinIO, which is what makes D3's claim that moving to another
/// immutable object store is configuration rather than redesign true rather than aspirational.
/// </para>
/// </remarks>
public sealed class ObjectStoreAssetStore : IAssetStore
{
    /// <summary>
    /// What a stored object needs to carry to come back as the type it went in as.
    /// </summary>
    /// <remarks>
    /// Held as one base64 metadata header rather than a field each. Object metadata has to be
    /// ASCII and an agency name has no obligation to be, so encoding it once beats discovering
    /// the limit the first time a real name is stored. Reconstructing from the key instead was
    /// the alternative and is worse: the key does not carry the identifier system, and parsing
    /// a path back into an identity is a decoder nobody maintains.
    /// </remarks>
    private sealed record Descriptor(
        [property: JsonPropertyName("lineage")] string Lineage,
        [property: JsonPropertyName("identitySystem")] string? IdentitySystem = null,
        [property: JsonPropertyName("identityValue")] string? IdentityValue = null,
        [property: JsonPropertyName("labelVersion")] int LabelVersion = 0,
        [property: JsonPropertyName("renderTemplate")] string? RenderTemplate = null,
        [property: JsonPropertyName("renderTemplateVersion")] int RenderTemplateVersion = 0,
        [property: JsonPropertyName("draft")] bool Draft = false,
        [property: JsonPropertyName("source")] string? Source = null,
        [property: JsonPropertyName("reference")] string? Reference = null);

    private const string DescriptorKey = "epi-descriptor";

    private readonly IAmazonS3 _client;
    private readonly string _bucket;
    private readonly AssetRetention _retention;
    private readonly TimeProvider _clock;

    /// <param name="clock">
    /// Handed in rather than read from the ambient one. A retention deadline is the only thing
    /// in this project that needs to know the time, and requiring it here keeps it out of reach
    /// of the renderers, whose output must not depend on when it was produced (ADR-033).
    /// </param>
    public ObjectStoreAssetStore(
        IAmazonS3 client, string bucket, AssetRetention retention, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentException.ThrowIfNullOrWhiteSpace(bucket);
        ArgumentNullException.ThrowIfNull(retention);
        ArgumentNullException.ThrowIfNull(clock);

        _client = client;
        _bucket = bucket;
        _retention = retention;
        _clock = clock;
    }

    /// <summary>Where an asset key lands in the bucket: the lineage is the prefix.</summary>
    public static string ObjectKey(AssetKey key)
    {
        ArgumentNullException.ThrowIfNull(key);
        return key.ToString();
    }

    public async Task PutAsync(
        AssetKey key, LabelDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ArgumentNullException.ThrowIfNull(document);

        var request = new PutObjectRequest
        {
            BucketName = _bucket,
            Key = ObjectKey(key),
            InputStream = new MemoryStream(document.Content),
            ContentType = document.MediaType,

            // The object store refuses the second write of a key, so the guarantee does not
            // depend on this code being the only way in (ADR-034 decision 1).
            IfNoneMatch = "*",

            ObjectLockMode = ObjectLockMode.Compliance,
            ObjectLockRetainUntilDate =
                _clock.GetUtcNow().Add(_retention.For(key.Lineage)).UtcDateTime,
        };

        request.Metadata.Add(DescriptorKey, Encode(document, key.Lineage));

        try
        {
            await _client.PutObjectAsync(request, cancellationToken);
        }
        catch (AmazonS3Exception refusal) when (refusal.StatusCode == HttpStatusCode.PreconditionFailed)
        {
            throw new AssetAlreadyStoredException(key);
        }
    }

    public async Task<LabelDocument?> GetAsync(
        AssetKey key, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(key);

        try
        {
            using var response = await _client.GetObjectAsync(
                _bucket, ObjectKey(key), cancellationToken);

            using var buffer = new MemoryStream();
            await response.ResponseStream.CopyToAsync(buffer, cancellationToken);

            return Decode(
                response.Metadata[DescriptorKey],
                response.Headers.ContentType,
                buffer.ToArray());
        }
        catch (AmazonS3Exception missing) when (missing.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<IReadOnlyList<AssetKey>> ListAsync(
        string lineage, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(lineage);

        var prefix = lineage + "/";
        var keys = new List<AssetKey>();
        string? token = null;

        do
        {
            var page = await _client.ListObjectsV2Async(
                new ListObjectsV2Request
                {
                    BucketName = _bucket,
                    Prefix = prefix,
                    ContinuationToken = token,
                },
                cancellationToken);

            keys.AddRange(page.S3Objects.Select(o => new AssetKey(lineage, o.Key[prefix.Length..])));
            token = page.IsTruncated == true ? page.NextContinuationToken : null;
        }
        while (token is not null);

        keys.Sort((left, right) => string.CompareOrdinal(left.Path, right.Path));
        return keys;
    }

    private static string Encode(LabelDocument document, string lineage)
    {
        var descriptor = document switch
        {
            RenderedDocument rendered => new Descriptor(
                lineage,
                rendered.Label.System,
                rendered.Label.Value,
                rendered.LabelVersion,
                rendered.RenderTemplate,
                rendered.RenderTemplateVersion,
                rendered.Draft),
            ArtworkDocument artwork => new Descriptor(
                lineage, Source: artwork.Source, Reference: artwork.Reference),
            _ => throw new ArgumentOutOfRangeException(
                nameof(document),
                $"There is no third lineage, and {document.GetType().Name} is neither of the two "
                + "(D1 Section 3.3, ADR-033 decision 6)."),
        };

        return Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(descriptor));
    }

    private static LabelDocument Decode(string? encoded, string mediaType, byte[] content)
    {
        if (string.IsNullOrWhiteSpace(encoded))
        {
            // Something wrote into this bucket without going through here, and guessing which
            // lineage it belongs to is exactly the interchange the design forbids.
            throw new AssetDescriptorException(
                "A stored object carries no lineage descriptor, so what it is cannot be "
                + "established. It was not written by this platform.");
        }

        var descriptor = JsonSerializer.Deserialize<Descriptor>(
            Encoding.UTF8.GetString(Convert.FromBase64String(encoded)))
            ?? throw new AssetDescriptorException("A stored object's lineage descriptor is empty.");

        return descriptor.Lineage switch
        {
            AssetKey.RenderedLineage => new RenderedDocument(
                mediaType,
                content,
                new DocumentIdentityRef(descriptor.IdentitySystem!, descriptor.IdentityValue!),
                descriptor.LabelVersion,
                descriptor.RenderTemplate!,
                descriptor.RenderTemplateVersion,
                descriptor.Draft),
            AssetKey.ArtworkLineage => new ArtworkDocument(
                mediaType, content, descriptor.Source!, descriptor.Reference!),
            _ => throw new AssetDescriptorException(
                $"A stored object claims the '{descriptor.Lineage}' lineage, and there are two."),
        };
    }
}

/// <summary>Raised when a stored object cannot be established as either lineage.</summary>
public sealed class AssetDescriptorException(string message) : Exception(message);
