using Epi.ContentCore;
using Hl7.Fhir.Model;
using Xunit;

namespace Epi.Validation.Tests;

// Unit tests for structural validation at the write gate.
//   FN-VAL-001 Check structural well-formedness against the pinned profile
//   FN-VAL-002 Check reference integrity, rejecting dangling references
//   FN-VAL-003 Produce structured issues carrying severity and element location
public sealed class StructuralValidatorTests : IClassFixture<StructuralValidatorFixture>
{
    private readonly StructuralValidator _validator;

    public StructuralValidatorTests(StructuralValidatorFixture fixture) => _validator = fixture.Validator;

    /// <summary>
    /// The fixture as the validator sees it in production: stamped with a canonical identity.
    /// FHIR constraint bdl-9 requires a document Bundle to carry an identifier, and the
    /// platform mints that rather than the submitter supplying it (ADR-015), so the artefact
    /// under validation is always the stamped one.
    /// </summary>
    private static Bundle MinimalDocument() => ContentIdentity.Stamp(
        EpiBundleReader.Read(File.ReadAllText(TestFixtures.Path("epi", "minimal-epi-document.json"))),
        ContentIdentity.Mint(),
        version: 1);

    private static Composition CompositionOf(Bundle bundle) =>
        Assert.IsType<Composition>(bundle.Entry[0].Resource);

    [Fact]
    public void FN_VAL_001_a_conformant_document_produces_no_errors()
    {
        var report = _validator.Validate(MinimalDocument());

        Assert.True(report.IsValid,
            "Expected the fixture to be structurally valid, but got: "
            + string.Join(" | ", report.Issues.Select(i => $"{i.Severity} {i.Location}: {i.Message}")));
    }

    [Fact]
    public void FN_VAL_001_a_missing_required_element_is_an_error()
    {
        // Composition.status is 1..1 in the base specification.
        var bundle = MinimalDocument();
        CompositionOf(bundle).Status = null;

        var report = _validator.Validate(bundle);

        Assert.False(report.IsValid);
        Assert.Contains(report.Issues, i => i.Severity == ValidationSeverity.Error);
    }

    [Fact]
    public void FN_VAL_003_an_issue_carries_a_severity_and_the_element_it_is_about()
    {
        var bundle = MinimalDocument();
        CompositionOf(bundle).Status = null;

        var report = _validator.Validate(bundle);

        var error = report.Issues.First(i => i.Severity == ValidationSeverity.Error);
        Assert.False(string.IsNullOrWhiteSpace(error.Message));

        // A path into the document, such as "Bundle.entry[0].resource[0]", which is what
        // CAP-VAL-005 means by a precise location: it addresses the element, not the type.
        Assert.StartsWith("Bundle", error.Location, StringComparison.Ordinal);
        Assert.Contains("entry", error.Location, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void FN_VAL_002_a_reference_to_something_absent_from_the_document_is_an_error()
    {
        // CAP-VAL-003: no dangling references in a document. A document Bundle is
        // self-contained, so an internal reference that resolves to nothing is a defect the
        // base specification does not catch on its own.
        var bundle = MinimalDocument();
        CompositionOf(bundle).Subject = [new ResourceReference("urn:uuid:00000000-0000-4000-8000-00000000dead")];

        var report = _validator.Validate(bundle);

        Assert.False(report.IsValid);
        Assert.Contains(report.Issues, i =>
            i.Severity == ValidationSeverity.Error && i.Message.Contains("resolve", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FN_VAL_002_a_reference_satisfied_within_the_document_is_accepted()
    {
        var bundle = MinimalDocument();
        var organisation = new Organization { Id = "org-1", Name = "Synthetic Pharma Ltd" };
        bundle.Entry.Add(new Bundle.EntryComponent
        {
            FullUrl = "urn:uuid:0195f3a0-0000-7000-8000-0000000000aa",
            Resource = organisation,
        });
        CompositionOf(bundle).Custodian =
            new ResourceReference("urn:uuid:0195f3a0-0000-7000-8000-0000000000aa");

        var report = _validator.Validate(bundle);

        Assert.DoesNotContain(report.Issues, i =>
            i.Severity == ValidationSeverity.Error && i.Message.Contains("resolve", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FN_VAL_002_an_external_reference_is_not_treated_as_dangling()
    {
        // A reference to a resource on another server is not a defect: it is out of scope for
        // a document-level integrity check, and flagging it would make the gate unusable.
        var bundle = MinimalDocument();
        CompositionOf(bundle).Custodian = new ResourceReference("https://example.org/fhir/Organization/1");

        var report = _validator.Validate(bundle);

        Assert.DoesNotContain(report.Issues, i =>
            i.Severity == ValidationSeverity.Error && i.Message.Contains("resolve", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void FN_VAL_003_a_valid_document_reports_no_issues_at_all_rather_than_silence()
    {
        var report = _validator.Validate(MinimalDocument());

        Assert.True(report.IsValid);
        Assert.DoesNotContain(report.Issues, i => i.Severity == ValidationSeverity.Error);
    }
}
