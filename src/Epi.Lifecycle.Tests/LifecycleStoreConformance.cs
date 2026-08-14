using Xunit;

namespace Epi.Lifecycle.Tests;

/// <summary>
/// The behaviour every lifecycle store must exhibit, whatever backs it (FN-LCM-003).
/// </summary>
/// <remarks>
/// Shared source, run once against the in-memory store and once against a real PostgreSQL.
/// Two implementations of one contract drift unless the same assertions are run against both,
/// and the assertions here are the ones a state history has to satisfy to be evidence.
/// </remarks>
public abstract class LifecycleStoreConformance : IAsyncDisposable
{
    private static readonly VersionRef Version = new("doc-1", 1);

    private readonly List<ILifecycleStore> _created = [];

    /// <summary>A store ready to use, with its schema in place if it needs one.</summary>
    protected abstract Task<ILifecycleStore> CreateStoreAsync();

    /// <summary>
    /// A store, remembered so it is disposed when the case finishes. A durable store owns a
    /// connection pool; leaving one per case open exhausted the server's connections partway
    /// through the suite, which surfaced as a connection torn down mid-handshake rather than as
    /// anything resembling "too many clients".
    /// </summary>
    private async Task<ILifecycleStore> NewStoreAsync()
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

    private static StateTransition Transition(
        string from, string to, string action, string actor = "user-anna",
        string? reason = null, string? signature = null, VersionRef? version = null) =>
        new(version ?? Version, from, to, action, actor,
            new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero), reason, signature);

    [Fact]
    public async Task FN_LCM_003_a_version_that_was_never_registered_has_no_state_and_no_author()
    {
        // Null rather than a default state. A store that answered "draft" for a version it has
        // never seen would let a transition proceed on something that does not exist.
        var store = await NewStoreAsync();

        Assert.Null(await store.CurrentStateAsync(Version));
        Assert.Null(await store.AuthorOfAsync(Version));
        Assert.Empty(await store.HistoryAsync(Version));
    }

    [Fact]
    public async Task FN_LCM_003_registration_records_the_author_and_the_initial_state()
    {
        var store = await NewStoreAsync();

        await store.RegisterAsync(Version, "user-anna", "draft");

        Assert.Equal("draft", await store.CurrentStateAsync(Version));
        Assert.Equal("user-anna", await store.AuthorOfAsync(Version));
    }

    [Fact]
    public async Task FN_LCM_003_a_version_cannot_be_registered_twice()
    {
        // Re-registration would rewrite the recorded author, and the author is what segregation
        // of duties is checked against - so silently accepting it would let someone become
        // eligible to approve their own work (CAP-IAM-006).
        var store = await NewStoreAsync();
        await store.RegisterAsync(Version, "user-anna", "draft");

        await Assert.ThrowsAnyAsync<Exception>(
            () => store.RegisterAsync(Version, "user-ben", "draft"));

        Assert.Equal("user-anna", await store.AuthorOfAsync(Version));
    }

    [Fact]
    public async Task FN_LCM_003_the_current_state_is_the_destination_of_the_last_transition()
    {
        var store = await NewStoreAsync();
        await store.RegisterAsync(Version, "user-anna", "draft");

        await store.AppendAsync(Transition("draft", "in-review", "submit"));
        await store.AppendAsync(Transition("in-review", "approved", "approve", "user-ben"));

        Assert.Equal("approved", await store.CurrentStateAsync(Version));
    }

    [Fact]
    public async Task FN_LCM_003_history_comes_back_oldest_first_with_every_field_intact()
    {
        // Order is evidence: a trail that cannot say what happened before what cannot support
        // a reconstruction (CAP-LCM-006).
        var store = await NewStoreAsync();
        await store.RegisterAsync(Version, "user-anna", "draft");

        await store.AppendAsync(Transition("draft", "in-review", "submit", reason: "ready"));
        await store.AppendAsync(Transition(
            "in-review", "approved", "approve", "user-ben", "checked", "sig-1"));

        var history = await store.HistoryAsync(Version);

        Assert.Equal(["submit", "approve"], history.Select(t => t.Action));

        var approval = history[1];
        Assert.Equal(Version, approval.Version);
        Assert.Equal("in-review", approval.From);
        Assert.Equal("approved", approval.To);
        Assert.Equal("user-ben", approval.Actor);
        Assert.Equal("checked", approval.Reason);
        Assert.Equal("sig-1", approval.SignatureReference);
        Assert.Equal(new DateTimeOffset(2026, 8, 14, 10, 0, 0, TimeSpan.Zero), approval.At);
    }

    [Fact]
    public async Task FN_LCM_003_history_is_kept_per_version()
    {
        var store = await NewStoreAsync();
        var second = new VersionRef("doc-1", 2);
        await store.RegisterAsync(Version, "user-anna", "draft");
        await store.RegisterAsync(second, "user-anna", "draft");

        await store.AppendAsync(Transition("draft", "in-review", "submit"));
        await store.AppendAsync(Transition("draft", "in-review", "submit", version: second));
        await store.AppendAsync(Transition("in-review", "approved", "approve", "user-ben", version: second));

        Assert.Equal("in-review", await store.CurrentStateAsync(Version));
        Assert.Equal("approved", await store.CurrentStateAsync(second));
        Assert.Single(await store.HistoryAsync(Version));
    }

    [Fact]
    public async Task FN_WFL_003_a_signature_is_spent_once_it_has_been_cited()
    {
        var store = await NewStoreAsync();
        await store.RegisterAsync(Version, "user-anna", "draft");

        Assert.False(await store.IsSignatureUsedAsync("sig-1"));

        await store.AppendAsync(Transition("draft", "approved", "approve", signature: "sig-1"));

        Assert.True(await store.IsSignatureUsedAsync("sig-1"));
        Assert.False(await store.IsSignatureUsedAsync("sig-2"));
    }

    [Fact]
    public async Task FN_WFL_003_an_unsigned_transition_spends_nothing()
    {
        var store = await NewStoreAsync();
        await store.RegisterAsync(Version, "user-anna", "draft");

        await store.AppendAsync(Transition("draft", "in-review", "submit"));

        Assert.False(await store.IsSignatureUsedAsync("sig-1"));
    }
}
