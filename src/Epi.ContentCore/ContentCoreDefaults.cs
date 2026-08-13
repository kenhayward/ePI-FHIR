namespace Epi.ContentCore;

public static class ContentCoreDefaults
{
    /// <summary>
    /// The identifier system documents are minted into.
    /// </summary>
    /// <remarks>
    /// DEVELOPMENT VALUE ONLY. ADR-015 records the identifier authority as an open point: this
    /// must be replaced with a real authority before any data exists outside a development
    /// environment, because identifiers are permanent.
    /// </remarks>
    public const string DocumentIdentifierSystem = "https://epi.example.org/identifier/document";

    /// <summary>
    /// The tag system carrying the platform's own version number on a stored document.
    /// </summary>
    /// <remarks>
    /// ADR-015 decision 4: the version is ours, a monotonic integer over the document identity.
    /// It is deliberately not the FHIR server's meta.versionId, which is server-assigned and
    /// would not survive a change of server (ADR-003).
    /// </remarks>
    public const string DocumentVersionTagSystem = "https://epi.example.org/tag/document-version";
}
