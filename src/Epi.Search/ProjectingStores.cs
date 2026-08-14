using Epi.ContentCore;
using Epi.Lifecycle;
using Hl7.Fhir.Model;

namespace Epi.Search;

/// <summary>
/// Feeds stored content into the search projection (ADR-022 decision 6).
/// </summary>
/// <remarks>
/// A decorator, for the same reason auditing is one: a write path that has to remember to
/// update the index is a write path that will one day forget, and the symptom is a document
/// nobody can find rather than an error anybody sees.
/// <para>
/// The projection is updated synchronously and in process, so it cannot lag while the platform
/// is one process. Moving it onto the event backbone makes it eventually consistent, which is
/// what ADR-022 decision 8 exists to survive.
/// </para>
/// </remarks>
public sealed class ProjectingContentStore(
    IContentStore inner, ISearchProjection projection, string initialState) : IContentStore
{
    private readonly IContentStore _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly ISearchProjection _projection =
        projection ?? throw new ArgumentNullException(nameof(projection));

    public async Task<EpiDocument> CreateAsync(
        Bundle bundle, CancellationToken cancellationToken = default)
    {
        var stored = await _inner.CreateAsync(bundle, cancellationToken);
        await _projection.ProjectAsync(stored, initialState, cancellationToken);
        return stored;
    }

    public async Task<EpiDocument> CreateVersionAsync(
        DocumentIdentity identity, Bundle bundle, CancellationToken cancellationToken = default)
    {
        var stored = await _inner.CreateVersionAsync(identity, bundle, cancellationToken);
        await _projection.ProjectAsync(stored, initialState, cancellationToken);
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
}

/// <summary>
/// Feeds lifecycle state into the search projection, so that "awaiting approval in my market"
/// is answerable (ADR-022 decision 6).
/// </summary>
/// <remarks>
/// Wraps the store rather than the service, so a transition recorded by any engine - or by a
/// future one this decorator has never heard of - still reaches the projection.
/// </remarks>
public sealed class ProjectingLifecycleStore(
    ILifecycleStore inner, ISearchProjection projection) : ILifecycleStore
{
    private readonly ILifecycleStore _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly ISearchProjection _projection =
        projection ?? throw new ArgumentNullException(nameof(projection));

    public async Task RegisterAsync(
        VersionRef version, string author, string initialState,
        CancellationToken cancellationToken = default)
    {
        await _inner.RegisterAsync(version, author, initialState, cancellationToken);
        await _projection.ProjectStateAsync(version, initialState, cancellationToken);
    }

    public async Task AppendAsync(
        StateTransition transition, CancellationToken cancellationToken = default)
    {
        await _inner.AppendAsync(transition, cancellationToken);
        await _projection.ProjectStateAsync(transition.Version, transition.To, cancellationToken);
    }

    public Task<string?> AuthorOfAsync(VersionRef version, CancellationToken cancellationToken = default) =>
        _inner.AuthorOfAsync(version, cancellationToken);

    public Task<string?> CurrentStateAsync(VersionRef version, CancellationToken cancellationToken = default) =>
        _inner.CurrentStateAsync(version, cancellationToken);

    public Task<IReadOnlyList<StateTransition>> HistoryAsync(
        VersionRef version, CancellationToken cancellationToken = default) =>
        _inner.HistoryAsync(version, cancellationToken);

    public Task<bool> IsSignatureUsedAsync(string reference, CancellationToken cancellationToken = default) =>
        _inner.IsSignatureUsedAsync(reference, cancellationToken);
}
