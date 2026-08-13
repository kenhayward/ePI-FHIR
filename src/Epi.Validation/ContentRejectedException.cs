namespace Epi.Validation;

/// <summary>
/// Raised when content fails validation at a gate. Carries every issue, so an author sees the
/// whole list rather than discovering problems one submission at a time (CAP-VAL-005).
/// </summary>
public sealed class ContentRejectedException(IReadOnlyList<ValidationIssue> issues)
    : Exception(BuildMessage(issues))
{
    public IReadOnlyList<ValidationIssue> Issues { get; } = issues;

    private static string BuildMessage(IReadOnlyList<ValidationIssue> issues) =>
        $"Content was rejected at the validation gate:{Environment.NewLine}  "
        + string.Join($"{Environment.NewLine}  ",
            issues.Select(i => $"{i.Severity} at {i.Location}: {i.Message}"));
}
