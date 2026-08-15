using Hl7.Fhir.Model;

namespace Epi.ContentCore;

/// <summary>
/// What a variant is a variant of, and of which version (ADR-032 decision 2).
/// </summary>
/// <param name="Language">
/// The language it is written in. What makes a variant a translation; a market variant in the
/// same language for a different regulator is the same shape and not a special case.
/// </param>
public sealed record VariantOf(
    DocumentIdentity Source,
    int SourceVersion,
    string Language,
    string? Country = null,
    string? Regulator = null);

/// <summary>
/// Marking content as a variant of a source version, and reading that back (CAP-LOC-001).
/// </summary>
/// <remarks>
/// A variant is content with its own identity, version lineage and lifecycle - not a version of
/// its source and not a dimension alongside version. What it records is where it came from,
/// which is the same shape as a template recording what instantiated a label (ADR-021) and a
/// section recording the unit it borrowed (ADR-026).
/// </remarks>
public static class Variants
{
    /// <summary>Records that this content is a variant of a specific source version.</summary>
    /// <exception cref="ArgumentException">
    /// If no source version is named. A link to "the English label" without a version points at
    /// whatever that label says today, and a translation is a translation of something specific.
    /// </exception>
    public static Bundle MarkAsVariant(
        Bundle bundle, VariantOf variant, IdentifierAuthority? authority = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(variant);
        ArgumentException.ThrowIfNullOrWhiteSpace(variant.Language);

        if (variant.SourceVersion < 1)
        {
            throw new ArgumentException(
                "A variant must name the source version it was translated from.", nameof(variant));
        }

        var systems = Systems(authority);

        bundle.Meta ??= new Meta();
        bundle.Meta.Tag =
        [
            .. bundle.Meta.Tag.Where(t => t.System != systems.Source && t.System != systems.Language),
            new Coding(systems.Source, $"{variant.Source.Value}@{variant.SourceVersion}"),
            new Coding(systems.Language, Designation(variant)),
        ];

        // The language of the content itself, not only a tag: a consumer reading the document
        // without knowing this platform's namespaces still has to be able to tell.
        if (bundle.Entry.Count > 0 && bundle.Entry[0].Resource is Composition composition)
        {
            composition.Language = variant.Language;
        }

        return bundle;
    }

    /// <summary>What this content is a variant of, or null where it is a source in its own right.</summary>
    public static VariantOf? Of(Bundle bundle, IdentifierAuthority? authority = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var systems = Systems(authority);
        var source = Tag(bundle, systems.Source);
        var designation = Tag(bundle, systems.Language);

        if (source is null || designation is null)
        {
            return null;
        }

        var parts = source.Split('@');
        if (parts.Length != 2 || !int.TryParse(parts[1], out var version) || version < 1)
        {
            // A link the platform cannot read as naming a version is not read as naming the
            // latest: that would silently turn every malformed link into a moving target.
            return null;
        }

        var scope = designation.Split('|');
        return new VariantOf(
            new DocumentIdentity(systems.DocumentSystem, parts[0]),
            version,
            scope[0],
            scope.Length > 1 && scope[1].Length > 0 ? scope[1] : null,
            scope.Length > 2 && scope[2].Length > 0 ? scope[2] : null);
    }

    /// <summary>
    /// Whether a variant is out of date, given the newest version its source now has.
    /// </summary>
    /// <remarks>
    /// A comparison made when asked, never a flag written onto the variant (ADR-032 decision 5).
    /// Writing one would modify approved content to record a fact about a different document,
    /// and it would be wrong from the moment the next source version landed until something
    /// noticed.
    /// </remarks>
    public static bool IsStale(VariantOf variant, int latestSourceVersion)
    {
        ArgumentNullException.ThrowIfNull(variant);
        return latestSourceVersion > variant.SourceVersion;
    }

    private static string Designation(VariantOf variant) =>
        $"{variant.Language}|{variant.Country}|{variant.Regulator}";

    private static string? Tag(Bundle bundle, string system) => bundle.Meta?.Tag
        .Where(t => t.System == system)
        .Select(t => t.Code)
        .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));

    private static (string Source, string Language, string DocumentSystem) Systems(
        IdentifierAuthority? authority)
    {
        var systems = authority ?? IdentifierAuthority.Demonstration;

        if (string.IsNullOrWhiteSpace(systems.VariantSourceTagSystem)
            || string.IsNullOrWhiteSpace(systems.VariantScopeTagSystem))
        {
            throw new InvalidOperationException(
                "The identifier authority names no variant namespaces, so a translation cannot "
                + "record what it was translated from (ADR-017, ADR-032).");
        }

        return (systems.VariantSourceTagSystem, systems.VariantScopeTagSystem, systems.DocumentSystem);
    }
}
