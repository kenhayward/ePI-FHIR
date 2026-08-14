using Epi.ContentCore;
using Hl7.Fhir.Model;

namespace Epi.Templates;

/// <summary>
/// Produces a conformant draft from a template (CAP-TPL-004, CAP-TPL-007, ADR-021).
/// </summary>
public static class TemplateInstantiation
{
    /// <summary>Scaffolds a document Bundle from a template.</summary>
    public static Bundle Instantiate(
        LabelTemplate template, string title, IdentifierAuthority? authority = null) =>
        throw new NotImplementedException();

    /// <summary>The template this content was instantiated from, or null if it was not.</summary>
    public static string? TemplateOf(Bundle bundle, IdentifierAuthority? authority = null) =>
        throw new NotImplementedException();

    /// <summary>The template version this content was instantiated from, or null.</summary>
    public static int? TemplateVersionOf(Bundle bundle, IdentifierAuthority? authority = null) =>
        throw new NotImplementedException();
}
