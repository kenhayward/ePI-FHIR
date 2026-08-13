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
}
