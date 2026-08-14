using Hl7.Fhir.Model;

namespace Epi.ContentCore;

/// <summary>
/// Places the text of each referenced unit into the section that borrows it, once, on the way
/// in (CAP-SCM-004, ADR-026 decisions 4 and 6).
/// </summary>
/// <remarks>
/// A decorator, positioned outside validation so that what is validated is what is stored, and
/// resolving units through a store scoped to the caller so that borrowing cannot be used to
/// read a unit the caller may not see.
/// <para>
/// Materialising at the write gate rather than resolving at read time is what makes a stored
/// label a self-contained conformant document, and what makes two renders of the same version
/// identical: the bytes were produced once. A pinned reference and its materialised text cannot
/// disagree, because unit versions are immutable.
/// </para>
/// </remarks>
public sealed class MaterialisingContentStore(
    IContentStore inner, IContentStore units, IdentifierAuthority? authority = null) : IContentStore
{
    private readonly IContentStore _inner = inner ?? throw new ArgumentNullException(nameof(inner));
    private readonly IContentStore _units = units ?? throw new ArgumentNullException(nameof(units));

    public async Task<EpiDocument> CreateAsync(
        DocumentIdentity identity, Bundle bundle, CancellationToken cancellationToken = default) =>
        await _inner.CreateAsync(
            identity, await MaterialiseAsync(bundle, cancellationToken), cancellationToken);

    public async Task<EpiDocument> CreateVersionAsync(
        DocumentIdentity identity, int version, Bundle bundle,
        CancellationToken cancellationToken = default) =>
        await _inner.CreateVersionAsync(
            identity, version, await MaterialiseAsync(bundle, cancellationToken), cancellationToken);

    public Task<EpiDocument?> GetAsync(
        DocumentIdentity identity, int version, CancellationToken cancellationToken = default) =>
        _inner.GetAsync(identity, version, cancellationToken);

    public Task<EpiDocument?> GetLatestAsync(
        DocumentIdentity identity, CancellationToken cancellationToken = default) =>
        _inner.GetLatestAsync(identity, cancellationToken);

    public Task<IReadOnlyList<int>> VersionsAsync(
        DocumentIdentity identity, CancellationToken cancellationToken = default) =>
        _inner.VersionsAsync(identity, cancellationToken);

    private async Task<Bundle> MaterialiseAsync(Bundle bundle, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var composition = bundle.Entry.Count > 0 ? bundle.Entry[0].Resource as Composition : null;
        if (composition is null)
        {
            return bundle;
        }

        foreach (var section in Flatten(composition.Section))
        {
            var reference = ReusableUnits.BorrowedBy(section, authority);
            if (reference is null)
            {
                continue;
            }

            var (narrative, version) = await BorrowAsync(reference, cancellationToken);

            section.Text = narrative;

            // Track-latest is resolved here and then pinned to what it resolved to, so the
            // stored version records the unit version it actually used rather than an intent
            // that could resolve differently later (ADR-026 decision 5).
            ReusableUnits.Borrow(
                section, reference with { Version = version }, authority);
        }

        return bundle;
    }

    private async Task<(Narrative Narrative, int Version)> BorrowAsync(
        UnitReference reference, CancellationToken cancellationToken)
    {
        var document = reference.Resolution == UnitResolution.TrackLatest
            ? await _units.GetLatestAsync(reference.Unit, cancellationToken)
            : await _units.GetAsync(reference.Unit, reference.Version, cancellationToken);

        if (document is null)
        {
            // Missing and out of scope are the same answer, deliberately: borrowing must not be
            // a way of learning that a unit exists.
            throw new UnitNotAvailableException(
                reference.Unit, reference.Version,
                "it does not exist, or is not one this caller may see");
        }

        if (!ReusableUnits.IsUnit(document.Bundle, authority))
        {
            // Borrowing from a label is not reuse. It would take that label's text without any
            // of the relationship reuse exists to record.
            throw new UnitNotAvailableException(
                reference.Unit, reference.Version, "it is not a reusable content unit");
        }

        var sections = (document.Bundle.Entry[0].Resource as Composition)?.Section ?? [];
        if (sections.Count != 1 || sections[0].Text is not { } narrative)
        {
            // One section, so there is no question which passage was borrowed. A unit with
            // several would make the answer depend on a rule nobody wrote down.
            throw new UnitNotAvailableException(
                reference.Unit, document.Version,
                "a reusable unit must hold exactly one section, and that section must have narrative");
        }

        return ((Narrative)narrative.DeepCopy(), document.Version);
    }

    private static IEnumerable<Composition.SectionComponent> Flatten(
        IEnumerable<Composition.SectionComponent> sections)
    {
        foreach (var section in sections)
        {
            yield return section;
            foreach (var nested in Flatten(section.Section))
            {
                yield return nested;
            }
        }
    }
}

/// <summary>Raised when a referenced unit cannot be borrowed from.</summary>
public sealed class UnitNotAvailableException(DocumentIdentity unit, int version, string reason)
    : Exception($"Version {version} of unit {unit} cannot be borrowed from: {reason}.")
{
    public DocumentIdentity Unit { get; } = unit;
}
