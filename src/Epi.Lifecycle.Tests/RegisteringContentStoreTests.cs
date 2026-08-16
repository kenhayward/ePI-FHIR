using Epi.ContentCore;
using Hl7.Fhir.Model;
using Xunit;

namespace Epi.Lifecycle.Tests;

// FN-LCM-003 Registering a version before its content is written (ADR-025).
//   CAP-LCM-001 Content is under lifecycle management from the moment it exists
//   CAP-IAM-006 The author is recorded, because it is what segregation of duties checks
public sealed class RegisteringContentStoreTests
{
    private static Bundle Document() => new()
    {
        Type = Bundle.BundleType.Document,
        Entry = [new Bundle.EntryComponent { Resource = new Composition { Title = "SYNTHETIC" } }],
    };

    private static LifecycleService Service(ILifecycleStore store) => new(
        new LifecycleModel("label", "draft", ["draft", "in-review"], []), store);

    private static (RegisteringContentStore Store, InMemoryLifecycleStore Lifecycle) Build(
        IContentStore? content = null)
    {
        var lifecycle = new InMemoryLifecycleStore();
        return (new RegisteringContentStore(
            content ?? new InMemoryContentStore(), Service(lifecycle), "user-anna"), lifecycle);
    }

    [Fact]
    public async Task CAP_LCM_001_stored_content_is_registered_without_the_caller_registering_it()
    {
        var (store, lifecycle) = Build();

        var stored = await store.CreateAsync(ContentIdentity.Mint(), Document());

        var reference = new VersionRef(stored.Identity.Value, 1);
        Assert.Equal("draft", await lifecycle.CurrentStateAsync(reference));
        Assert.Equal("user-anna", await lifecycle.AuthorOfAsync(reference));
    }

    [Fact]
    public async Task CAP_LCM_001_a_new_version_is_registered_as_that_version_not_as_version_one()
    {
        var (store, lifecycle) = Build();
        var first = await store.CreateAsync(ContentIdentity.Mint(), Document());

        await store.CreateVersionAsync(first.Identity, 2, Document());

        Assert.Equal("draft", await lifecycle.CurrentStateAsync(new VersionRef(first.Identity.Value, 2)));
    }

    [Fact]
    public async Task CAP_IAM_006_a_write_that_fails_leaves_a_record_with_no_content_rather_than_the_reverse()
    {
        // The whole decision. Ungoverned content is readable through every read path and can
        // never be approved, because the author is what segregation of duties is checked
        // against. A registration for content that was never written refers to nothing:
        // every read is not-found and every transition refuses (ADR-025 decision 2).
        var identity = ContentIdentity.Mint();
        var refusing = new RefusingContentStore();
        var (store, lifecycle) = Build(refusing);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.CreateAsync(identity, Document()));

