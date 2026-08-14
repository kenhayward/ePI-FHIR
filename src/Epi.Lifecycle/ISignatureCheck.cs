namespace Epi.Lifecycle;

/// <summary>Whether a signature is acceptable at a gate, and if not, why not.</summary>
public sealed record SignatureCheckResult(bool IsValid, string? Problem = null)
{
    public static SignatureCheckResult Valid { get; } = new(true);

    public static SignatureCheckResult Invalid(string problem) => new(false, problem);
}

/// <summary>
/// Confirms that a signature reference is a real signature, by this actor, over this version,
/// meaning what the gate requires (FN-WFL-003, CAP-WFL-003).
/// </summary>
/// <remarks>
/// Declared here and implemented in the signature module rather than the other way round, so
/// that the lifecycle engine stays free of any dependency on how signing works. A different
/// signature mechanism is a different implementation of this interface and nothing else.
/// <para>
/// Deliberately asks only whether a signature is valid. Whether it has already been spent is
/// the lifecycle store's question, because the transition history is the record of what has
/// been signed for - and answering it there means a signature is never mutated to mark it used,
/// which an append-only store could not do anyway.
/// </para>
/// </remarks>
public interface ISignatureCheck
{
    Task<SignatureCheckResult> IsValidAsync(
        string reference,
        VersionRef version,
        string actor,
        string meaning,
        CancellationToken cancellationToken = default);
}
