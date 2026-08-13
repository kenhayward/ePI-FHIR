using Hl7.Fhir.Model;

namespace Epi.ContentCore;

/// <summary>
/// Minting and stamping of canonical identity (ADR-015). Shared by every store so that two
/// implementations cannot drift into two different notions of what identity means.
/// </summary>
internal static class ContentIdentity
{
    /// <summary>A fresh, opaque, time-ordered identity in the platform's own system.</summary>
    public static DocumentIdentity Mint() => new(
        ContentCoreDefaults.DocumentIdentifierSystem,
        Guid.CreateVersion7().ToString());

    /// <summary>Writes identity and version onto a bundle about to be stored.</summary>
    public static Bundle Stamp(Bundle bundle, DocumentIdentity identity, int version)
    {
        bundle.Identifier = new Identifier(identity.System, identity.Value);

        // The version travels with the content rather than relying on the server's own
        // versioning, which ADR-015 keeps us independent of.
        bundle.Meta ??= new Meta();
        bundle.Meta.Tag = [
            .. bundle.Meta.Tag.Where(t => t.System != ContentCoreDefaults.DocumentVersionTagSystem),
            new Coding(ContentCoreDefaults.DocumentVersionTagSystem, version.ToString())
        ];

        return bundle;
    }

    /// <summary>The platform version stamped on a stored bundle, if any.</summary>
    public static int? VersionOf(Bundle bundle) =>
        bundle.Meta?.Tag
            .Where(t => t.System == ContentCoreDefaults.DocumentVersionTagSystem)
            .Select(t => int.TryParse(t.Code, out var v) ? v : (int?)null)
            .FirstOrDefault(v => v is not null);

    /// <summary>
    /// Submitted content must not claim an identifier in the platform's own system: identity
    /// is minted here (ADR-015), or an external system could collide with our identity space.
    /// </summary>
    public static void RejectClaimedIdentity(Bundle bundle)
    {
        if (string.Equals(bundle.Identifier?.System,
                ContentCoreDefaults.DocumentIdentifierSystem, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidEpiBundleException([
                "Submitted content must not carry an identifier in the platform's own identifier "
                + "system: identity is minted by the platform (ADR-015). A legacy or external "
                + "identifier belongs in a secondary identifier with provenance to its source."
            ]);
        }
    }
}
