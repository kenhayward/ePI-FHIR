using System.Text.RegularExpressions;
using Hl7.Fhir.Model;

namespace Epi.ContentCore;

/// <summary>
/// A reference from one passage of narrative to a section (ADR-028).
/// </summary>
/// <param name="Document">
/// The document the target is in, or null when it is this one. A cross-document reference names
/// a version too, because an unversioned one points at whatever that document says today.
/// </param>
public sealed record CrossReference(
    string SourceSectionIdentifier,
    string TargetSectionIdentifier,
    string? Document = null,
    int? Version = null)
{
    /// <summary>Whether the target is inside the document that carries the reference.</summary>
    public bool IsInternal => Document is null;
}

/// <summary>
/// Reading and checking the cross-references a document carries (CAP-SCM-005, ADR-028).
/// </summary>
/// <remarks>
/// Held as anchors in the narrative rather than on the section, because a cross-reference is a
/// phrase inside a sentence and there may be four in one paragraph pointing at four places. A
/// reference held on the section could not say which words it belongs to.
/// </remarks>
public static partial class CrossReferences
{
    /// <summary>
    /// A cross-document target: the document identifier, the version, and the section.
    /// </summary>
    private const string DocumentAnchorPrefix = "epi:";

    [GeneratedRegex("""href\s*=\s*["']([^"']+)["']""", RegexOptions.IgnoreCase)]
    private static partial Regex Anchor();

    /// <summary>Every cross-reference in the document, in document order.</summary>
    public static IReadOnlyList<CrossReference> In(Bundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        var found = new List<CrossReference>();
        foreach (var section in Flatten(Anchors(bundle)))
        {
            // The source identifier is metadata about the reference, not a condition for
            // finding one: section identity is assigned by the store, so at the write gate the
            // sections have none yet. Requiring it here made the integrity check silently find
            // nothing in exactly the normal case - content whose ids have not been assigned.
            var source = section.ElementId ?? string.Empty;
            if (section.Text?.Div is not { } div)
            {
                continue;
            }

            foreach (Match match in Anchor().Matches(div))
            {
                if (Parse(source, match.Groups[1].Value) is { } reference)
                {
                    found.Add(reference);
                }
            }
        }

        return found;
    }

    /// <summary>
    /// The section an internal reference points at, or null if the document has no such section.
    /// </summary>
    /// <remarks>
    /// Resolved within the bundle that carries the reference - not against the latest version of
    /// the document, and not against the document as a concept. A cross-reference cannot rot,
    /// because the bytes it points into cannot change (ADR-028 decision 2).
    /// </remarks>
    public static Composition.SectionComponent? Resolve(Bundle bundle, CrossReference reference)
    {
        ArgumentNullException.ThrowIfNull(bundle);
        ArgumentNullException.ThrowIfNull(reference);

        return reference.IsInternal
            ? Flatten(Anchors(bundle)).FirstOrDefault(
                s => string.Equals(s.ElementId, reference.TargetSectionIdentifier, StringComparison.Ordinal))
            : null;
    }

    /// <summary>
    /// Every internal reference that points at a section this document does not have.
    /// </summary>
    /// <remarks>
    /// Cross-document references are deliberately not checked here: the target is another
    /// aggregate, possibly not yet written and possibly outside the caller's scope, so a write
    /// would fail because of something entirely outside it - and the caller could not be told
    /// which target failed without disclosing whether it exists (ADR-028 decision 4).
    /// </remarks>
    public static IReadOnlyList<CrossReference> Dangling(Bundle bundle)
    {
        ArgumentNullException.ThrowIfNull(bundle);

        return
        [
            .. In(bundle)
                .Where(reference => reference.IsInternal && Resolve(bundle, reference) is null),
        ];
    }

    private static CrossReference? Parse(string source, string href)
    {
        if (href.StartsWith('#'))
        {
            var target = href[1..];
            return string.IsNullOrWhiteSpace(target) ? null : new CrossReference(source, target);
        }

        if (!href.StartsWith(DocumentAnchorPrefix, StringComparison.Ordinal))
        {
            // An ordinary link out to the web is not a cross-reference and is not the platform's
            // to interpret. Whether narrative may contain one at all is the profile's business.
            return null;
        }

        // epi:{document}/{version}#{section}
        var rest = href[DocumentAnchorPrefix.Length..].Split('#');
        var path = rest[0].Split('/');
        return rest.Length == 2 && path.Length == 2 && int.TryParse(path[1], out var version) && version > 0
               && !string.IsNullOrWhiteSpace(path[0]) && !string.IsNullOrWhiteSpace(rest[1])
            ? new CrossReference(source, rest[1], path[0], version)
            : null;
    }

    private static IEnumerable<Composition.SectionComponent> Anchors(Bundle bundle) =>
        bundle.Entry.Count > 0 && bundle.Entry[0].Resource is Composition composition
            ? composition.Section
            : [];

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
}

/// <summary>
/// Refuses content whose internal cross-references point at sections it does not have
/// (ADR-028 decision 3).
/// </summary>
/// <remarks>
/// A decorator on the write path, inside nothing and outside the store, so every route that
/// stores content is checked. A label pointing at a section it does not have is a label with a
/// broken instruction in it, and the write gate is the last place that is cheap to catch.
/// </remarks>
public sealed class CrossReferenceCheckingContentStore(IContentStore inner) : IContentStore
{
    private readonly IContentStore _inner = inner ?? throw new ArgumentNullException(nameof(inner));

    public Task<EpiDocument> CreateAsync(
        DocumentIdentity identity, Bundle bundle, CancellationToken cancellationToken = default) =>
        _inner.CreateAsync(identity, Checked(bundle), cancellationToken);

    public Task<EpiDocument> CreateVersionAsync(
        DocumentIdentity identity, int version, Bundle bundle,
        CancellationToken cancellationToken = default) =>
        _inner.CreateVersionAsync(identity, version, Checked(bundle), cancellationToken);

    public Task<EpiDocument?> GetAsync(
        DocumentIdentity identity, int version, CancellationToken cancellationToken = default) =>
        _inner.GetAsync(identity, version, cancellationToken);

    public Task<EpiDocument?> GetLatestAsync(
        DocumentIdentity identity, CancellationToken cancellationToken = default) =>
        _inner.GetLatestAsync(identity, cancellationToken);

    public Task<IReadOnlyList<int>> VersionsAsync(
        DocumentIdentity identity, CancellationToken cancellationToken = default) =>
        _inner.VersionsAsync(identity, cancellationToken);

    private static Bundle Checked(Bundle bundle)
    {
        var dangling = CrossReferences.Dangling(bundle);
        return dangling.Count == 0
            ? bundle
            : throw new InvalidEpiBundleException(
            [
                .. dangling.Select(reference =>
                    $"A cross-reference points at section '{reference.TargetSectionIdentifier}', "
                    + "which this document does not have (CAP-SCM-005)."),
            ]);
    }
}
