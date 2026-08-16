using Hl7.Fhir.Model;

namespace Epi.ContentCore;

/// <summary>
/// One section as an author sees it (ADR-038).
/// </summary>
/// <param name="Identity">
/// The identity the platform assigned (ADR-015). The join for a save, and never invented here.
/// </param>
/// <param name="Narrative">
/// The section's XHTML narrative, or empty where it has none. Empty rather than absent: a
/// section an author has not written yet is normal, and omitting it would be a section they
/// assume does not exist.
/// </param>
public sealed record ProjectedSection(string Identity, string? Title, string Narrative);

/// <summary>
/// A section-shaped view of a version, and the patch back (ADR-038, FN-CC-010).
/// </summary>
/// <remarks>
/// <para>
/// Derived on every read and stored nowhere. FHIR remains the single source of truth, so there
/// is no table of sections to keep in step and nothing that can disagree with the Bundle,
/// because nothing here outlives the request.
/// </para>
/// <para>
/// The write is the half that has to be right. A projection carries what an author may change -
/// a title and a narrative - and a Bundle carries a great deal more, so <see cref="Apply"/>
/// patches the version it was read from rather than rebuilding one. Everything the author did
/// not touch is what it was.
/// </para>
/// </remarks>
public static class SectionProjection
{
    /// <summary>Every section of a version, nested ones included, in document order.</summary>
    public static IReadOnlyList<ProjectedSection> Of(Bundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var composition = bundle.Entry.Count > 0 ? bundle.Entry[0].Resource as Composition : null;

        return
        [
            .. Flatten(composition?.Section ?? []).Select(section => new ProjectedSection(
                section.ElementId ?? string.Empty,
                section.Title,

                // Empty, not null. A section with nothing written in it is a section, and the
                // surface has to be able to tell that from one it was not shown.
                section.Text?.Div ?? string.Empty)),
        ];
    }

    /// <summary>
    /// Applies edited sections to the version they were read from, returning a new Bundle.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// If a section names an identity the version does not have. Adding a section is a different
    /// operation from editing one, with different rules, and a save that did it by accident is
    /// how a label acquires a section nobody approved (ADR-038 decision 4).
    /// </exception>
    public static Bundle Apply(Bundle bundle, IReadOnlyList<ProjectedSection> edited)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(edited);

        // Copied, because a patch that mutated its input would leave the version in memory
        // disagreeing with the one in the store.
        var patched = (Bundle)bundle.DeepCopy();
        var composition = patched.Entry.Count > 0 ? patched.Entry[0].Resource as Composition : null;
        var sections = Flatten(composition?.Section ?? [])
            .Where(section => !string.IsNullOrWhiteSpace(section.ElementId))
            .ToDictionary(section => section.ElementId!, StringComparer.Ordinal);

        foreach (var change in edited)
        {
            if (!sections.TryGetValue(change.Identity, out var section))
            {
                throw new ArgumentException(
                    $"This version has no section '{change.Identity}'. A save may change what a "
                    + "section says, and adding one is a separate operation with its own rules.",
                    nameof(edited));
            }

            section.Title = change.Title;
            section.Text = new Narrative
            {
                // Generated, because it is: the author wrote the words and the platform wrote
                // the markup around them (ADR-037 decision 4).
                Status = Narrative.NarrativeStatus.Generated,
                Div = change.Narrative,
            };
        }

        return patched;
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
