using Xunit;

namespace Epi.Lifecycle.Tests;

// What a version was approved against, terminology included (FN-TRM-001).
//   CAP-LCM-011 Pin the content snapshot and its validating context at approval
//   CAP-TRM-007 Track terminology source versions
//
// ADR-023 exists so a version can be reconstructed with what it was approved against, and its
// pinned context recorded conformance packages and nothing about terminology. A code that was
// valid at approval because the code system said so - in the version of that system in force
// that month - was a code the platform could say nothing about afterwards. That is a gap in
// exactly the mechanism ADR-023 was written to close.
public sealed class TerminologyBindingTests
{
    [Fact]
    public void FN_TRM_001_a_binding_names_the_system_and_the_version_that_answered()
    {
        // "SNOMED CT said so" is not a fact an inspection can check. "The 2026-03-01
        // international release said so" is (ADR-036 decision 2).
        var binding = new TerminologyBinding(
            "http://snomed.info/sct", "http://snomed.info/sct/900000000000207008/version/20260301");

        Assert.Equal("http://snomed.info/sct", binding.System);
        Assert.True(binding.IsVersioned);
    }

    [Fact]
    public void FN_TRM_001_a_source_that_cannot_say_which_version_answered_records_that()
    {
        // Rather than the version it would use today, which is a true answer to a different
        // question - the same trap ADR-023 was written about.
        var binding = new TerminologyBinding("http://snomed.info/sct", Version: null);

        Assert.False(binding.IsVersioned);
        Assert.Contains("unversioned", binding.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void FN_TRM_001_a_binding_with_no_system_is_refused()
    {
        // A version without the system it versions says nothing at all.
        Assert.Throws<ArgumentException>(() => new TerminologyBinding(" ", "20260301"));
    }

    [Fact]
    public void FN_TRM_001_an_approval_carries_the_bindings_in_force_at_the_time()
    {
        var context = new ApprovalContext(
            "sha256:abc", [], "https://epi.example.org",
            TerminologyBindings:
            [
                new TerminologyBinding("http://snomed.info/sct", "20260301"),
                new TerminologyBinding("http://terminology.example.org/meddra", "26.1"),
            ]);

        Assert.Equal(2, context.TerminologyBindings.Count);
    }

    [Fact]
    public void FN_TRM_001_an_approval_asked_for_no_terminology_records_none()
    {
        // Distinguishable from a pin taken before bindings existed, which is why this is an
        // empty list rather than a null (ADR-036 decision 3).
        var context = new ApprovalContext("sha256:abc", [], "https://epi.example.org");

        Assert.Empty(context.TerminologyBindings);
    }

    [Fact]
    public void FN_TRM_001_two_bindings_for_one_system_are_refused()
    {
        // Which version answered would depend on which was read first, and the whole point of
        // recording the version is that it is not a matter of chance.
        Assert.Throws<ArgumentException>(() => new ApprovalContext(
            "sha256:abc", [], "https://epi.example.org",
            TerminologyBindings:
            [
                new TerminologyBinding("http://snomed.info/sct", "20260301"),
                new TerminologyBinding("http://snomed.info/sct", "20250901"),
            ]));
    }
}
