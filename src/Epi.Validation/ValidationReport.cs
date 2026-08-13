namespace Epi.Validation;

/// <summary>How much an issue matters at a gate (CAP-VAL-005).</summary>
public enum ValidationSeverity
{
    Information,
    Warning,
    Error,
}

/// <summary>
/// One finding, located precisely enough for an author to act on it without reading FHIR.
/// </summary>
public sealed record ValidationIssue(ValidationSeverity Severity, string Location, string Message);

/// <summary>The outcome of validating one document.</summary>
public sealed record ValidationReport(IReadOnlyList<ValidationIssue> Issues)
{
    /// <summary>No error-severity issues. Warnings do not block a gate (CAP-VAL-007).</summary>
    public bool IsValid => !Issues.Any(i => i.Severity == ValidationSeverity.Error);
}
