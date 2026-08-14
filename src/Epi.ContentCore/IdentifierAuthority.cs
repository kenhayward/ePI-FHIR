namespace Epi.ContentCore;

/// <summary>
/// The namespaces this deployment mints identifiers and tags into (ADR-017).
/// </summary>
/// <remarks>
/// Configuration rather than code: an adopting organisation sets these once, in
/// <c>config/identifiers.json</c>, without touching the codebase. The same authority is used in
/// every environment of a deployment, because per-environment identity would mean the same
/// document has a different identity in development and production, and could not be promoted
/// between them.
/// </remarks>
public sealed record IdentifierAuthority(
    string DocumentSystem,
    string VersionTagSystem,
    string AffiliateTagSystem,
    string MarketTagSystem,
    string TemplateSystem = "",
    string TemplateVersionTagSystem = "",
    string UnitSystem = "",
    string UnitReferenceExtension = "")
{
    /// <summary>
    /// The authority used when none is configured.
    /// </summary>
    /// <remarks>
    /// Uses a domain RFC 2606 reserves for documentation, which therefore can never be owned by
    /// anyone. That is deliberate: this repository is a demonstration whose adopting
    /// organisation is not yet known, and an unset authority should be conspicuously wrong
    /// rather than plausibly right. ADR-017 records how an adopter determines their own.
    /// </remarks>
    public static IdentifierAuthority Demonstration { get; } = new(
        "https://epi.example.org/identifier/document",
        "https://epi.example.org/tag/document-version",
        "https://epi.example.org/tag/affiliate",
        "https://epi.example.org/tag/market",
        "https://epi.example.org/tag/template",
        "https://epi.example.org/tag/template-version",
        "https://epi.example.org/tag/reusable-unit",
        "https://epi.example.org/extension/unit-reference");

    /// <summary>True when this is the demonstration placeholder rather than a real authority.</summary>
    public bool IsDemonstration => this == Demonstration;
}
