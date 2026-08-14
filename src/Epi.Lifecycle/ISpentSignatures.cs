namespace Epi.Lifecycle;

/// <summary>
/// Whether a signature has already been used for a transition (FN-WFL-003, FN-LCM-007).
/// </summary>
/// <remarks>
/// This is how a signature is made single-use. Nothing is written back to a signature to mark it
/// spent - an append-only store could not do that - so the question is answered from the
/// transition history, which is the record of what has actually been signed for.
/// <para>
/// A port rather than a method on one store, because a signature is spent platform-wide. A
/// signature offered for a regulatory submission must be refused if it was already used for an
/// internal approval, and neither store can see the other's records.
/// </para>
/// </remarks>
public interface ISpentSignatures
{
    Task<bool> IsSignatureUsedAsync(string reference, CancellationToken cancellationToken = default);
}

/// <summary>Combines the stores that record signature use, so single use holds across all of them.</summary>
public sealed class SpentSignatures(params ISpentSignatures[] ledgers) : ISpentSignatures
{
    private readonly ISpentSignatures[] _ledgers = ledgers is { Length: > 0 }
        ? ledgers
        : throw new ArgumentException(
            "At least one ledger is required, or no signature could ever be found to be spent.",
            nameof(ledgers));

    public async Task<bool> IsSignatureUsedAsync(
        string reference, CancellationToken cancellationToken = default)
    {
        foreach (var ledger in _ledgers)
        {
            if (await ledger.IsSignatureUsedAsync(reference, cancellationToken))
            {
                return true;
            }
        }

        return false;
    }
}
