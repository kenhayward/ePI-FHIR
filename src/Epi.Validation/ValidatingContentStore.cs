using Epi.ContentCore;
using Hl7.Fhir.Model;

namespace Epi.Validation;

/// <summary>
/// The write gate (CAP-VAL-004): content is validated before it reaches the store, so
/// rejected content leaves no trace behind it.
/// </summary>
/// <remarks>
/// A decorator rather than logic inside a store or an endpoint. Validation is then on the
/// write path by construction, applies to every store implementation equally, and cannot be
/// bypassed by a caller who reaches for the inner store's method directly.
/// </remarks>
public sealed class ValidatingContentStore(IContentStore inner, StructuralValidator validator) : IContentStore
{
    private readonly IContentStore _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly StructuralValidator _validator =
        validator ?? throw new ArgumentNullException(nameof(validator));

    public async Task<EpiDocument> CreateAsync(
        DocumentIdentity identity, Bundle bundle, CancellationToken cancellationToken = default)
    {
        await GateAsync(bundle, cancellationToken);
        return await _inner.CreateAsync(identity, bundle, cancellationToken);
    }

    public async Task<EpiDocument> CreateVersionAsync(
        DocumentIdentity identity, int version, Bundle bundle, CancellationToken cancellationToken = default)
    {
        await GateAsync(bundle, cancellationToken);
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

    /// <summary>Throws before the store is touched, so a rejection cannot leave partial state.</summary>
    /// <remarks>
    /// Validates the <em>stamped</em> form. FHIR requires a document Bundle to carry an
    /// identifier (constraint bdl-9) and the platform mints that identifier itself (ADR-015),
    /// so validating the draft as submitted would fail every time on an identifier the
    /// submitter is not allowed to provide. A provisional stamp is applied to a copy: any
    /// identity satisfies the constraint, and the real one is applied by the store.
    /// </remarks>
    private async Task GateAsync(Bundle bundle, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var candidate = ContentIdentity.Stamp(
            (Bundle)bundle.DeepCopy(), ContentIdentity.Mint(), version: 1);

        // Awaited rather than waited on. Validation is serialised across the process, so a
        // synchronous wait here holds a request thread for every write queued behind the one
        // being validated - which is where the measured cost of this gate almost entirely was.
        var report = await _validator.ValidateAsync(candidate, cancellationToken);
        if (!report.IsValid)
        {
            throw new ContentRejectedException(report.Issues);
        }
    }
}
