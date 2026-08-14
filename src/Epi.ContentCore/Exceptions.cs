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

/// <summary>
/// Raised when a caller creates a version that already exists (ADR-025 decision 4).
/// </summary>
/// <remarks>
/// Two authors both reading version 3 and both writing version 4 is a conflict, not a queue.
/// Silently assigning the second one version 5 would keep both, in an order neither intended,
/// with the later one appearing to have been written knowing the earlier - which is what a
/// version lineage is supposed to mean.
/// </remarks>
public sealed class VersionConflictException(DocumentIdentity identity, int version)
    : Exception($"Version {version} of {identity} already exists.")
{
    public DocumentIdentity Identity { get; } = identity;

    public int Version { get; } = version;
}
