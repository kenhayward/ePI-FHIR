using Xunit;

namespace Epi.Lifecycle.Tests;

/// <summary>
/// The behaviour every store of pinned validating contexts must exhibit (FN-LCM-005).
/// </summary>
/// <remarks>
/// The store under test is the lifecycle store, because a pin is written by the same append
/// that records the transition it belongs to and there is no other write path to one
/// (ADR-024 decision 2). Shared source, run once against the in-memory store and once against
/// a real PostgreSQL: the atomicity these cases assert is the whole reason the decision exists,
/// and only a real database can fail them.
/// </remarks>
public abstract class PinnedContextStoreConformance : IAsyncDisposable
{
    private static readonly VersionRef Version = new("doc-1", 1);

    private static readonly DateTimeOffset Registered =
        new(2026, 8, 14, 9, 0, 0, TimeSpan.Zero);

    private readonly List<ILifecycleStore> _created = [];

    /// <summary>A store ready to use, which both records transitions and reads back pins.</summary>
    protected abstract Task<ILifecycleStore> CreateStoreAsync();

    private async Task<(ILifecycleStore Store, IPinnedContextStore Pins)> NewStoreAsync()
    {
        var store = await CreateStoreAsync();
        _created.Add(store);

        // The same object behind both ports, because they are one operational store. A store
        // that could not satisfy this could not make the write atomic either.
        return (store, Assert.IsAssignableFrom<IPinnedContextStore>(store));
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

    private static StateTransition Approval(VersionRef? version = null) => new(
        version ?? Version, "in-review", "approved", "approve", "user-ben",
        new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero), Reason: null,
        SignatureReference: "sig-1");

    [Fact]
    public async Task FN_LCM_005_a_version_with_no_pin_has_no_context()
    {
        // Null rather than an empty context. Something that looks like a record of what a
        // version was approved against, for a version nobody approved, is worse than nothing.
        var (_, pins) = await NewStoreAsync();

        Assert.Null(await pins.ForAsync(Version));
    }

    [Fact]
    public async Task FN_LCM_005_a_pinned_context_comes_back_as_it_was_pinned()
    {
        var (store, pins) = await NewStoreAsync();
        await store.RegisterAsync(Version, "user-anna", "draft", Registered);
        await store.AppendAsync(Approval(), Context());

        var pinned = await pins.ForAsync(Version);

        Assert.NotNull(pinned);
        Assert.Equal("sha-256:abc123", pinned!.ContentHash);
        Assert.Equal("label", pinned.StateModel);
        Assert.Equal("approved", pinned.State);
        Assert.Equal("smpc-gb", pinned.Template);
        Assert.Equal(3, pinned.TemplateVersion);
        Assert.Equal("https://epi.example.org/identifier/document", pinned.IdentifierAuthority);
        Assert.Equal(new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero), pinned.PinnedAt);
    }

    [Fact]
    public async Task CAP_LCM_011_the_packages_survive_with_their_versions_and_digests()
    {
        // The digest is the whole point: a name and a version say which package was meant, and
        // only the digest says which bytes were used (ADR-023 decision 2).
        var (store, pins) = await NewStoreAsync();
        await store.RegisterAsync(Version, "user-anna", "draft", Registered);
        await store.AppendAsync(Approval(), Context());

        var packages = (await pins.ForAsync(Version))!.Packages;

        Assert.Equal(2, packages.Count);
        var ig = Assert.Single(packages, p => p.Name == "hl7.fhir.uv.emedicinal-product-info");
        Assert.Equal("1.0.0", ig.Version);
        Assert.Equal("c99767", ig.Sha256);
    }

    [Fact]
    public async Task CAP_LCM_011_a_version_that_came_from_no_template_pins_none()
    {
        var (store, pins) = await NewStoreAsync();
        await store.RegisterAsync(Version, "user-anna", "draft", Registered);
        await store.AppendAsync(Approval(), Context(template: null));

        var pinned = await pins.ForAsync(Version);

        Assert.Null(pinned!.Template);
        Assert.Null(pinned.TemplateVersion);
    }

    [Fact]
    public async Task CAP_LCM_011_a_transition_without_a_context_pins_nothing()
    {
        // Most transitions pin nothing. A submit is not an approval, and a store that wrote an
        // empty pin for one would put a record of commitment against a draft.
        var (store, pins) = await NewStoreAsync();
        await store.RegisterAsync(Version, "user-anna", "draft", Registered);

        await store.AppendAsync(new StateTransition(
            Version, "draft", "in-review", "submit", "user-anna",
            new DateTimeOffset(2026, 8, 14, 9, 30, 0, TimeSpan.Zero)));

        Assert.Null(await pins.ForAsync(Version));
        Assert.Single(await store.HistoryAsync(Version));
    }

    [Fact]
    public async Task CAP_LCM_011_a_version_cannot_be_pinned_twice()
    {
        // A record that can be replaced is not a record, and an approval happens once. The
        // store refuses rather than overwriting, because overwriting is silent.
        var (store, pins) = await NewStoreAsync();
        await store.RegisterAsync(Version, "user-anna", "draft", Registered);
        await store.AppendAsync(Approval(), Context());

        await Assert.ThrowsAsync<ContextAlreadyPinnedException>(
            () => store.AppendAsync(Approval(), Context(hash: "sha-256:different")));

        Assert.Equal("sha-256:abc123", (await pins.ForAsync(Version))!.ContentHash);
    }

    [Fact]
    public async Task CAP_LCM_011_a_refused_pin_takes_its_transition_with_it()
    {
        // The point of the whole decision. If the pin fails and the transition lands anyway,
        // the result is an approved version with no record of what it was approved against -
        // which is the failure ADR-024 exists to prevent, arriving by the back door.
        var (store, pins) = await NewStoreAsync();
        await store.RegisterAsync(Version, "user-anna", "draft", Registered);
        await store.AppendAsync(Approval(), Context());

        var before = (await store.HistoryAsync(Version)).Count;

        await Assert.ThrowsAsync<ContextAlreadyPinnedException>(
            () => store.AppendAsync(Approval(), Context(hash: "sha-256:different")));

        Assert.Equal(before, (await store.HistoryAsync(Version)).Count);
        Assert.Equal("approved", await store.CurrentStateAsync(Version));
        Assert.Equal("sha-256:abc123", (await pins.ForAsync(Version))!.ContentHash);
    }

    [Fact]
    public async Task FN_LCM_005_pins_are_held_per_version_not_per_document()
    {
        // Two versions of one label are approved against whatever was in force when each was
        // approved, which may be two different implementation guides.
        var second = new VersionRef("doc-1", 2);
        var (store, pins) = await NewStoreAsync();
        await store.RegisterAsync(Version, "user-anna", "draft", Registered);
        await store.RegisterAsync(second, "user-anna", "draft", Registered);

        await store.AppendAsync(Approval(), Context(hash: "sha-256:first"));
        await store.AppendAsync(Approval(second), Context(second, "sha-256:second"));

        Assert.Equal("sha-256:first", (await pins.ForAsync(Version))!.ContentHash);
        Assert.Equal("sha-256:second", (await pins.ForAsync(second))!.ContentHash);
    }
}
