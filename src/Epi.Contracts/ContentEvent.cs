namespace Epi.Contracts;

/// <summary>
/// Something that happened to canonical content, published for anyone who cares (capability 20).
/// </summary>
/// <remarks>
/// <para>
/// A notification, not a carrier: it names what changed and where to fetch it, rather than
/// embedding the document. Regulated content is large, it is scoped (capability 17), and a
/// consumer entitled to hear that a label changed is not necessarily entitled to read it. An
/// event that carried the content would route around the authorisation the platform applies on
/// read.
/// </para>
/// <para>
/// The schema is versioned from the first event rather than from the first breaking change,
/// because consumers outside this repository cannot be redeployed in step with it
/// (CAP-EVT-006).
/// </para>
/// </remarks>
public sealed record ContentEvent(
    string Type,
    string DocumentIdentifier,
    string DocumentSystem,
    int Version,
    string Affiliate,
    string Market,
    DateTimeOffset OccurredAt,
    string SchemaVersion = ContentEvent.CurrentSchemaVersion)
{
    public const string CurrentSchemaVersion = "1";

    public const string Created = "content.created";
    public const string VersionCreated = "content.version-created";
}

/// <summary>
/// The event backbone (capability 20). Kafka in the deployed platform (ADR-009); the interface
/// is what the domain depends on so the broker is an adapter rather than a coupling.
/// </summary>
public interface IEventPublisher
{
    Task PublishAsync(ContentEvent published, CancellationToken cancellationToken = default);
}

/// <summary>
/// An in-memory publisher. The reference implementation the same tests hold a broker-backed one
/// to, and what the API uses until the broker is wired in.
/// </summary>
public sealed class InMemoryEventPublisher : IEventPublisher
{
    private readonly List<ContentEvent> _published = [];
    private readonly Lock _gate = new();

    public IReadOnlyList<ContentEvent> Published
    {
        get { lock (_gate) { return [.. _published]; } }
    }

    public Task PublishAsync(ContentEvent published, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(published);
        lock (_gate)
        {
            _published.Add(published);
        }

        return Task.CompletedTask;
    }
}
