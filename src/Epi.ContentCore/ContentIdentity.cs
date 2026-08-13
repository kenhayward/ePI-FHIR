using Hl7.Fhir.Model;

namespace Epi.ContentCore;

/// <summary>
/// Minting and stamping of canonical identity (ADR-015). Shared by every store so that two
/// implementations cannot drift into two different notions of what identity means.
/// </summary>
/// <remarks>
/// Public because validation needs it too: FHIR constraint bdl-9 requires a document Bundle to
/// carry an identifier, and the platform mints that identifier rather than the submitter
/// supplying it. A write gate must therefore validate the stamped form - the artefact that will
/// actually be stored - not the draft that arrived.
/// </remarks>
public static class ContentIdentity
{
    /// <summary>A fresh, opaque, time-ordered identity in the platform's own system.</summary>
    public static DocumentIdentity Mint(IdentifierAuthority? authority = null) => new(
        (authority ?? IdentifierAuthority.Demonstration).DocumentSystem,
        Guid.CreateVersion7().ToString());

    /// <summary>Writes identity and version onto a bundle about to be stored.</summary>
    public static Bundle Stamp(
        Bundle bundle, DocumentIdentity identity, int version, IdentifierAuthority? authority = null)
    {
        var systems = authority ?? IdentifierAuthority.Demonstration;
        bundle.Identifier = new Identifier(identity.System, identity.Value);

        // Sections are identified as part of stamping, so every stored version has them and no
        // write path can produce content without them (ADR-015 decision 6).
        SectionIdentity.AssignMissing(bundle);

        // The version travels with the content rather than relying on the server's own
        // versioning, which ADR-015 keeps us independent of.
        bundle.Meta ??= new Meta();
        bundle.Meta.Tag = [
            .. bundle.Meta.Tag.Where(t => t.System != systems.VersionTagSystem),
            new Coding(systems.VersionTagSystem, version.ToString())
        ];

        return bundle;
    }

    /// <summary>The platform version stamped on a stored bundle, if any.</summary>
    public static int? VersionOf(Bundle bundle, IdentifierAuthority? authority = null) =>
        bundle.Meta?.Tag
            .Where(t => t.System == (authority ?? IdentifierAuthority.Demonstration).VersionTagSystem)
            .Select(t => int.TryParse(t.Code, out var v) ? v : (int?)null)
            .FirstOrDefault(v => v is not null);

    /// <summary>
    /// Submitted content must not claim an identifier in the platform's own system: identity
    /// is minted here (ADR-015), or an external system could collide with our identity space.
    /// </summary>
    public static void RejectClaimedIdentity(Bundle bundle, IdentifierAuthority? authority = null)
    {
        if (string.Equals(bundle.Identifier?.System,
                (authority ?? IdentifierAuthority.Demonstration).DocumentSystem,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidEpiBundleException([
                "Submitted content must not carry an identifier in the platform's own identifier "
                + "system: identity is minted by the platform (ADR-015). A legacy or external "
                + "identifier belongs in a secondary identifier with provenance to its source."
            ]);
        }
    }
}
