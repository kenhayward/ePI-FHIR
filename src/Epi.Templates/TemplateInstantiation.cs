using Epi.ContentCore;
using Hl7.Fhir.Model;

namespace Epi.Templates;

/// <summary>
/// Produces a conformant draft from a template (CAP-TPL-004, CAP-TPL-007, ADR-021).
/// </summary>
/// <remarks>
/// The result is an ordinary document Bundle and goes through the ordinary write gate. A
/// template that cannot produce a conformant draft is a broken template, and the validator
/// already in the write path is what says so - there is no second validation path here
/// (ADR-021 decision 3).
/// </remarks>
public static class TemplateInstantiation
{
    /// <summary>Scaffolds a document Bundle from a template.</summary>
    public static Bundle Instantiate(
        LabelTemplate template, string title, IdentifierAuthority? authority = null)
    {
        ArgumentNullException.ThrowIfNull(template);

        // The template names the kind of label; only the author can name the product.
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        if (template.Sections.Count == 0)
        {
            throw new InvalidTemplateException(
                [$"Template '{template.Identifier}' defines no sections, so it can scaffold nothing."]);
        }

        var systems = authority ?? IdentifierAuthority.Demonstration;

        var composition = new Composition
        {
            Status = CompositionStatus.Preliminary,
            Title = title,
            Date = null,
            Section = [.. template.Sections.Select(Scaffold)],
        };

        var bundle = new Bundle
        {
            Type = Bundle.BundleType.Document,
            Entry =
            [
                new Bundle.EntryComponent
                {
                    FullUrl = $"urn:uuid:{Guid.CreateVersion7()}",
                    Resource = composition,
                },
            ],
        };

        // Recorded on the content itself, so provenance survives independently of any registry
        // the platform keeps and answers "which template made this" from the document alone
        // (CAP-TPL-007).
        bundle.Meta ??= new Meta();
        bundle.Meta.Tag =
        [
            .. bundle.Meta.Tag.Where(tag =>
                tag.System != systems.TemplateSystem && tag.System != systems.TemplateVersionTagSystem),
            new Coding(systems.TemplateSystem, template.Identifier),
            new Coding(systems.TemplateVersionTagSystem, template.Version.ToString()),
        ];

        return bundle;
    }

    /// <summary>The template this content was instantiated from, or null if it was not.</summary>
    public static string? TemplateOf(Bundle bundle, IdentifierAuthority? authority = null) =>
        Tag(bundle, (authority ?? IdentifierAuthority.Demonstration).TemplateSystem);

    /// <summary>The template version this content was instantiated from, or null.</summary>
    public static int? TemplateVersionOf(Bundle bundle, IdentifierAuthority? authority = null) =>
        int.TryParse(
            Tag(bundle, (authority ?? IdentifierAuthority.Demonstration).TemplateVersionTagSystem),
            out var version)
            ? version
            : null;

    private static string? Tag(Bundle bundle, string system)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        return bundle.Meta?.Tag.FirstOrDefault(tag => tag.System == system)?.Code;
    }

    /// <summary>
    /// One section, and everything under it. Section identity is deliberately not assigned
    /// here: it is minted per document when content is stored (ADR-015 decision 6), because
    /// two labels from one template are two documents whose sections are not the same sections.
    /// </summary>
    private static Composition.SectionComponent Scaffold(TemplateSection section) =>
        new()
        {
            Title = section.Title,
            Code = new CodeableConcept(section.CodeSystem, section.Code),

            // An empty section is honest. Inventing placeholder narrative would put words into
            // a regulated document that nobody wrote and a reviewer might not notice.
            Text = string.IsNullOrWhiteSpace(section.Boilerplate)
                ? null
                : new Narrative
                {
                    Status = Narrative.NarrativeStatus.Additional,
                    Div = section.Boilerplate,
                },
            Section = [.. section.Sections.Select(Scaffold)],
        };
}
