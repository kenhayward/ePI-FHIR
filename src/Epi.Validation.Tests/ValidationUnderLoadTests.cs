using Epi.ContentCore;
using Hl7.Fhir.Model;
using Xunit;

namespace Epi.Validation.Tests;

// What the write gate does when more than one write arrives at once (FN-VAL-004).
//   CAP-VAL-001 Validate structural conformance against the pinned profile
//
// The serialisation in StructuralValidator was a correctness-first stopgap, recorded as a debt
// against PR 6a and left unmeasured because nothing produced enough concurrent validation to
// measure. Translation changed that: a label in three languages is three validations, and every
// variant of every source change is another.
//
// Measured before changing anything (recorded in design/iteration-3.md), and the measurement
// reversed the expected answer twice over:
//
//   - The gate is load-bearing, not merely cautious. Fifteen rounds of sixteen concurrent
//     validations against a cold validator, with the gate reached past, reported errors in 238
//     of 240. The document is valid; the errors are canonicals failing to resolve under
//     concurrent first use. Removing the gate would reject valid labels almost every time a
//     cold service took two writes at once.
//   - The gate's cost is not the serialising. Thirty-two concurrent validations take 175 ms
//     with the gate and 39 ms without, on dedicated threads. Through the thread pool the same
//     work took 10.6 seconds - sixty times more - because a synchronous wait holds a pool
//     thread, and a starved pool injects replacements about twice a second.
//
// So the gate stays and stops blocking. These cases pin the property that matters, which is
// correctness under concurrency; the timings are recorded rather than asserted, because a
// timing assertion on a shared runner fails for reasons that have nothing to do with the code.
public sealed class ValidationUnderLoadTests
{
    private const int Concurrent = 16;

    private static Bundle Document() => ContentIdentity.Stamp(
        EpiBundleReader.Read(File.ReadAllText(TestFixtures.Path("epi", "minimal-epi-document.json"))),
        ContentIdentity.Mint(),
        version: 1);

    private static StructuralValidator Validator() => new(ProfileSource.FromPinnedPackages(null));

    [Fact]
    public async Task FN_VAL_004_concurrent_validation_of_a_valid_document_finds_nothing_wrong()
    {
        // A cold validator on purpose: this is where reaching past the gate fails, 238 times in
        // 240. If the gate ever stopped being held across the whole validation, this is the
        // case that would notice.
        var validator = Validator();

        var reports = await Task.WhenAll(
            Enumerable.Range(0, Concurrent).Select(_ => validator.ValidateAsync(Document())));

        Assert.All(reports, report => Assert.True(
            report.IsValid,
            "a valid document validated concurrently reported: "
            + string.Join("; ", report.Issues
                .Where(issue => issue.Severity == ValidationSeverity.Error)
                .Select(issue => $"{issue.Location}: {issue.Message}"))));
    }

    [Fact]
    public async Task FN_VAL_004_concurrent_validation_still_finds_what_is_actually_wrong()
    {
        // The other half. A gate that returned "valid" for everything would pass the case above
        // and be worthless, so an invalid document has to stay invalid under the same load.
        var validator = Validator();

        var reports = await Task.WhenAll(Enumerable.Range(0, Concurrent).Select(_ =>
        {
            var broken = Document();

            // Bundle.type is required, so a document without one cannot be valid however many
            // callers ask at once.
            broken.Type = null;
            return validator.ValidateAsync(broken);
        }));

        Assert.All(reports, report => Assert.False(report.IsValid));
    }

    [Fact]
    public async Task FN_VAL_004_the_synchronous_path_agrees_with_the_asynchronous_one()
    {
        // Both exist because not every caller can await, and two gates around one validator
        // would be one gate too many. This is what stops them drifting apart.
        var validator = Validator();
        var document = Document();

        Assert.Equal(validator.Validate(document).IsValid, (await validator.ValidateAsync(document)).IsValid);
    }
}
