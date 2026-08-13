using System.Text.Json;
using Confluent.Kafka;
using Epi.Contracts;

namespace Epi.Governance.Events;

/// <summary>
/// Publishes content events to Kafka, the event backbone (ADR-009, CAP-EVT-001).
/// </summary>
/// <remarks>
/// <para>
/// The message key is the document identifier, so every event about one document lands on the
/// same partition and is delivered in order. Ordering across documents is neither guaranteed
/// nor needed; ordering within a document is, because a consumer must not see version 2 before
/// version 1 (CAP-EVT-005).
/// </para>
/// <para>
/// The producer waits for acknowledgement from the replicas before reporting success. A
/// publisher that returns as soon as the message is buffered would report delivery it cannot
/// know about, and the caller has no way to notice the difference until events are missing.
/// </para>
/// </remarks>
public sealed class KafkaEventPublisher : IEventPublisher, IDisposable
{
    public const string DefaultTopic = "epi.content";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IProducer<string, string> _producer;
    private readonly string _topic;

    public KafkaEventPublisher(string bootstrapServers, string? topic = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bootstrapServers);

        _topic = topic ?? DefaultTopic;
        _producer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = bootstrapServers,
            // Acknowledged by all in-sync replicas, and retried idempotently, so a retry after
            // a timeout cannot produce a duplicate that consumers would process twice.
            Acks = Acks.All,
            EnableIdempotence = true,
        }).Build();
    }

    public async Task PublishAsync(ContentEvent published, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(published);

        await _producer.ProduceAsync(_topic, new Message<string, string>
        {
            Key = published.DocumentIdentifier,
            Value = JsonSerializer.Serialize(published, Json),
            Headers =
            [
                // On the message rather than only in the body, so a consumer can route or
                // reject by type and schema without deserialising a payload it may not
                // understand (CAP-EVT-006).
                new Header("event-type", System.Text.Encoding.UTF8.GetBytes(published.Type)),
                new Header("schema-version", System.Text.Encoding.UTF8.GetBytes(published.SchemaVersion)),
            ],
        }, cancellationToken);
    }

    public void Dispose()
    {
        // Flush before disposing: buffered events are events the platform has already told a
        // caller were published.
        _producer.Flush(TimeSpan.FromSeconds(10));
        _producer.Dispose();
    }
}
