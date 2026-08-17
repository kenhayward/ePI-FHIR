using Epi.ContentCore;
using Epi.Lifecycle;
using Epi.Rendering;
using Epi.Templates;

namespace Epi.Publishing;

/// <summary>What producing an official render did (FN-RND-004).</summary>
/// <param name="AlreadyFiled">
/// Whether the artefact was already in the asset store. A render is a pure function of its two
/// versions, so asking again asks for the same bytes: this says which request made them.
/// </param>
public sealed record OfficialRenderOutcome(
    RenderedDocument Document, AssetKey Key, bool AlreadyFiled);

/// <summary>Raised when a rule refuses to produce an official render.</summary>
/// <remarks>
/// A refusal rather than a preview. Quietly downgrading to a draft render would hand back
/// something that looks like the artefact of record and is not, which is the confusion
/// CAP-RND-004 exists to prevent.
/// </remarks>
public sealed class RenderRefusedException(string message) : Exception(message);

/// <summary>
/// Raised when what is filed differs from what the content and template now produce.
/// </summary>
public sealed class RenderMismatchException(AssetKey key)
    : Exception(
        $"The artefact filed at {key} is not what this label version and this template version "
        + "produce. One of them has changed underneath a copy somebody already has, and "
        + "answering with either version silently would hide that.")
{
    public AssetKey Key { get; } = key;
}

