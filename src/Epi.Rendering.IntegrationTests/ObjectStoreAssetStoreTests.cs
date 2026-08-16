using System.Net;
using System.Text;
using Amazon.S3;
using Amazon.S3.Model;
using Epi.Rendering.Tests;
using Xunit;

namespace Epi.Rendering.IntegrationTests;

// The durable asset store, held to the same suite the in-memory one answers (ADR-034 decision 5).
//   CAP-RND-002 Store rendered output and ingested artwork, kept apart and write-once
[Collection(ObjectStoreCollection.Name)]
[Trait("Category", "Container")]
public sealed class ObjectStoreAssetStoreConformanceTests(ObjectStoreContainer store)
    : AssetStoreConformance
{
    protected override async Task<IAssetStore> CreateStoreAsync() =>
        new ObjectStoreAssetStore(
            store.Client,
            await store.CreateBucketAsync(),
            AssetRetention.Load(RetentionConfig.Path),
            TimeProvider.System);
}

// The two object-store behaviours the design rests on (ADR-034 decision 6).
//
// Asserted directly rather than only through the conformance suite, because they are properties
// of the object store rather than of our code. If a future release accepted a conditional
// overwrite, the suite would still pass - our code has no dictionary to fall back on and would
// simply stop being write-once - so the fact is checked where a change in it fails loudly.
[Collection(ObjectStoreCollection.Name)]
[Trait("Category", "Container")]
public sealed class ObjectStoreGuaranteeTests(ObjectStoreContainer store)
{
    private const string Key = "rendered/label/3/final.pdf";

    private static PutObjectRequest Put(string bucket, string body) => new()
    {
        BucketName = bucket,
        Key = Key,
        InputStream = new MemoryStream(Encoding.ASCII.GetBytes(body)),
        ContentType = "application/pdf",
    };

    [Fact]
    public async Task CAP_RND_002_the_object_store_refuses_a_conditional_write_over_an_existing_key()
    {
        var bucket = await store.CreateBucketAsync();
        await store.Client.PutObjectAsync(Put(bucket, "the one that was filed"));

        var second = Put(bucket, "something else");
        second.IfNoneMatch = "*";

        var refusal = await Assert.ThrowsAsync<AmazonS3Exception>(
            () => store.Client.PutObjectAsync(second));

        Assert.Equal(HttpStatusCode.PreconditionFailed, refusal.StatusCode);
    }

    [Fact]
    public async Task CAP_RND_002_object_lock_alone_does_not_make_a_key_write_once()
    {
        // The measurement in ADR-034, kept as a test so the correction cannot be forgotten
        // twice. Retention protects a version, not a key: an unconditional overwrite is
        // accepted and a read afterwards returns the second object. This is why the store
        // writes conditionally, and asserting it here is what stops somebody concluding from
        // "the bucket has object-lock" that the conditional write is redundant.
        var bucket = await store.CreateBucketAsync();

        var first = Put(bucket, "the one that was filed");
        first.ObjectLockMode = ObjectLockMode.Compliance;
        first.ObjectLockRetainUntilDate = DateTime.UtcNow.AddDays(1);
        await store.Client.PutObjectAsync(first);

        await store.Client.PutObjectAsync(Put(bucket, "something else"));

        using var read = await store.Client.GetObjectAsync(bucket, Key);
        using var reader = new StreamReader(read.ResponseStream);
        Assert.Equal("something else", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task CAP_RND_002_a_retained_version_cannot_be_deleted()
    {
        var bucket = await store.CreateBucketAsync();

        var request = Put(bucket, "the one that was filed");
        request.ObjectLockMode = ObjectLockMode.Compliance;
        request.ObjectLockRetainUntilDate = DateTime.UtcNow.AddDays(1);
        var written = await store.Client.PutObjectAsync(request);

        await Assert.ThrowsAsync<AmazonS3Exception>(
            () => store.Client.DeleteObjectAsync(new DeleteObjectRequest
            {
                BucketName = bucket,
                Key = Key,
                VersionId = written.VersionId,
            }));

        using var read = await store.Client.GetObjectAsync(bucket, Key);
        using var reader = new StreamReader(read.ResponseStream);
        Assert.Equal("the one that was filed", await reader.ReadToEndAsync());
    }

    [Fact]
    public async Task CAP_RND_002_what_the_store_writes_carries_the_configured_retention()
    {
        // Retention that is configured and never applied is the defect ADR-034 found in the
        // development stack's own bucket setup, and it looks exactly like retention that works.
        var retention = AssetRetention.Load(RetentionConfig.Path);
        var bucket = await store.CreateBucketAsync();
        var assets = new ObjectStoreAssetStore(store.Client, bucket, retention, TimeProvider.System);
        var rendered = new RenderedDocument(
            "application/pdf",
            Encoding.ASCII.GetBytes("%PDF-1.4 synthetic"),
            new DocumentIdentityRef(
                "https://epi.example.org/identifier/document",
                "01a00000-0000-7000-8000-00000000000a"),
            3,
            "qrd-leaflet",
            2);

        await assets.PutAsync(AssetKey.For(rendered), rendered);

        var head = await store.Client.GetObjectMetadataAsync(
            bucket, ObjectStoreAssetStore.ObjectKey(AssetKey.For(rendered)));

        Assert.Equal(ObjectLockMode.Compliance, head.ObjectLockMode);
        Assert.True(
            head.ObjectLockRetainUntilDate > DateTime.UtcNow.AddDays(3649),
            $"retention should run to the configured period, and runs to {head.ObjectLockRetainUntilDate:O}");
    }
}

/// <summary>Where the retention configuration lives, relative to the repository root.</summary>
internal static class RetentionConfig
{
    public static string Path { get; } = System.IO.Path.GetFullPath(
        System.IO.Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "config", "assets", "retention.json"));
}
