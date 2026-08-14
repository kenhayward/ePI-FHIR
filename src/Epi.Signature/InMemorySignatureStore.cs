namespace Epi.Signature;

/// <summary>
/// A signature store held in memory, for tests and for the demonstration until the durable one
/// lands. Append-only like its durable counterpart, so behaviour does not change with it.
/// </summary>
public sealed class InMemorySignatureStore : ISignatureStore
{
    public Task AppendAsync(SignatureManifest manifest, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();

    public Task<SignatureManifest?> FindAsync(
        string reference, CancellationToken cancellationToken = default) =>
        throw new NotImplementedException();
}
