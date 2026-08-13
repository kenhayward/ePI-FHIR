using Firely.Fhir.Validation;
using Hl7.Fhir.Model;
using Hl7.Fhir.Specification.Source;
using Hl7.Fhir.Specification.Terminology;

namespace Epi.Validation;

/// <summary>
/// Technical validation (capability 11): structural conformance against the pinned
/// definitions, and reference integrity within the document.
/// </summary>
/// <remarks>
/// Deliberately not completeness or business rules, which are capability 12's job and use the
/// market template as the yardstick. This gate answers "is this a well-formed, internally
/// consistent FHIR document?", nothing more.
/// </remarks>
public sealed class StructuralValidator
{
    // The SDK caches compiled schemas in state shared through the resolver, and concurrent
    // validation can make an uncached canonical fail to resolve. The validator reports that as
    // an error, so the gate would reject valid content intermittently. Serialised until the
    // shared state is understood well enough to do better: a slow gate is acceptable, a gate
    // that rejects valid labels under load is not.
    private static readonly SemaphoreSlim Gate = new(1, 1);

    private readonly Validator _validator;

    public StructuralValidator(IAsyncResourceResolver resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);

        // Terminology is resolved from the same pinned packages, so code validation is as
        // offline and reproducible as structural validation (ADR-016).
        _validator = new Validator(resolver, new LocalTerminologyService(resolver));
    }

    /// <summary>Validates one document, reporting every issue found.</summary>
    public ValidationReport Validate(Bundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        Gate.Wait();
        OperationOutcome outcome;
        try
        {
            outcome = _validator.Validate(bundle);
        }
        finally
        {
            Gate.Release();
        }

        var issues = outcome.Issue
            .Select(Translate)
            .Concat(DanglingReferences(bundle))
            .ToList();

        return new ValidationReport(issues);
    }

    private static ValidationIssue Translate(OperationOutcome.IssueComponent issue) => new(
        issue.Severity switch
        {
            OperationOutcome.IssueSeverity.Fatal or OperationOutcome.IssueSeverity.Error
                => ValidationSeverity.Error,
            OperationOutcome.IssueSeverity.Warning => ValidationSeverity.Warning,
            _ => ValidationSeverity.Information,
        },
        issue.Expression.FirstOrDefault() ?? issue.Location?.FirstOrDefault() ?? "(document)",
        issue.Details?.Text ?? issue.Diagnostics ?? "Unspecified validation issue.");

    /// <summary>
    /// References inside a document must resolve within it (CAP-VAL-003). A document Bundle is
    /// self-contained: a local reference pointing at nothing means the content cannot be
    /// rendered or published faithfully, and the base specification does not catch it.
    /// References to other servers are out of scope here, not defects.
    /// </summary>
    private static IEnumerable<ValidationIssue> DanglingReferences(Bundle bundle)
    {
        var present = new HashSet<string>(StringComparer.Ordinal);
        foreach (var entry in bundle.Entry.Where(e => e.Resource is not null))
        {
            if (!string.IsNullOrWhiteSpace(entry.FullUrl))
            {
                present.Add(entry.FullUrl);
            }

            var resource = entry.Resource!;
            if (!string.IsNullOrWhiteSpace(resource.Id))
            {
                present.Add($"{resource.TypeName}/{resource.Id}");
                present.Add($"#{resource.Id}");
            }
        }

        foreach (var (location, reference) in References(bundle))
        {
            if (IsExternal(reference) || present.Contains(reference))
            {
                continue;
            }

            yield return new ValidationIssue(
                ValidationSeverity.Error,
                location,
                $"The reference '{reference}' does not resolve to an entry in this document. "
                + "A document Bundle must be self-contained.");
        }
    }

    private static IEnumerable<(string Location, string Reference)> References(Bundle bundle)
    {
        foreach (var entry in bundle.Entry.Where(e => e.Resource is not null))
        {
            var resource = entry.Resource!;
            foreach (var node in Walk(resource).OfType<ResourceReference>())
            {
                if (!string.IsNullOrWhiteSpace(node.Reference))
                {
                    yield return ($"{resource.TypeName}.reference", node.Reference);
                }
            }
        }
    }

    /// <summary>Every element beneath a resource, depth first.</summary>
    private static IEnumerable<Base> Walk(Base element)
    {
        foreach (var value in element.EnumerateElements().Select(e => e.Value))
        {
            // An element's value is either a single Base or a collection of them. Repeating
            // elements such as Composition.subject are the interesting case: missing them
            // would mean references in a list were never checked at all.
            foreach (var child in Flatten(value))
            {
                yield return child;
                foreach (var descendant in Walk(child))
                {
                    yield return descendant;
                }
            }
        }
    }

    private static IEnumerable<Base> Flatten(object? value)
    {
        switch (value)
        {
            case Base single:
                yield return single;
                break;
            case System.Collections.IEnumerable many:
                foreach (var item in many.OfType<Base>())
                {
                    yield return item;
                }

                break;
        }
    }

    /// <summary>A reference the document is not expected to contain.</summary>
    private static bool IsExternal(string reference) =>
        reference.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
        || reference.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
