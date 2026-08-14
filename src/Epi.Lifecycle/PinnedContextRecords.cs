namespace Epi.Lifecycle;

/// <summary>
/// One conformance package a version was validated against, as it was pinned (ADR-016).
/// </summary>
/// <remarks>
/// The digest is what makes the pin checkable rather than merely descriptive: a name and a
/// version say which package was meant, and only the digest says which bytes were used.
/// </remarks>
public sealed record PinnedPackage(string Name, string Version, string Sha256);

/// <summary>
/// What a version was approved against, recorded at approval (CAP-LCM-011, ADR-023).
/// </summary>
/// <remarks>
/// Everything here is configuration at the moment of approval, and configuration moves. Asked
/// later, the platform could say what it would validate against today, which is a true answer
/// to a different question. This is the answer to the question an inspection asks.
/// </remarks>
public sealed record PinnedContext(
    VersionRef Version,
    string ContentHash,
    string StateModel,
    string State,
    IReadOnlyList<PinnedPackage> Packages,
    string IdentifierAuthority,
    DateTimeOffset PinnedAt,
    string? Template = null,
    int? TemplateVersion = null);

/// <summary>
/// The pinned validating contexts, append-only like everything else that is evidence.
/// </summary>
public interface IPinnedContextStore
{
    /// <summary>
    /// Records what a version was approved against.
    /// </summary>
    /// <exception cref="ContextAlreadyPinnedException">
    /// If this version already has a pin. A record that can be replaced is not a record, and an
    /// approval happens once.
    /// </exception>
    Task PinAsync(PinnedContext context, CancellationToken cancellationToken = default);

    /// <summary>The context pinned for this version, or null where none was.</summary>
    Task<PinnedContext?> ForAsync(VersionRef version, CancellationToken cancellationToken = default);
}

/// <summary>Raised when a version that already has a pinned context is pinned again.</summary>
public sealed class ContextAlreadyPinnedException(VersionRef version)
    : Exception($"{version} already has a pinned validating context, and a pin is not replaceable.")
{
    public VersionRef Version { get; } = version;
}
