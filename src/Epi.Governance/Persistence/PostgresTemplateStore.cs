using Epi.Templates;
using Npgsql;

namespace Epi.Governance.Persistence;

/// <summary>
/// The durable template store: one append-only row per template version (ADR-042 decision 2,
/// ADR-043, CAP-TPL-001).
/// </summary>
/// <remarks>
/// <para>
/// The same shape as the lifecycle store and for the same reason: a template version is
/// immutable, so there is no update and no delete, and a change arrives as another row. A render
/// keyed to template version 2 must mean the same thing in five years as it did when it was
/// filed (ADR-033 decision 1), and a row anybody could edit is a render nothing can vouch for.
/// </para>
/// <para>
/// Version numbers are allocated inside the insert rather than read and then written, so two
/// callers versioning the same template concurrently cannot both be told they made version 3.
/// The primary key refuses the loser.
/// </para>
/// </remarks>
public sealed class PostgresTemplateStore(string connectionString)
    : ITemplateStore, IAsyncDisposable
{
    private readonly Lazy<NpgsqlDataSource> _source =
        new(() => new NpgsqlDataSourceBuilder(connectionString).Build());

    public async Task<StoredRenderTemplate> CreateAsync(
        RenderTemplateDefinition definition, CancellationToken cancellationToken = default)
    {
        Check(definition);

        // Version 1 only, and the primary key is what refuses a second one. Falling through to
        // a new version would let a second author replace what a first one registered, quietly.
        try
        {
            return await InsertAsync(definition, 1, cancellationToken);
        }
        catch (PostgresException clash) when (clash.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            throw new TemplateConflictException(
                $"A template '{definition.Identifier}' already exists. A change to one is a "
                + "new version of it, which is a different operation.");
        }
    }

    public async Task<StoredRenderTemplate> CreateVersionAsync(
        string identifier,
        RenderTemplateDefinition definition,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);
        Check(definition);

        var versions = await VersionsAsync(identifier, cancellationToken);

        if (versions.Count == 0)
        {
            throw new TemplateConflictException(
                $"There is no template '{identifier}' to add a version to.");
        }

        try
        {
            return await InsertAsync(
                definition with { Identifier = identifier }, versions[^1] + 1, cancellationToken);
        }
        catch (PostgresException clash) when (clash.SqlState == PostgresErrorCodes.UniqueViolation)
        {
            // Somebody else allocated that number between the read and the write. Reported as a
            // conflict rather than retried: the caller wrote their version against what they had
            // read, and a silent retry would file it on top of a change they have not seen.
            throw new TemplateConflictException(
                $"Version {versions[^1] + 1} of '{identifier}' was created by somebody else. "
                + "Read the template again and version what is now there.");
        }
    }

    public async Task<StoredRenderTemplate?> GetAsync(
        string identifier, int version, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        await using var command = _source.Value.CreateCommand("""
            SELECT identifier, version, name, stylesheet FROM render_template
            WHERE identifier = $1 AND version = $2
            """);

        command.Parameters.AddWithValue(identifier);
        command.Parameters.AddWithValue(version);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        return await reader.ReadAsync(cancellationToken) ? Read(reader) : null;
    }

    public async Task<IReadOnlyList<int>> VersionsAsync(
        string identifier, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(identifier);

        await using var command = _source.Value.CreateCommand("""
            SELECT version FROM render_template WHERE identifier = $1 ORDER BY version
            """);

        command.Parameters.AddWithValue(identifier);

        var versions = new List<int>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            versions.Add(reader.GetInt32(0));
        }

        return versions;
    }

    public async Task<IReadOnlyList<StoredRenderTemplate>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        // The latest version of each, because that is what a chooser needs. DISTINCT ON is
        // PostgreSQL's own and reads more plainly here than a window function would.
        await using var command = _source.Value.CreateCommand("""
            SELECT DISTINCT ON (identifier) identifier, version, name, stylesheet
            FROM render_template
            ORDER BY identifier, version DESC
            """);

        var templates = new List<StoredRenderTemplate>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            templates.Add(Read(reader));
        }

        return templates;
    }

    public ValueTask DisposeAsync() =>
        _source.IsValueCreated ? _source.Value.DisposeAsync() : ValueTask.CompletedTask;

    private async Task<StoredRenderTemplate> InsertAsync(
        RenderTemplateDefinition definition, int version, CancellationToken cancellationToken)
    {
        await using var command = _source.Value.CreateCommand("""
            INSERT INTO render_template (identifier, version, name, stylesheet)
            VALUES ($1, $2, $3, $4)
            """);

        command.Parameters.AddWithValue(definition.Identifier);
        command.Parameters.AddWithValue(version);
        command.Parameters.AddWithValue(definition.Name);
        command.Parameters.AddWithValue(definition.Stylesheet);

        await command.ExecuteNonQueryAsync(cancellationToken);

        return new StoredRenderTemplate(
            definition.Identifier, version, definition.Name, definition.Stylesheet);
    }

    private static StoredRenderTemplate Read(NpgsqlDataReader reader) =>
        new(reader.GetString(0), reader.GetInt32(1), reader.GetString(2), reader.GetString(3));

    private static void Check(RenderTemplateDefinition definition)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentException.ThrowIfNullOrWhiteSpace(definition.Identifier);

        // The same two refusals the in-memory store makes, here rather than delegated, because
        // a NOT NULL column would report an empty name as a constraint violation and an empty
        // name is not a database problem. The conformance suite holds both stores to it.
        if (string.IsNullOrWhiteSpace(definition.Name))
        {
            throw new ArgumentException(
                "A render template must be named. The name is what an approver reads when "
                + "deciding whether to sign for what a patient will read.",
                nameof(definition));
        }

        if (string.IsNullOrWhiteSpace(definition.Stylesheet))
        {
            throw new ArgumentException(
                "A render template with no stylesheet renders a leaflet as unstyled markup.",
                nameof(definition));
        }
    }
}
