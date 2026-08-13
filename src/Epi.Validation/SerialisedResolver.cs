using Hl7.Fhir.Model;
using Hl7.Fhir.Specification.Source;

namespace Epi.Validation;

/// <summary>
/// Serialises access to a resource resolver.
/// </summary>
/// <remarks>
/// The SDK's caching resolvers are not safe for concurrent first use: two callers arriving at
/// the same uncached canonical can each get a resolution failure. The validator reports such a
/// failure as an error, so the write gate would reject content that is perfectly valid -
/// intermittently, and more often under load. Rejecting a valid label because two requests
/// arrived together is not a trade-off worth making for throughput, and resolutions are cached
/// after the first, so contention is brief and confined to start-up.
/// </remarks>
#pragma warning disable CS0618 // Implementing the interface as declared, not calling deprecated API.
internal sealed class SerialisedResolver(IAsyncResourceResolver inner) : IAsyncResourceResolver
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<Resource?> ResolveByUriAsync(string uri)
    {
        await _gate.WaitAsync();
        try
        {
            return await inner.ResolveByUriAsync(uri);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<Resource?> ResolveByCanonicalUriAsync(string uri)
    {
        await _gate.WaitAsync();
        try
        {
            return await inner.ResolveByCanonicalUriAsync(uri);
        }
        finally
        {
            _gate.Release();
        }
    }
}
#pragma warning restore CS0618
