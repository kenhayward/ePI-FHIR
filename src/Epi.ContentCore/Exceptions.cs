namespace Epi.ContentCore;

/// <summary>
/// Raised when submitted content is not a usable ePI document. Carries every problem found
/// rather than only the first (CAP-VAL-005).
/// </summary>
public sealed class InvalidEpiBundleException(IReadOnlyList<string> problems)
    : Exception(BuildMessage(problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;

    private static string BuildMessage(IReadOnlyList<string> problems) =>
        $"The content is not a valid ePI document:{Environment.NewLine}  "
        + string.Join($"{Environment.NewLine}  ", problems);
}

/// <summary>Raised when a new version is requested for a document that does not exist.</summary>
public sealed class UnknownDocumentException(DocumentIdentity identity)
    : Exception($"No document exists with identity {identity}.")
{
    public DocumentIdentity Identity { get; } = identity;
}
