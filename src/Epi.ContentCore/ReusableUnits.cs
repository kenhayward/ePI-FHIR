using Hl7.Fhir.Model;

namespace Epi.ContentCore;

/// <summary>How a label follows a unit it references (ADR-007, ADR-026 decision 5).</summary>
public enum UnitResolution
{
    /// <summary>The version named here, and no other. The default, deliberately.</summary>
    Pinned,

    /// <summary>
    /// Newer unit versions should be propagated - as a new label version, never in place.
    /// </summary>
    TrackLatest,
}

/// <summary>
/// A section's use of a reusable content unit: which unit, which version, and how it follows.
/// </summary>
public sealed record UnitReference(
    DocumentIdentity Unit, int Version, UnitResolution Resolution = UnitResolution.Pinned);

/// <summary>
/// Marking content as a reusable unit, and recording where a section borrowed from
/// (CAP-SCM-004, ADR-026).
/// </summary>
/// <remarks>
/// A unit is content in the same shape and the same store as a label, so there is nothing here
/// about storage: only how a unit says what it is, and how a section says what it borrowed. The
/// reference is what change impact reads - it is the record that the relationship exists at all
/// (ADR-026 decision 3).
/// </remarks>
public static class ReusableUnits
{
    private const string UnitTagCode = "reusable-unit";

    private const string VersionExtension = "version";

    private const string ResolutionExtension = "resolution";

    /// <summary>Marks content as a reusable unit rather than a label.</summary>
    /// <remarks>
    /// A tag rather than a separate store: units go through every gate a label does, and the
    /// only thing that differs is what they are for.
    /// </remarks>
    public static Bundle MarkAsUnit(Bundle bundle, IdentifierAuthority? authority = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var system = Systems(authority).Unit;

        bundle.Meta ??= new Meta();
        if (!IsUnit(bundle, authority))
        {
            bundle.Meta.Tag = [.. bundle.Meta.Tag, new Coding(system, UnitTagCode)];
        }

        return bundle;
    }

    /// <summary>Whether this content is a reusable unit.</summary>
    public static bool IsUnit(Bundle bundle, IdentifierAuthority? authority = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        var system = Systems(authority).Unit;

        return bundle.Meta?.Tag.Any(
            t => t.System == system && t.Code == UnitTagCode) == true;
    }

    /// <summary>Records that this section borrows from a unit.</summary>
    /// <remarks>
    /// Held on <c>section.entry</c> as a reference carrying the unit's business identifier -
    /// never a server-assigned logical id, which would not survive a change of FHIR server
    /// (ADR-003, ADR-026 decision 2). The version and the resolution mode ride alongside as a
    /// complex extension in the deployment's own namespace (ADR-017).
    /// </remarks>
    public static Composition.SectionComponent Borrow(
        Composition.SectionComponent section, UnitReference reference,
        IdentifierAuthority? authority = null)
    {
        ArgumentNullException.ThrowIfNull(section);
        ArgumentNullException.ThrowIfNull(reference);

        if (reference.Version < 1)
        {
            // A reference with no version is a reference to whatever the unit says today, which
            // is the opposite of pinned (ADR-026 decision 2).
            throw new ArgumentException(
                "A unit reference must name the version it is pinned to.", nameof(reference));
        }

        var extension = Systems(authority).Reference;
        var entry = new ResourceReference
        {
            Identifier = new Identifier(reference.Unit.System, reference.Unit.Value),
        };

        entry.Extension.Add(new Extension
        {
            Url = extension,
            Extension =
            [
                new Extension(VersionExtension, new Integer(reference.Version)),
                new Extension(ResolutionExtension, new Code(Spelling(reference.Resolution))),
            ],
        });

        section.Entry = [.. section.Entry.Where(e => Of(e, extension) is null), entry];
        return section;
    }

    /// <summary>What this section borrows, or null if it borrows nothing.</summary>
    public static UnitReference? BorrowedBy(
        Composition.SectionComponent section, IdentifierAuthority? authority = null)
    {
        ArgumentNullException.ThrowIfNull(section);
        var extension = Systems(authority).Reference;

        return section.Entry.Select(entry => Of(entry, extension)).FirstOrDefault(r => r is not null);
    }

    /// <summary>Every unit this document borrows from, in document order.</summary>
    /// <remarks>
    /// The answer to "what does this label depend on". Its mirror - "which labels use this
    /// unit" - needs an index and is recorded as a debt (ADR-026 consequences).
    /// </remarks>
    public static IReadOnlyList<UnitReference> BorrowedIn(
        Bundle bundle, IdentifierAuthority? authority = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var composition = bundle.Entry.Count > 0 ? bundle.Entry[0].Resource as Composition : null;
        return
        [
            .. Flatten(composition?.Section ?? [])
                .Select(section => BorrowedBy(section, authority))
                .Where(reference => reference is not null)
                .Select(reference => reference!),
        ];
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

    private static UnitReference? Of(ResourceReference entry, string extensionUrl)
    {
        var extension = entry.Extension.FirstOrDefault(e => e.Url == extensionUrl);
        if (extension is null || entry.Identifier?.System is null || entry.Identifier.Value is null)
        {
            return null;
        }

        var version = (extension.Extension
            .FirstOrDefault(e => e.Url == VersionExtension)?.Value as Integer)?.Value;

        var resolution = (extension.Extension
            .FirstOrDefault(e => e.Url == ResolutionExtension)?.Value as Code)?.Value;

        // A reference the platform cannot read as a pin is not treated as one. Reading a
        // versionless reference as "the latest" would silently turn every malformed reference
        // into a track-latest one, which is the wrong default in the wrong direction.
        return version is null or < 1
            ? null
            : new UnitReference(
                new DocumentIdentity(entry.Identifier.System, entry.Identifier.Value),
                version.Value,
                resolution == "track-latest" ? UnitResolution.TrackLatest : UnitResolution.Pinned);
    }

    private static string Spelling(UnitResolution resolution) =>
        resolution == UnitResolution.TrackLatest ? "track-latest" : "pinned";

    private static (string Unit, string Reference) Systems(IdentifierAuthority? authority)
    {
        var systems = authority ?? IdentifierAuthority.Demonstration;

        if (string.IsNullOrWhiteSpace(systems.UnitSystem)
            || string.IsNullOrWhiteSpace(systems.UnitReferenceExtension))
        {
            // ADR-017 refuses partial configuration: a deployment that has not named these
            // namespaces would otherwise write references into an empty one.
            throw new InvalidOperationException(
                "The identifier authority names no reusable-unit namespaces, so a unit cannot "
                + "be marked and a reference cannot be recorded (ADR-017, ADR-026).");
        }

        return (systems.UnitSystem, systems.UnitReferenceExtension);
    }
}