        Assert.Equal("draft", await lifecycle.CurrentStateAsync(new VersionRef(identity.Value, 1)));
        Assert.Null(await store.GetLatestAsync(identity));
    }

    [Fact]
    public async Task CAP_LCM_001_nothing_is_stored_that_was_not_registered_first()
    {
        // Asserted by order, not by outcome: a decorator that registered afterwards would pass
        // every other case here and fail this one.
        var recorder = new RecordingContentStore();
        var lifecycle = new RecordingLifecycleStore();
        var store = new RegisteringContentStore(recorder, Service(lifecycle), "user-anna");

        await store.CreateAsync(ContentIdentity.Mint(), Document());

        Assert.True(lifecycle.RegisteredAt < recorder.StoredAt);
    }

    [Fact]
    public void CAP_IAM_006_a_store_with_no_author_to_register_against_refuses_to_be_built()
    {
        // A blank author would register content nobody wrote, which is the same hole reached
        // by a different route.
        Assert.Throws<ArgumentException>(() => new RegisteringContentStore(
            new InMemoryContentStore(), Service(new InMemoryLifecycleStore()), "  "));
    }

    private sealed class RefusingContentStore : IContentStore
    {
        public Task<EpiDocument> CreateAsync(
            DocumentIdentity identity, Bundle bundle, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the content store is unavailable");

        public Task<EpiDocument> CreateVersionAsync(
            DocumentIdentity identity, int version, Bundle bundle,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("the content store is unavailable");

        public Task<EpiDocument?> GetAsync(
            DocumentIdentity identity, int version, CancellationToken cancellationToken = default) =>
            Task.FromResult<EpiDocument?>(null);

        public Task<EpiDocument?> GetLatestAsync(
            DocumentIdentity identity, CancellationToken cancellationToken = default) =>
            Task.FromResult<EpiDocument?>(null);

        public Task<IReadOnlyList<int>> VersionsAsync(
            DocumentIdentity identity, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<int>>([]);
    }

    private sealed class RecordingContentStore : IContentStore
    {
        private readonly InMemoryContentStore _inner = new();

        public long StoredAt { get; private set; }

        public Task<EpiDocument> CreateAsync(
            DocumentIdentity identity, Bundle bundle, CancellationToken cancellationToken = default)
        {
            StoredAt = Interlocked.Increment(ref Sequence.Next);
            return _inner.CreateAsync(identity, bundle, cancellationToken);
        }

        public Task<EpiDocument> CreateVersionAsync(
            DocumentIdentity identity, int version, Bundle bundle,
            CancellationToken cancellationToken = default)
        {
            StoredAt = Interlocked.Increment(ref Sequence.Next);
            return _inner.CreateVersionAsync(identity, version, bundle, cancellationToken);
        }

        public Task<EpiDocument?> GetAsync(
            DocumentIdentity identity, int version, CancellationToken cancellationToken = default) =>
            _inner.GetAsync(identity, version, cancellationToken);

        public Task<EpiDocument?> GetLatestAsync(
            DocumentIdentity identity, CancellationToken cancellationToken = default) =>
            _inner.GetLatestAsync(identity, cancellationToken);

        public Task<IReadOnlyList<int>> VersionsAsync(
            DocumentIdentity identity, CancellationToken cancellationToken = default) =>
            _inner.VersionsAsync(identity, cancellationToken);
    }

    private sealed class RecordingLifecycleStore : ILifecycleStore
    {
        private readonly InMemoryLifecycleStore _inner = new();

        public long RegisteredAt { get; private set; }

        public Task RegisterAsync(
            VersionRef version, string author, string initialState, DateTimeOffset registeredAt,
            string kind = RegisteredArtefact.Content, CancellationToken cancellationToken = default)
        {
            RegisteredAt = Interlocked.Increment(ref Sequence.Next);
            return _inner.RegisterAsync(
                version, author, initialState, registeredAt, kind, cancellationToken);
        }

        public Task<string?> AuthorOfAsync(VersionRef version, CancellationToken cancellationToken = default) =>
            _inner.AuthorOfAsync(version, cancellationToken);

        public Task<DateTimeOffset?> RegisteredAtAsync(
            VersionRef version, CancellationToken cancellationToken = default) =>
            _inner.RegisteredAtAsync(version, cancellationToken);

        public Task<IReadOnlyList<Registration>> RegistrationsBeforeAsync(
            DateTimeOffset moment, CancellationToken cancellationToken = default) =>
            _inner.RegistrationsBeforeAsync(moment, cancellationToken);

        public Task<string?> CurrentStateAsync(VersionRef version, CancellationToken cancellationToken = default) =>
            _inner.CurrentStateAsync(version, cancellationToken);

        public Task<IReadOnlyList<StateTransition>> HistoryAsync(
            VersionRef version, CancellationToken cancellationToken = default) =>
            _inner.HistoryAsync(version, cancellationToken);

        public Task AppendAsync(
            StateTransition transition, PinnedContext? pin = null,
            StateTransition? consequence = null,
            CancellationToken cancellationToken = default) =>
            _inner.AppendAsync(transition, pin, consequence, cancellationToken);

        public Task<IReadOnlyList<int>> VersionsInStateAsync(
            string documentIdentifier, string state, CancellationToken cancellationToken = default) =>
            _inner.VersionsInStateAsync(documentIdentifier, state, cancellationToken);

        public Task<bool> IsSignatureUsedAsync(string reference, CancellationToken cancellationToken = default) =>
            _inner.IsSignatureUsedAsync(reference, cancellationToken);
    }

    private static class Sequence
    {
        public static long Next;
    }
}
