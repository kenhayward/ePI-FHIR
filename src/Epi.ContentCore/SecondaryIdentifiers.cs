using Hl7.Fhir.Model;

namespace Epi.ContentCore;

/// <summary>An identifier content arrived with, in a system this platform does not own.</summary>
public sealed record SecondaryIdentifier(string System, string Value);

/// <summary>
/// The identifiers content brought with it, kept but never acted on (ADR-027).
/// </summary>
/// <remarks>
/// <c>Bundle.identifier</c> is the platform's own and only that (ADR-015). What a document
/// arrived carrying - a legacy identifier from a migration, a submitter's own reference - goes
/// on the anchoring <c>Composition</c>, because those identify the thing the content is about
/// in another system rather than this document as this platform holds it.
/// <para>
/// Nothing here resolves by a secondary identifier, mints from one, or rejects a duplicate. A
/// legacy system that reused an identifier is a fact to record, not an error, and a migration
/// that could not record it could not be reconciled against the system it came from.
/// </para>
/// </remarks>
public static class SecondaryIdentifiers
{
    /// <summary>Records an identifier the content arrived with.</summary>
    /// <exception cref="InvalidEpiBundleException">
    /// If it is in the platform's own identifier system. Identity is minted, never submitted
    /// (ADR-015), and a secondary identifier claiming to be one would be indistinguishable from
    /// the real thing everywhere downstream.
    /// </exception>
    public static Bundle Add(
        Bundle bundle, SecondaryIdentifier identifier, IdentifierAuthority? authority = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(identifier);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier.System);
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier.Value);

        var systems = authority ?? IdentifierAuthority.Demonstration;
        if (string.Equals(identifier.System, systems.DocumentSystem, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidEpiBundleException([
                "A secondary identifier must not be in the platform's own identifier system: "
                + "identity is minted by the platform, never submitted (ADR-015, ADR-027)."
            ]);
        }

        var composition = Anchor(bundle)
            ?? throw new InvalidEpiBundleException([
                "A secondary identifier is recorded on the anchoring Composition, and this "
                + "content has none."
            ]);

        if (!composition.Identifier.Any(
                existing => string.Equals(existing.System, identifier.System, StringComparison.Ordinal)
                            && string.Equals(existing.Value, identifier.Value, StringComparison.Ordinal)))
        {
            composition.Identifier.Add(new Identifier(identifier.System, identifier.Value));
        }

        return bundle;
    }

    /// <summary>Every identifier this content arrived with, in the order recorded.</summary>
    public static IReadOnlyList<SecondaryIdentifier> Of(
        Bundle bundle, IdentifierAuthority? authority = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var systems = authority ?? IdentifierAuthority.Demonstration;

        return
        [
            .. (Anchor(bundle)?.Identifier ?? [])
                .Where(i => !string.IsNullOrWhiteSpace(i.System) && !string.IsNullOrWhiteSpace(i.Value))

                // The platform's own system is excluded on the way out as well as refused on the
                // way in: content restored from a backup taken before this rule existed must not
                // start reading as though it had submitted its own identity.
                .Where(i => !string.Equals(i.System, systems.DocumentSystem, StringComparison.OrdinalIgnoreCase))
                .Select(i => new SecondaryIdentifier(i.System!, i.Value!)),
        ];
    }

    private static Composition? Anchor(Bundle bundle) =>
        bundle.Entry.Count > 0 ? bundle.Entry[0].Resource as Composition : null;
}
