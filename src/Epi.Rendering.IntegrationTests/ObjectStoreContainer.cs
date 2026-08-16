using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;
using Xunit;

namespace Epi.Rendering.IntegrationTests;

/// <summary>A real object store, on the image the development stack pins.</summary>
/// <remarks>
/// Readiness is polled rather than declared with <c>WithWaitStrategy</c>, for the reason recorded
/// on <see cref="PrintEngineContainer"/>: a wait strategy on a container built inside an xUnit
/// collection fixture crashes the VSTest host on Testcontainers 4.13.0.
/// </remarks>
public sealed class ObjectStoreContainer : IAsyncLifetime
{
    /// <summary>The image the development stack runs (deploy/docker-compose).</summary>
    private const string Image = "minio/minio:RELEASE.2025-09-07T16-13-09Z";

    private const string RootUser = "epi-test-root";

    private const string RootPassword = "epi-test-root-password";

    private readonly IContainer _container = new ContainerBuilder(Image)
        .WithCommand("server", "/data")
        .WithEnvironment("MINIO_ROOT_USER", RootUser)
        .WithEnvironment("MINIO_ROOT_PASSWORD", RootPassword)
        .WithPortBinding(9000, assignRandomHostPort: true)
        .Build();

    public IAmazonS3 Client { get; private set; } = null!;

    /// <summary>
    /// A bucket per test, so one test's write-once refusal is not another test's stale object.
    /// Object-lock is enabled at creation because S3 does not allow enabling it afterwards.
    /// </summary>
    public async Task<string> CreateBucketAsync(bool objectLock = true)
    {
        var name = $"epi-{Guid.NewGuid():n}";

        await Client.PutBucketAsync(new PutBucketRequest
        {
            BucketName = name,
            ObjectLockEnabledForBucket = objectLock,
        });

        return name;
    }

    public async Task InitializeAsync()
    {
        await _container.StartAsync();

        Client = new AmazonS3Client(
            RootUser,
            RootPassword,
            new AmazonS3Config
            {
                ServiceURL = $"http://{_container.Hostname}:{_container.GetMappedPublicPort(9000)}",
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1",
            });

        var deadline = DateTimeOffset.UtcNow.AddMinutes(2);
        while (true)
        {
            try
            {
                await Client.ListBucketsAsync();
                return;
            }
            catch (Exception) when (DateTimeOffset.UtcNow < deadline)
            {
                // Still starting.
            }

            if (DateTimeOffset.UtcNow >= deadline)
            {
                throw new TimeoutException($"{Image} did not become ready within two minutes.");
            }

            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }

    public async Task DisposeAsync()
    {
        Client?.Dispose();
        await _container.DisposeAsync();
    }
}

[CollectionDefinition(ObjectStoreCollection.Name)]
public sealed class ObjectStoreCollection : ICollectionFixture<ObjectStoreContainer>
{
    public const string Name = "object-store";
}
