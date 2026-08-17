using System.Text;

namespace Epi.Templates;

/// <summary>
/// The bytes a signature over a render template is made across (ADR-047, FN-TPL-006).
/// </summary>
/// <remarks>
/// <para>
/// A signature is a hash of what was signed, and the only thing that could be hashed was a FHIR
/// Bundle - so a template could be submitted for review and never approved, because no signature
/// could be minted for one. The gate in <c>config/lifecycle/template-states.json</c> was
/// configured and unreachable.
/// </para>
/// <para>
/// What an approver signs for is what the template will do to a leaflet: its stylesheet, because
/// that is what changes the document, and its name, because that is what they read when deciding
/// whether to sign. Its identity and version are in there too, so a signature cannot be carried
/// from one template to another that happens to look the same.
/// </para>
/// <para>
/// Readable rather than packed, so what was signed can be shown to somebody and not only
/// compared. A signature nobody can inspect is a signature nobody can challenge.
/// </para>
/// </remarks>
public static class TemplateCanonicalForm
{
    /// <summary>
    /// The canonical bytes of a stored template version.
    /// </summary>
    /// <remarks>
    /// Length-prefixed rather than delimited. A separator can appear inside a stylesheet, and a
    /// form that could be forged by moving a character from one field to the next is a hash with
    /// a collision anybody can construct.
    /// </remarks>
    public static byte[] Of(StoredRenderTemplate template)
    {
        ArgumentNullException.ThrowIfNull(template);

        var canonical = new StringBuilder();
        Append(canonical, "render-template", template.Identifier);
        Append(canonical, "version", template.Version.ToString());
        Append(canonical, "name", template.Name);
        Append(canonical, "stylesheet", template.Stylesheet);

        return Encoding.UTF8.GetBytes(canonical.ToString());
    }

    private static void Append(StringBuilder canonical, string field, string value) =>
        canonical.Append(field).Append(':').Append(value.Length).Append(':').Append(value)
            .Append('\n');
}
