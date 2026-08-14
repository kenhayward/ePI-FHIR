using System.Text;
using System.Text.Json;
using Confluent.Kafka;
using Epi.Contracts;
using Epi.Governance.Events;
using Testcontainers.Kafka;
using Xunit;

namespace Epi.Governance.IntegrationTests;

/// <summary>A real Kafka broker, the same image the development stack runs.</summary>
public sealed class KafkaBroker : IAsyncLifetime
{
    /// <summary>The image the development stack runs (deploy/docker-compose).</summary>
    private const string Image = "apache/kafka:3.8.0";

    /// <remarks>
    /// The Testcontainers Kafka module rather than a hand-built container, because a broker
    /// needs its advertised listeners rewritten to the mapped host port after it starts, and
    /// that cannot be done with environment variables alone - the port is not known until the
    /// container is running. Configuring it by hand without them is what made these tests hang:
    /// the broker advertised an address only reachable inside the container, the client
    /// connected, received unusable metadata, and every produce sat there until librdkafka's
    /// five-minute message timeout expired.
    /// </remarks>
    private readonly KafkaContainer _container = new KafkaBuilder(Image).Build();

    public string BootstrapServers => _container.GetBootstrapAddress().Replace("PLAINTEXT://", "");

    public Task InitializeAsync() => _container.StartAsync();

    public Task DisposeAsync() => _container.DisposeAsync().AsTask();
}

[CollectionDefinition(Name)]
public sealed class KafkaCollection : ICollectionFixture<KafkaBroker>
{
    public const string Name = "kafka";
}

/// <summary>
/// Events published to a real broker and read back off it. An in-memory publisher cannot tell
/// you whether the message survived serialisation, reached a partition, or carried its headers.
/// </summary>
[Collection(KafkaCollection.Name)]
[Trait("Category", "Container")]
public sealed class KafkaEventPublisherTests(KafkaBroker broker)
{
    /// <summary>
    /// Short on purpose. A broker these tests cannot reach should fail in seconds; librdkafka's
    /// five-minute default made three broken tests look like a sixteen-minute CI job.
    /// </summary>
    private static readonly TimeSpan PublishTimeout = TimeSpan.FromSeconds(20);

    private static ContentEvent Event(string identifier = "doc-1", int version = 1) => new(
        ContentEvent.Created, identifier, "https://epi.example.org/identifier/document",
        version, "uk-affiliate", "GB", DateTimeOffset.UtcNow);

    private IConsumer<string, string> Subscribe(string topic)
    {
        var consumer = new ConsumerBuilder<string, string>(new ConsumerConfig
        {
            BootstrapServers = broker.BootstrapServers,
            GroupId = $"test-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
        }).Build();

        consumer.Subscribe(topic);
        return consumer;
    }

    [Fact]
    public async Task FN_EVT_002_an_event_published_can_be_read_back_off_the_broker()
    {
        var topic = $"epi.content.{Guid.NewGuid():N}";
        using var consumer = Subscribe(topic);
        using var publisher = new KafkaEventPublisher(broker.BootstrapServers, topic, PublishTimeout);

        await publisher.PublishAsync(Event());

        var message = consumer.Consume(TimeSpan.FromSeconds(30));
        Assert.NotNull(message);

        var received = JsonSerializer.Deserialize<ContentEvent>(
            message!.Message.Value, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.Equal(ContentEvent.Created, received!.Type);
        Assert.Equal("doc-1", received.DocumentIdentifier);
        Assert.Equal("uk-affiliate", received.Affiliate);
    }

    [Fact]
    public async Task FN_EVT_002_the_type_and_schema_version_travel_as_headers()
    {
        // So a consumer can route or reject by type without deserialising a payload it may
        // not understand (CAP-EVT-006).
        var topic = $"epi.content.{Guid.NewGuid():N}";
        using var consumer = Subscribe(topic);
        using var publisher = new KafkaEventPublisher(broker.BootstrapServers, topic, PublishTimeout);

        await publisher.PublishAsync(Event());

        var message = consumer.Consume(TimeSpan.FromSeconds(30));
        Assert.NotNull(message);

        var headers = message!.Message.Headers.ToDictionary(
            h => h.Key, h => Encoding.UTF8.GetString(h.GetValueBytes()));
        Assert.Equal(ContentEvent.Created, headers["event-type"]);
        Assert.Equal(ContentEvent.CurrentSchemaVersion, headers["schema-version"]);
    }

    [Fact]
    public async Task FN_EVT_002_events_about_one_document_are_keyed_by_it_so_they_stay_in_order()
    {
        // A consumer must not see version 2 before version 1. Keying by document puts every
        // event about it on one partition, which is what makes that true.
        var topic = $"epi.content.{Guid.NewGuid():N}";
        using var consumer = Subscribe(topic);
        using var publisher = new KafkaEventPublisher(broker.BootstrapServers, topic, PublishTimeout);

        await publisher.PublishAsync(Event("doc-7", version: 1));
        await publisher.PublishAsync(Event("doc-7", version: 2) with { Type = ContentEvent.VersionCreated });

        var first = consumer.Consume(TimeSpan.FromSeconds(30));
        var second = consumer.Consume(TimeSpan.FromSeconds(30));

        Assert.Equal("doc-7", first!.Message.Key);
        Assert.Equal("doc-7", second!.Message.Key);
        Assert.Equal(first.Partition, second.Partition);
    }
}
