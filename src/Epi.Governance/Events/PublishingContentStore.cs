using Epi.ContentCore;
using Epi.Contracts;
using Hl7.Fhir.Model;

namespace Epi.Governance.Events;

/// <summary>
/// Emits a content event after a successful write (FN-EVT-001, FN-EVT-002, CAP-EVT-001).
/// </summary>
/// <remarks>
/// After, deliberately: an event announcing a write that then failed would have consumers
/// reacting to content that does not exist. Publication failure does not fail the write either
/// - the content is stored and the audit record exists, and losing a notification is
/// recoverable where losing content is not. At-least-once delivery and dead-lettering
/// (CAP-EVT-005) belong to the broker adapter, not here.
/// </remarks>
public sealed class PublishingContentStore(
    IContentStore inner, IEventPublisher publisher, TimeProvider? time = null) : IContentStore
{
    private readonly IContentStore _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IEventPublisher _publisher = publisher ?? throw new ArgumentNullException(nameof(publisher));
    private readonly TimeProvider _time = time ?? TimeProvider.System;

    public async Task<EpiDocument> CreateAsync(Bundle bundle, CancellationToken cancellationToken = default)
    {
        var stored = await _inner.CreateAsync(bundle, cancellationToken);
        await Announce(ContentEvent.Created, stored, cancellationToken);
        return stored;
    }

    public async Task<EpiDocument> CreateVersionAsync(
        DocumentIdentity identity, Bundle bundle, CancellationToken cancellationToken = default)
    {
        var stored = await _inner.CreateVersionAsync(identity, bundle, cancellationToken);
        await Announce(ContentEvent.VersionCreated, stored, cancellationToken);
        return stored;
    }

    public Task<EpiDocument?> GetAsync(
        DocumentIdentity identity, int version, CancellationToken cancellationToken = default) =>
        _inner.GetAsync(identity, version, cancellationToken);

    public Task<EpiDocument?> GetLatestAsync(
        DocumentIdentity identity, CancellationToken cancellationToken = default) =>
        _inner.GetLatestAsync(identity, cancellationToken);

    public Task<IReadOnlyList<int>> VersionsAsync(
        DocumentIdentity identity, CancellationToken cancellationToken = default) =>
        _inner.VersionsAsync(identity, cancellationToken);

    private async Task Announce(string type, EpiDocument stored, CancellationToken cancellationToken)
    {
        var scope = ContentScope.Of(stored.Bundle);

        await _publisher.PublishAsync(new ContentEvent(
            type,
            stored.Identity.Value,
            stored.Identity.System,
            stored.Version,
            scope?.Affiliate ?? string.Empty,
            scope?.Market ?? string.Empty,
            _time.GetUtcNow()), cancellationToken);
    }
}
