using System.Collections.Concurrent;

namespace Epi.Signature;

/// <summary>
/// A signature store held in memory, for tests and for the demonstration until the durable one
/// lands. Append-only like its durable counterpart, so behaviour does not change with it.
/// </summary>
public sealed class InMemorySignatureStore : ISignatureStore
{
    private readonly ConcurrentDictionary<string, SignatureManifest> _signatures =
        new(StringComparer.Ordinal);

    public Task AppendAsync(SignatureManifest manifest, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(manifest);

        if (!_signatures.TryAdd(manifest.Reference, manifest))
        {
            // Overwriting would be an amendment, which is the one thing this store must not
            // do (ADR-020 decision 7). The durable store enforces the same rule at the
            // database, as the audit sink already does.
            throw new InvalidOperationException(
                $"A signature with reference '{manifest.Reference}' is already recorded, and "
                + "signatures are append-only.");
        }

        return Task.CompletedTask;
    }

    public Task<SignatureManifest?> FindAsync(
        string reference, CancellationToken cancellationToken = default) =>
        Task.FromResult(_signatures.TryGetValue(reference, out var manifest) ? manifest : null);
}
