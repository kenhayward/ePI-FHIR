namespace Epi.Templates;

/// <summary>
/// What a render template says, before it has a version (ADR-042).
/// </summary>
/// <param name="Name">
/// What an approver reads when deciding whether to sign for it. A template with no name is one
/// somebody would approve without knowing what they were approving.
/// </param>
public sealed record RenderTemplateDefinition(string Identifier, string Name, string Stylesheet);

/// <summary>
/// A render template as stored: a definition, and the version that makes it referable (ADR-042
/// decision 2).
/// </summary>
/// <remarks>
/// Immutable, like a content version and for the same reason: a render keyed to template
/// version 2 must mean the same thing in five years as it did when it was filed (ADR-033
/// decision 1).
/// </remarks>
public sealed record StoredRenderTemplate(
    string Identifier, int Version, string Name, string Stylesheet);

/// <summary>
/// Where render templates live (ADR-042, FN-TPL-003).
/// </summary>
/// <remarks>
/// <para>
/// Their own store, not the FHIR content core: a render template is a stylesheet, there is no
/// FHIR resource that means one, and putting it there would mean inventing one and asserting it
/// is ePI content (ADR-042 decision 1).
/// </para>
/// <para>
/// There is deliberately no update and no delete. A change is a new version, and a template
/// version that could be edited is one a filed render can no longer be trusted to describe.
/// </para>
/// <para>
/// Nothing here decides whether a template may be used. That is its lifecycle state, held by the
/// same engine labels use (ADR-042 decision 3), and only an approved version may produce an
/// official render.
/// </para>
/// </remarks>
public interface ITemplateStore
{
    /// <summary>Creates the first version of a template.</summary>
    /// <exception cref="TemplateConflictException">
    /// If one of that identifier exists. Silently becoming a new version would let a second
    /// author replace what a first one registered.
    /// </exception>
    Task<StoredRenderTemplate> CreateAsync(
        RenderTemplateDefinition definition, CancellationToken cancellationToken = default);

    /// <summary>Adds a version to a template that exists.</summary>
    /// <exception cref="TemplateConflictException">
    /// If it does not. A new version of nothing is a template created by a route that skips
    /// whatever creating one is supposed to do.
    /// </exception>
    Task<StoredRenderTemplate> CreateVersionAsync(
        string identifier,
        RenderTemplateDefinition definition,
        CancellationToken cancellationToken = default);

    /// <summary>One version of one template, or null.</summary>
    Task<StoredRenderTemplate?> GetAsync(
        string identifier, int version, CancellationToken cancellationToken = default);

    /// <summary>Every version of a template, ascending.</summary>
    Task<IReadOnlyList<int>> VersionsAsync(
        string identifier, CancellationToken cancellationToken = default);

    /// <summary>The latest version of every template, so one can be chosen rather than typed.</summary>
    Task<IReadOnlyList<StoredRenderTemplate>> ListAsync(
        CancellationToken cancellationToken = default);
}

/// <summary>Raised when a template is created twice, or versioned without existing.</summary>
public sealed class TemplateConflictException(string message) : Exception(message);

/// <summary>
/// An in-memory template store: the reference implementation the conformance suite holds every
/// implementation to.
/// </summary>
public sealed class InMemoryTemplateStore : ITemplateStore
{
    private readonly Dictionary<string, List<StoredRenderTemplate>> _templates = [];
    private readonly Lock _gate = new();

    public Task<StoredRenderTemplate> CreateAsync(
        RenderTemplateDefinition definition, CancellationToken cancellationToken = default)
    {
        Check(definition);

        lock (_gate)
        {
            if (_templates.ContainsKey(definition.Identifier))
            {
                throw new TemplateConflictException(
                    $"A template '{definition.Identifier}' already exists. A change to one is a "
                    + "new version of it, which is a different operation.");
            }

            return Task.FromResult(Append(definition, 1));
        }
    }

    public Task<StoredRenderTemplate> CreateVersionAsync(
        string identifier,
        RenderTemplateDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        Check(definition);

        lock (_gate)
        {
            if (!_templates.TryGetValue(identifier, out var versions))
            {
                throw new TemplateConflictException(
                    $"There is no template '{identifier}' to add a version to.");
            }

            return Task.FromResult(
                Append(definition with { Identifier = identifier }, versions[^1].Version + 1));
        }
    }

    public Task<StoredRenderTemplate?> GetAsync(
        string identifier, int version, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        lock (_gate)
        {
            return Task.FromResult(
                _templates.TryGetValue(identifier, out var versions)
                    ? versions.FirstOrDefault(t => t.Version == version)
                    : null);
        }
    }

    public Task<IReadOnlyList<int>> VersionsAsync(
        string identifier, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<int>>(
                _templates.TryGetValue(identifier, out var versions)
                    ? [.. versions.Select(t => t.Version).Order()]
                    : []);
        }
    }

    public Task<IReadOnlyList<StoredRenderTemplate>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        lock (_gate)
        {
            return Task.FromResult<IReadOnlyList<StoredRenderTemplate>>(
                [.. _templates.Values.Select(versions => versions[^1])]);
        }
    }

    private StoredRenderTemplate Append(RenderTemplateDefinition definition, int version)
    {
        var stored = new StoredRenderTemplate(
            definition.Identifier, version, definition.Name, definition.Stylesheet);

        if (!_templates.TryGetValue(definition.Identifier, out var versions))
        {
            versions = [];
            _templates[definition.Identifier] = versions;
        }

        versions.Add(stored);
        return stored;
    }

    private static void Check(RenderTemplateDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Identifier);

        // The name is what an approver reads when deciding whether to sign for it.
        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new ArgumentException(
                "A render template must be named. The name is what an approver reads when "
                + "deciding whether to sign for what a patient will read.",
                nameof(definition));
        }

        // A template that styles nothing produces a leaflet that looks like unstyled markup, and
        // somebody would have approved it without seeing that.
        if (string.IsNullOrWhiteSpace(definition.Stylesheet))
        {
            throw new ArgumentException(
                "A render template with no stylesheet renders a leaflet as unstyled markup.",
                nameof(definition));
        }
    }
}
