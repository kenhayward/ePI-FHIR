using Hl7.Fhir.Model;

namespace Epi.ContentCore;

/// <summary>
/// Stable identity for the sections of a document (ADR-015 decision 6).
/// </summary>
/// <remarks>
/// <para>
/// Assigned on creation and preserved thereafter: through editing, through new versions, and
/// through translation, so a translated section carries the same identity as its source and a
/// change to the source propagates to the right target section.
/// </para>
/// <para>
/// Opaque, like document identity, and for the same reason: a section identifier derived from
/// a title or a position would move the moment a section was retitled or reordered, which is
/// exactly when impact analysis most needs it to hold still.
/// </para>
/// </remarks>
public static class SectionIdentity
{
    /// <summary>
    /// Gives an identifier to every section that lacks one, leaving existing ones untouched.
    /// </summary>
    /// <remarks>
    /// Idempotent, because it runs on every write. A section identifier that changed on the
    /// second save would be worse than no identifier at all: cross-references would resolve
    /// until someone saved again.
    /// </remarks>
    public static Bundle AssignMissing(Bundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        foreach (var composition in bundle.Entry.Select(e => e.Resource).OfType<Composition>())
        {
            Assign(composition.Section);
        }

        return bundle;
    }

    private static void Assign(IEnumerable<Composition.SectionComponent> sections)
    {
        foreach (var section in sections)
        {
            if (string.IsNullOrWhiteSpace(section.ElementId))
            {
                section.ElementId = Guid.CreateVersion7().ToString();
            }

            // A section within a section is still a section: impact analysis addresses it, so
            // it needs identity as much as a top-level one.
            Assign(section.Section);
        }
    }
}
