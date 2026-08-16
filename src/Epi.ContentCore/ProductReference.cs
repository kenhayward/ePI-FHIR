using Hl7.Fhir.Model;

namespace Epi.ContentCore;

/// <summary>
/// Which product a label is about (ADR-040, FN-CC-011).
/// </summary>
/// <param name="Identifier">
/// The product's identity in whatever is the system of record (CAP-MDM-002). This is what every
/// question the platform asks resolves against.
/// </param>
/// <param name="Display">
/// The name, for a reader. A copy of what the directory said when the reference was written, and
/// copies go stale - so nothing resolves it (ADR-040 decision 2).
/// </param>
public sealed record ProductReference(string Identifier, string? Display)
{
    public string Identifier { get; } = string.IsNullOrWhiteSpace(Identifier)
        ? throw new ArgumentException(
            "A product reference must carry the product's identifier. A display alone is the "
            + "free text this exists to replace: unresolvable, and no use for asking which "
            + "labels are about a product (ADR-040).",
            nameof(Identifier))
        : Identifier;

    /// <summary>
    /// Reads the product a label is about, or null where it names none resolvably.
    /// </summary>
    /// <remarks>
    /// Content written before ADR-040 carries a display and no identifier. It is readable and it
    /// is not resolvable, and answering with it would make an unresolvable label look resolved -
    /// so this answers null, and the facet in search is honestly incomplete for older content
    /// rather than quietly wrong about it.
    /// </remarks>
    public static ProductReference? Of(Bundle bundle, IdentifierAuthority? authority = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var systems = authority ?? IdentifierAuthority.Demonstration;
        var composition = bundle.Entry.Count > 0 ? bundle.Entry[0].Resource as Composition : null;

        var subject = composition?.Subject.FirstOrDefault(reference =>
            string.Equals(reference.Identifier?.System, systems.ProductSystem, StringComparison.Ordinal)
            && !string.IsNullOrWhiteSpace(reference.Identifier?.Value));

        return subject?.Identifier?.Value is null
            ? null
            : new ProductReference(subject.Identifier.Value, subject.Display);
    }

    /// <summary>
    /// Records which product a label is about, replacing any it already named.
    /// </summary>
    /// <remarks>
    /// One subject, because a label is about one product: two would make "which labels are about
    /// this product" answer twice for one label, and neither answer would be wrong.
    /// </remarks>
    public static Bundle Stamp(
        Bundle bundle, ProductReference product, IdentifierAuthority? authority = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(product);

        var systems = authority ?? IdentifierAuthority.Demonstration;
        if (string.IsNullOrWhiteSpace(systems.ProductSystem))
        {
            throw new ArgumentException(
                "This deployment has configured no product identifier system, so a product "
                + "reference would be written into a namespace nobody owns. Set 'productSystem' "
                + "in config/identifiers.json (ADR-017).",
                nameof(authority));
        }

        // Copied, so stamping does not change the document it was handed - the same rule the
        // section projection follows for the same reason.
        var stamped = (Bundle)bundle.DeepCopy();
        var composition = stamped.Entry.Count > 0 ? stamped.Entry[0].Resource as Composition : null;

        if (composition is null)
        {
            throw new ArgumentException(
                "This is not a document Bundle anchored by a Composition, so there is nothing "
                + "for a product reference to be about.",
                nameof(bundle));
        }

        composition.Subject =
        [
            new ResourceReference
            {
                Identifier = new Identifier(systems.ProductSystem, product.Identifier),
                Display = product.Display,
            },
        ];

        return stamped;
    }
}
