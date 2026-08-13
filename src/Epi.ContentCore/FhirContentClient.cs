using System.Net.Http.Headers;
using Hl7.Fhir.Rest;

namespace Epi.ContentCore;

/// <summary>
/// Builds a FHIR client configured the way the content store needs it.
/// </summary>
public static class FhirContentClient
{
    /// <summary>A client for the content store at the given FHIR base URL.</summary>
    /// <remarks>
    /// <para>
    /// <b>Search caching is disabled deliberately.</b> HAPI, and FHIR servers generally, may
    /// serve a cached result set for a repeated search. That is a sensible default for
    /// reporting and a correctness bug for us: immediately after storing version 2, a search
    /// for a document's versions must return both, not the set that was current a moment ago.
    /// A store that can miss a version it has just written cannot support version lineage
    /// (CAP-LCM-002) or reconstruction (CAP-LCM-006).
    /// </para>
    /// <para>
    /// This surfaced as five failing conformance tests the first time the adapter met a real
    /// server: every one of them involved reading back a second version.
    /// </para>
    /// </remarks>
    public static FhirClient Create(string baseUrl)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baseUrl);

        var http = new HttpClient();
        http.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue
        {
            NoCache = true,
            NoStore = true,
        };

        return new FhirClient(baseUrl, http, new FhirClientSettings
        {
            PreferredFormat = ResourceFormat.Json,
            VerifyFhirVersion = false,
        });
    }
}
