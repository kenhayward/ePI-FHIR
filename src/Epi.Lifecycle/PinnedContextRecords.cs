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
/// Which version of which code system answered, at the moment it was asked (ADR-036 decision 2).
/// </summary>
/// <param name="Version">
/// The version the source said answered, or null where it could not say. Null is recorded as
/// null and never filled in with the version the platform would use today: that is a true
/// answer to a different question, which is the trap ADR-023 exists to avoid.
/// </param>
public sealed record TerminologyBinding(string System, string? Version)
{
    public string System { get; } = string.IsNullOrWhiteSpace(System)
        ? throw new ArgumentException(
            "A terminology binding must name the system it binds. A version without the system "
            + "it versions says nothing at all.",
            nameof(System))
        : System;

    /// <summary>Whether the source could say which version answered.</summary>
    public bool IsVersioned => !string.IsNullOrWhiteSpace(Version);

    public override string ToString() =>
        IsVersioned ? $"{System}|{Version}" : $"{System} (unversioned)";
}

/// <summary>
/// What a version was approved against, recorded at approval (CAP-LCM-011, ADR-023).
/// </summary>
/// <remarks>
/// Everything here is configuration at the moment of approval, and configuration moves. Asked
/// later, the platform could say what it would validate against today, which is a true answer
/// to a different question. This is the answer to the question an inspection asks.
/// </remarks>
/// <param name="TerminologyBindings">
/// The terminology in force at approval (ADR-036 decision 3). Empty means the approval was
/// asked and had none, which is deliberately distinguishable from a pin taken before bindings
/// were recorded at all.
/// </param>
public sealed record PinnedContext(
    VersionRef Version,
    string ContentHash,
    string StateModel,
    string State,
    IReadOnlyList<PinnedPackage> Packages,
    string IdentifierAuthority,
    DateTimeOffset PinnedAt,
    string? Template = null,
    int? TemplateVersion = null,
    IReadOnlyList<TerminologyBinding>? TerminologyBindings = null)
{
    public IReadOnlyList<TerminologyBinding> TerminologyBindings { get; } =
        TerminologyBindings ?? [];
}

/// <summary>
/// What a caller must supply for an approval to be pinnable (ADR-024 decision 3).
/// </summary>
/// <remarks>
/// The ingredients, not the record. The lifecycle engine knows when a transition lands on the
/// approved state and what its timestamp is; it does not know about FHIR bytes or conformance
/// packages. Passing the parts rather than the finished pin keeps each side ignorant of the
/// other's business and puts the decision about <em>when</em> to pin with the thing that knows.
/// </remarks>
public sealed record ApprovalContext(
    string ContentHash,
    IReadOnlyList<PinnedPackage> Packages,
    string IdentifierAuthority,
    string? Template = null,
    int? TemplateVersion = null,
    IReadOnlyList<TerminologyBinding>? TerminologyBindings = null)
{
    public IReadOnlyList<TerminologyBinding> TerminologyBindings { get; } = Checked(
        TerminologyBindings ?? []);

    /// <summary>
    /// Refuses two bindings for one system, because which version answered would otherwise
    /// depend on which was read first - and the point of recording a version is that it is not
    /// a matter of chance.
    /// </summary>
    private static IReadOnlyList<TerminologyBinding> Checked(
        IReadOnlyList<TerminologyBinding> bindings)
    {
        var duplicated = bindings
            .GroupBy(binding => binding.System, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        return duplicated is null
            ? bindings
            : throw new ArgumentException(
                $"'{duplicated.Key}' is bound twice, to {string.Join(" and ", duplicated.Select(b => b.Version ?? "no version"))}. "
                + "Which one answered would depend on the order they were read in.",
                nameof(bindings));
    }
}

/// <summary>
/// The pinned validating contexts, read-only (ADR-024 decision 2).
/// </summary>
/// <remarks>
/// There is deliberately no write here. A pin is written by the same append that records the
/// transition it belongs to, in one transaction, and a second way to write one would be a way
/// to write a pin with no transition behind it.
/// </remarks>
public interface IPinnedContextStore
{
    /// <summary>The context pinned for this version, or null where none was.</summary>
    Task<PinnedContext?> ForAsync(VersionRef version, CancellationToken cancellationToken = default);
}

/// <summary>Raised when a version that already has a pinned context is pinned again.</summary>
public sealed class ContextAlreadyPinnedException(VersionRef version)
    : Exception($"{version} already has a pinned validating context, and a pin is not replaceable.")
{
    public VersionRef Version { get; } = version;
}
