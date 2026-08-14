using Xunit;

namespace Epi.Lifecycle.Tests;

/// <summary>
/// The behaviour every pinned-context store must exhibit, whatever backs it (FN-LCM-005).
/// </summary>
/// <remarks>
/// Shared source, run once against the in-memory store and once against a real PostgreSQL, for
/// the same reason the other store suites are: the assertions here are what a record of "what
/// this was approved against" has to satisfy to be evidence, and a durable store can fail them
/// in ways an in-memory one never will.
/// </remarks>
public abstract class PinnedContextStoreConformance : IAsyncDisposable
{
    private static readonly VersionRef Version = new("doc-1", 1);

    private readonly List<IPinnedContextStore> _created = [];

    protected abstract Task<IPinnedContextStore> CreateStoreAsync();

    private async Task<IPinnedContextStore> NewStoreAsync()
    {
        var store = await CreateStoreAsync();
        _created.Add(store);
        return store;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var store in _created)
        {
            if (store is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
        }

        GC.SuppressFinalize(this);
    }

    private static PinnedContext Context(
        VersionRef? version = null, string hash = "sha-256:abc123", string? template = "smpc-gb") =>
        new(version ?? Version,
            hash,
            "label",
            "approved",
            [
                new PinnedPackage("hl7.fhir.uv.emedicinal-product-info", "1.0.0", "c99767"),
                new PinnedPackage("hl7.terminology.r5", "5.0.0", "071645"),
            ],
            "https://epi.example.org/identifier/document",
            new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero),
            template,
            template is null ? null : 3);

    [Fact]
    public async Task FN_LCM_005_a_version_with_no_pin_has_no_context()
    {
        // Null rather than an empty context. Something that looks like a record of what a
        // version was approved against, for a version nobody approved, is worse than nothing.
        var store = await NewStoreAsync();

        Assert.Null(await store.ForAsync(Version));
    }

    [Fact]
    public async Task FN_LCM_005_a_pinned_context_comes_back_as_it_was_pinned()
    {
        var store = await NewStoreAsync();
        await store.PinAsync(Context());

        var pinned = await store.ForAsync(Version);

        Assert.NotNull(pinned);
        Assert.Equal("sha-256:abc123", pinned!.ContentHash);
        Assert.Equal("label", pinned.StateModel);
        Assert.Equal("approved", pinned.State);
        Assert.Equal("smpc-gb", pinned.Template);
        Assert.Equal(3, pinned.TemplateVersion);
        Assert.Equal("https://epi.example.org/identifier/document", pinned.IdentifierAuthority);
        Assert.Equal(
            new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero), pinned.PinnedAt);
    }

    [Fact]
    public async Task CAP_LCM_011_the_packages_survive_with_their_versions_and_digests()
    {
        // The digest is the whole point: a name and a version say which package was meant, and
        // only the digest says which bytes were used (ADR-023 decision 2).
        var store = await NewStoreAsync();
        await store.PinAsync(Context());

        var packages = (await store.ForAsync(Version))!.Packages;

        Assert.Equal(2, packages.Count);
        var ig = Assert.Single(packages, p => p.Name == "hl7.fhir.uv.emedicinal-product-info");
        Assert.Equal("1.0.0", ig.Version);
        Assert.Equal("c99767", ig.Sha256);
    }

    [Fact]
    public async Task CAP_LCM_011_a_version_that_came_from_no_template_pins_none()
    {
        var store = await NewStoreAsync();
        await store.PinAsync(Context(template: null));

        var pinned = await store.ForAsync(Version);

        Assert.Null(pinned!.Template);
        Assert.Null(pinned.TemplateVersion);
    }

    [Fact]
    public async Task CAP_LCM_011_a_version_cannot_be_pinned_twice()
    {
        // A record that can be replaced is not a record, and an approval happens once. The
        // store refuses rather than overwriting, because overwriting is silent.
        var store = await NewStoreAsync();
        await store.PinAsync(Context());

        await Assert.ThrowsAsync<ContextAlreadyPinnedException>(
            () => store.PinAsync(Context(hash: "sha-256:different")));

        Assert.Equal("sha-256:abc123", (await store.ForAsync(Version))!.ContentHash);
    }

    [Fact]
    public async Task FN_LCM_005_pins_are_held_per_version_not_per_document()
    {
        // Two versions of one label are approved against whatever was in force when each was
        // approved, which may be two different implementation guides.
        var store = await NewStoreAsync();
        await store.PinAsync(Context(hash: "sha-256:first"));
        await store.PinAsync(Context(new VersionRef("doc-1", 2), "sha-256:second"));

        Assert.Equal("sha-256:first", (await store.ForAsync(Version))!.ContentHash);
        Assert.Equal("sha-256:second", (await store.ForAsync(new VersionRef("doc-1", 2)))!.ContentHash);
    }
}
