using Epi.ContentCore;
using Hl7.Fhir.Model;

namespace Epi.Search;

/// <summary>
/// What of a document is searchable, read from the content itself (FN-SCH-001).
/// </summary>
/// <remarks>
/// Derived at projection time rather than stored on the content, so a change to what is
/// searchable is a rebuild rather than a migration of the canonical store.
/// </remarks>
public sealed record SearchableContent(
    string Title,
    DocumentScope Scope,
    string? Language,
    string? Product,
    string? DocumentType,
    string Text)
{
    /// <summary>Reads the searchable metadata and text out of a document Bundle.</summary>
    /// <exception cref="ArgumentException">
    /// If the content carries no affiliate and market scope. Unscoped content cannot be indexed,
    /// because there is no scope to filter it by and it would therefore match every caller's
    /// query - the exact failure ADR-022 decision 3 guards the query side against.
    /// </exception>
    public static SearchableContent Of(Bundle bundle, IdentifierAuthority? authority = null) =>
        throw new NotImplementedException();
}
