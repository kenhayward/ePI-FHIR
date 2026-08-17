using System.Text;
using Xunit;

namespace Epi.Templates.Tests;

// What a signature over a template is made over (FN-TPL-006).
//   CAP-TPL-008 Template lifecycle with approval via workflow
//   CAP-AUD-003 Electronic signature at approval gates
//
// A signature is a hash of what was signed, and until now the only thing that could be hashed
// was a FHIR Bundle - so a template could be submitted for review and never approved, because
// no signature could be minted for one. The gate was configured and unreachable (ADR-047).
//
// What an approver is signing for is what the template will do to a leaflet: its name, because
// that is what they read when deciding, and its stylesheet, because that is what changes the
// document.
public sealed class TemplateCanonicalFormTests
{
    private static StoredRenderTemplate Template(
        string identifier = "qrd-package-leaflet",
        int version = 1,
        string name = "EU QRD package leaflet",
        string stylesheet = "body { font-family: sans-serif; }") =>
        new(identifier, version, name, stylesheet);

    [Fact]
    public void FN_TPL_006_the_same_template_canonicalises_to_the_same_bytes()
    {
        // A signature made this morning has to verify this afternoon.
        Assert.Equal(
            TemplateCanonicalForm.Of(Template()),
            TemplateCanonicalForm.Of(Template()));
    }

    [Fact]
    public void FN_TPL_006_a_different_stylesheet_is_different_bytes()
    {
        // The whole point. Signing for a template whose stylesheet could change underneath the
        // signature would be signing for nothing.
        Assert.NotEqual(
            TemplateCanonicalForm.Of(Template()),
            TemplateCanonicalForm.Of(Template(stylesheet: "body { color: navy; }")));
    }

    [Fact]
    public void FN_TPL_006_a_different_name_is_different_bytes()
    {
        // The name is what an approver reads when deciding whether to sign, so it is part of
        // what they signed for.
        Assert.NotEqual(
            TemplateCanonicalForm.Of(Template()),
            TemplateCanonicalForm.Of(Template(name: "Something else entirely")));
    }

    [Fact]
    public void FN_TPL_006_a_different_version_of_one_template_is_different_bytes()
    {
        Assert.NotEqual(
            TemplateCanonicalForm.Of(Template()),
            TemplateCanonicalForm.Of(Template(version: 2)));
    }

    [Fact]
    public void FN_TPL_006_two_templates_that_differ_only_by_identifier_are_different_bytes()
    {
        Assert.NotEqual(
            TemplateCanonicalForm.Of(Template()),
            TemplateCanonicalForm.Of(Template(identifier: "qrd-labelling")));
    }

    [Fact]
    public void FN_TPL_006_the_fields_cannot_be_run_together_to_forge_a_match()
    {
        // Concatenation without separators lets one template's name absorb another's stylesheet
        // and produce the same bytes. A hash is only as good as the encoding underneath it.
        Assert.NotEqual(
            TemplateCanonicalForm.Of(Template(name: "ab", stylesheet: "cd")),
            TemplateCanonicalForm.Of(Template(name: "a", stylesheet: "bcd")));
    }

    [Fact]
    public void FN_TPL_006_the_canonical_form_is_readable()
    {
        // So that what was signed can be shown to somebody, rather than only compared. A
        // signature nobody can inspect is a signature nobody can challenge.
        var canonical = Encoding.UTF8.GetString(TemplateCanonicalForm.Of(Template()));

        Assert.Contains("qrd-package-leaflet", canonical, StringComparison.Ordinal);
        Assert.Contains("EU QRD package leaflet", canonical, StringComparison.Ordinal);
    }
}