/// <summary>
/// Produces the artefact of record: a render of an approved label version, made with an approved
/// template, filed where it can be cited (ADR-033 decision 2, ADR-046).
/// </summary>
/// <remarks>
/// <para>
/// Two approvals, not one. The content has to be approved because an official render of a draft
/// is a document somebody will eventually send; the template has to be approved because a
/// template determines what a patient reads (ADR-042). Either one missing makes the artefact a
/// preview, whatever else is true of it - and a preview is what
/// <c>/labels/{id}/versions/{v}/preview</c> is for.
/// </para>
/// <para>
/// This is deliberately not in <c>Epi.Rendering</c>, which reads content and nothing else.
/// Deciding whether a render may be produced is a different question from producing one, and
/// putting lifecycle state inside the renderer would spend the purity that makes a render
/// reproducible.
/// </para>
/// </remarks>
public sealed class OfficialRender(
    IContentStore content,
    ILifecycleStore lifecycle,
    ITemplateStore templates,
    IAssetStore assets,
    string labelApprovedState,
    string templateApprovedState)
{
    /// <summary>
    /// Renders and files, or returns what was filed before. Null if there is no such version.
    /// </summary>
    /// <exception cref="RenderRefusedException">
    /// If the version or the template is not approved, or the template does not exist.
    /// </exception>
    /// <exception cref="RenderMismatchException">
    /// If what is filed is not what this content and this template produce.
    /// </exception>
    public async Task<OfficialRenderOutcome?> ProduceAsync(
        DocumentIdentity label,
        int version,
        string templateIdentifier,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(templateIdentifier);

        var document = await content.GetAsync(label, version, cancellationToken);

        if (document is null)
        {
            return null;
        }

        var reference = new VersionRef(label.Value, version);
        var state = await lifecycle.CurrentStateAsync(reference, cancellationToken);

        if (!string.Equals(state, labelApprovedState, StringComparison.Ordinal))
        {
            throw new RenderRefusedException(
                $"Version {version} is {state ?? "not registered"}, and only an "
                + $"{labelApprovedState} version has an official render. Use the preview to look "
                + "at content that is still being worked on.");
        }

        var template = await ApprovedTemplateAsync(templateIdentifier, cancellationToken);
        var rendered = HtmlRenderer.Render(
            document,
            new RenderTemplate(
                template.Identifier, template.Version, template.Name, template.Stylesheet));
        var key = AssetKey.For(rendered);

        if (await assets.GetAsync(key, cancellationToken) is { } filed)
        {
            // Byte-compared rather than assumed. "Reproducible" is a claim about these two
            // things being equal, and the only place it can be checked is here.
            if (!filed.Content.AsSpan().SequenceEqual(rendered.Content))
            {
                throw new RenderMismatchException(key);
            }

            return new OfficialRenderOutcome(rendered, key, AlreadyFiled: true);
        }

        try
        {
            await assets.PutAsync(key, rendered, cancellationToken);
        }
        catch (AssetAlreadyStoredException)
        {
            // Somebody filed it between the read and the write. Their bytes are this render's
            // bytes, because a render is a pure function of its two versions - so this is a
            // race with no loser rather than a conflict to report.
            return new OfficialRenderOutcome(rendered, key, AlreadyFiled: true);
        }

        return new OfficialRenderOutcome(rendered, key, AlreadyFiled: false);
    }

    /// <summary>What has been filed for one label version.</summary>
    /// <remarks>
    /// So a surface can offer what exists rather than asking for it to be made again, and so an
    /// inspector can see what a version produced.
    /// </remarks>
    public async Task<IReadOnlyList<FiledRender>> FiledAsync(
        DocumentIdentity label, int version, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(label);

        var prefix = $"{label.Value}/{version}/";
        var keys = await assets.ListAsync(AssetKey.RenderedLineage, cancellationToken);

        return
        [
            .. keys
                .Where(key => key.Path.StartsWith(prefix, StringComparison.Ordinal))
                .Select(key => Filed(key, prefix))
                .Where(filed => filed is not null)
                .Select(filed => filed!)
                .OrderBy(filed => filed.RenderTemplate, StringComparer.Ordinal)
                .ThenByDescending(filed => filed.RenderTemplateVersion),
        ];
    }

    private async Task<StoredRenderTemplate> ApprovedTemplateAsync(
        string identifier, CancellationToken cancellationToken)
    {
        var versions = await templates.VersionsAsync(identifier, cancellationToken);

        if (versions.Count == 0)
        {
            throw new RenderRefusedException(
                $"There is no template '{identifier}'. Templates come from the platform rather "
                + "than from whoever is asking for a render.");
        }

        // The newest approved version, not the newest version. A template revised after an
        // approval has a draft at the top, and rendering with that would be rendering with
        // something nobody signed for.
        foreach (var version in versions.OrderByDescending(v => v))
        {
            var state = await lifecycle.CurrentStateAsync(
                new VersionRef(identifier, version), cancellationToken);

            if (string.Equals(state, templateApprovedState, StringComparison.Ordinal))
            {
                return await templates.GetAsync(identifier, version, cancellationToken)
                       ?? throw new RenderRefusedException(
                           $"Template '{identifier}' version {version} is {templateApprovedState} "
                           + "and is not in the template store.");
            }
        }

        throw new RenderRefusedException(
            $"No version of template '{identifier}' is {templateApprovedState}. A template "
            + "determines what a patient reads, so a render made with one nobody signed for is "
            + "not the artefact filed with a regulator (ADR-042 decision 4).");
    }

    /// <summary>Reads a filed render's identity back out of its key.</summary>
    /// <remarks>
    /// The key is the record. It is written from the two versions that made the artefact
    /// (<see cref="AssetKey.For(RenderedDocument)"/>), so reading it back is reading what was
    /// filed rather than consulting a second index that could disagree with the bucket.
    /// </remarks>
    private static FiledRender? Filed(AssetKey key, string prefix)
    {
        var rest = key.Path[prefix.Length..].Split('/');

        return rest.Length == 3 && int.TryParse(rest[1], out var templateVersion)
            ? new FiledRender(rest[0], templateVersion, key, rest[2].StartsWith("draft", StringComparison.Ordinal))
            : null;
    }
}

/// <summary>An artefact in the asset store, as its key describes it.</summary>
public sealed record FiledRender(
    string RenderTemplate, int RenderTemplateVersion, AssetKey Key, bool Draft);
