using System.Text;
using System.Xml;
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
    public static SearchableContent Of(Bundle bundle, IdentifierAuthority? authority = null)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var scope = ContentScope.Of(bundle, authority)
            ?? throw new ArgumentException(
                "The content carries no affiliate and market scope, so it cannot be indexed: a "
                + "document with no scope matches every caller's query.",
                nameof(bundle));

        var composition = bundle.Entry.Count > 0 ? bundle.Entry[0].Resource as Composition : null;

        var text = new StringBuilder();
        Append(text, composition?.Title);
        foreach (var section in Flatten(composition?.Section ?? []))
        {
            Append(text, section.Title);
            Append(text, PlainText(section.Text?.Div));
        }

        return new SearchableContent(
            composition?.Title ?? string.Empty,
            scope,
            Blank(composition?.Language),

            // Product binds properly to master data (capability 5), which does not exist yet.
            // What the content names as its subject is the honest stand-in, and content that
            // names none is indexed without one rather than refused.
            Blank(composition?.Subject.FirstOrDefault()?.Display),
            Blank(composition?.Type?.Coding.FirstOrDefault()?.Code),
            text.ToString());
    }

    private static IEnumerable<Composition.SectionComponent> Flatten(
        IEnumerable<Composition.SectionComponent> sections)
    {
        foreach (var section in sections)
        {
            yield return section;
            foreach (var nested in Flatten(section.Section))
            {
                yield return nested;
            }
        }
    }

    private static void Append(StringBuilder text, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            text.Append(value).Append('\n');
        }
    }

    private static string? Blank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// The words in a narrative, without its markup.
    /// </summary>
    /// <remarks>
    /// Matching on the markup would let a query for "div" return the corpus, and a word split
    /// across two elements match nothing. Narrative is a constrained XHTML fragment
    /// (CAP-SCM-003), so it parses as XML; anything that does not is indexed as nothing rather
    /// than as its source, because content that cannot be read is content whose markup would
    /// otherwise become its text.
    /// </remarks>
    private static string PlainText(string? div)
    {
        if (string.IsNullOrWhiteSpace(div))
        {
            return string.Empty;
        }

        try
        {
            // No resolver, so nothing in a narrative can cause the indexer to fetch anything.
            // Narrative excludes active content and external references by profile; the parser
            // must not be the place that assumption is trusted.
            var document = new XmlDocument { XmlResolver = null };
            document.LoadXml(div);
            return document.DocumentElement?.InnerText ?? string.Empty;
        }
        catch (XmlException)
        {
            return string.Empty;
        }
    }
}
