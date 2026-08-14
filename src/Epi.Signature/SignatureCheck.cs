using Epi.Lifecycle;

namespace Epi.Signature;

/// <summary>
/// Answers the lifecycle engine's question about a signature (FN-WFL-003, CAP-WFL-003).
/// </summary>
public sealed class SignatureCheck(ISignatureStore store) : ISignatureCheck
{
    private readonly ISignatureStore _store = store ?? throw new ArgumentNullException(nameof(store));

    public Task<SignatureCheckResult> IsValidAsync(
        string reference,
        VersionRef version,
        string actor,
        string meaning,
        CancellationToken cancellationToken = default) => throw new NotImplementedException();
}
