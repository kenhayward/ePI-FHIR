using Epi.ContentCore;
using Hl7.Fhir.Model;

namespace Epi.Lifecycle;

/// <summary>
/// Registers a version under lifecycle management before its content is written
/// (ADR-025 decision 2).
/// </summary>
/// <remarks>
/// The content store and the governance store are two systems with no shared transaction, so
/// the question is not how to make the pair atomic but which way round to fail. Writing content
/// first leaves content nobody is recorded as having authored - ungoverned content, readable
/// through every read path, in a system whose whole claim is that content is governed.
/// Registering first leaves a record that refers to nothing: every read returns not found and
/// every transition refuses, because scope is decided on the content. That one is inert.
/// <para>
/// A decorator, placed inside validation and scope and outside the store, so content that is
/// invalid or out of scope never reaches registration - and nothing is stored that was not
/// registered first (ADR-025 decision 3).
/// </para>
/// </remarks>
public sealed class RegisteringContentStore(
    IContentStore inner, LifecycleService lifecycle, string author) : IContentStore
{
    private readonly IContentStore _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    private readonly LifecycleService _lifecycle =
        lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));

    private readonly string _author = string.IsNullOrWhiteSpace(author)
        ? throw new ArgumentException(
            "A version must be registered against an author: the author is what segregation of "
            + "duties is checked against (CAP-IAM-006).",
            nameof(author))
        : author;

    public async Task<EpiDocument> CreateAsync(
        DocumentIdentity identity, Bundle bundle, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        await _lifecycle.RegisterAsync(new VersionRef(identity.Value, 1), _author, cancellationToken);
        return await _inner.CreateAsync(identity, bundle, cancellationToken);
    }

    public async Task<EpiDocument> CreateVersionAsync(
        DocumentIdentity identity, int version, Bundle bundle,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(identity);

        await _lifecycle.RegisterAsync(
            new VersionRef(identity.Value, version), _author, cancellationToken);
        return await _inner.CreateVersionAsync(identity, version, bundle, cancellationToken);
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
