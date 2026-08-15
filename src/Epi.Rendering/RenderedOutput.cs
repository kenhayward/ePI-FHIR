namespace Epi.Rendering;

/// <summary>
/// The two lineages of PDF, which are never interchanged (D1 Section 3.3, D3 Section 3.2,
/// ADR-033 decision 6).
/// </summary>
/// <remarks>
/// Separate types rather than a field, so that storing artwork as a render is something that
/// does not compile rather than something that gets reviewed. They have different provenance,
/// different lifecycles and different meanings to a regulator: a rendered document is produced
/// by this platform from FHIR content and is reproducible from it, while artwork is produced
/// externally by an agency and is only ingested and linked.
/// </remarks>
public abstract record LabelDocument(string MediaType, byte[] Content)
{
    /// <summary>The bytes, copied, so an artefact cannot be edited after it is produced.</summary>
    public byte[] Content { get; } = [.. Content];
}

/// <summary>
/// A document this platform produced from a label version and a render template.
/// </summary>
/// <param name="Draft">
/// Whether the version rendered was approved. An author preview indistinguishable from an
/// official render is a document that will eventually be sent to somebody (CAP-RND-004).
/// </param>
public sealed record RenderedDocument(
    string MediaType,
    byte[] Content,
    DocumentIdentityRef Label,
    int LabelVersion,
    string RenderTemplate,
    int RenderTemplateVersion,
    bool Draft = false) : LabelDocument(MediaType, Content);

/// <summary>
/// Artwork produced outside this platform and only ingested and linked.
/// </summary>
/// <remarks>
/// It has no label version and no render template, because nothing here produced it. That is
/// the whole distinction, and it is why this is a different type rather than a flag: there is
/// no honest value to put in those fields.
/// </remarks>
public sealed record ArtworkDocument(
    string MediaType,
    byte[] Content,
    string Source,
    string Reference) : LabelDocument(MediaType, Content);

/// <summary>A label's business identity, as a render records it.</summary>
public sealed record DocumentIdentityRef(string System, string Value);
