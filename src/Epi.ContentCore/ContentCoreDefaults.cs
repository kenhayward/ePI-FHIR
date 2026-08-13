namespace Epi.ContentCore;

/// <summary>
/// Convenience access to the identifier systems in use when nothing is configured.
/// </summary>
/// <remarks>
/// The values themselves live in <see cref="IdentifierAuthority"/> and are configuration
/// (ADR-017). These members exist so that call sites reading the demonstration defaults stay
/// readable; anything that can be configured should take an
/// <see cref="IdentifierAuthority"/> instead.
/// </remarks>
public static class ContentCoreDefaults
{
    public static string DocumentIdentifierSystem => IdentifierAuthority.Demonstration.DocumentSystem;

    public static string DocumentVersionTagSystem => IdentifierAuthority.Demonstration.VersionTagSystem;
}
