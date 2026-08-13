using Hl7.Fhir.Model;

namespace Epi.ContentCore;

/// <summary>
/// Which affiliate and market a document belongs to. The attributes authorisation decisions
/// are made against (CAP-IAM-003), carried by the content itself.
/// </summary>
public sealed record DocumentScope(string Affiliate, string Market);

/// <summary>
/// Reads and writes a document's scope. Stored as tags on the content rather than in a side
/// table, so scope travels with the document: a copy that has lost its scope cannot be
/// authorised, and would otherwise be authorised by whatever the surrounding record claimed.
/// </summary>
public static class ContentScope
{
    public static Bundle Stamp(Bundle bundle, DocumentScope scope, IdentifierAuthority? authority = null)
    {
        var systems = authority ?? IdentifierAuthority.Demonstration;
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(scope);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Affiliate);
        ArgumentException.ThrowIfNullOrWhiteSpace(scope.Market);

        bundle.Meta ??= new Meta();
        bundle.Meta.Tag = [
            .. bundle.Meta.Tag.Where(t =>
                t.System != systems.AffiliateTagSystem && t.System != systems.MarketTagSystem),
            new Coding(systems.AffiliateTagSystem, scope.Affiliate),
            new Coding(systems.MarketTagSystem, scope.Market),
        ];

        return bundle;
    }

    /// <summary>The scope a document carries, or null when it carries none.</summary>
    public static DocumentScope? Of(Bundle bundle, IdentifierAuthority? authority = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var systems = authority ?? IdentifierAuthority.Demonstration;
        var affiliate = Tag(bundle, systems.AffiliateTagSystem);
        var market = Tag(bundle, systems.MarketTagSystem);

        return affiliate is null || market is null ? null : new DocumentScope(affiliate, market);
    }

    private static string? Tag(Bundle bundle, string system) => bundle.Meta?.Tag
        .Where(t => t.System == system)
        .Select(t => t.Code)
        .FirstOrDefault(c => !string.IsNullOrWhiteSpace(c));
}
