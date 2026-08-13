using Epi.ContentCore;
using Hl7.Fhir.Model;
using Xunit;

namespace Epi.Iam.Tests;

// FN-IAM-004 Apply affiliate and market scope filtering at data access.
// Decisions come from a stub here; the real policy is exercised in the OPA container tests.
// What is under test is what the platform does with a decision, on every path.
public sealed class ScopedContentStoreTests
{
    private static readonly Subject Anna =
        new("user-anna", ["affiliate_author"], ["uk-affiliate"], ["GB"]);

    private static Bundle UkDocument() => ContentScope.Stamp(
        MinimalDocument(), new DocumentScope("uk-affiliate", "GB"));

    private static Bundle MinimalDocument() => new()
    {
        Type = Bundle.BundleType.Document,
        Entry = [new Bundle.EntryComponent
        {
            FullUrl = "urn:uuid:0195f3a0-0000-7000-8000-000000000001",
            Resource = new Composition { Title = "SYNTHETIC TEST LABEL" },
        }],
    };

    private static (ScopedContentStore Scoped, InMemoryContentStore Inner) Build(bool allow) =>
        Build(new StubPolicy(allow));

    private static (ScopedContentStore Scoped, InMemoryContentStore Inner) Build(IPolicyDecisionPoint policy)
    {
        var inner = new InMemoryContentStore();
        return (new ScopedContentStore(inner, policy, Anna), inner);
    }

    [Fact]
    public async Task FN_IAM_004_content_in_scope_can_be_written_and_read()
    {
        var (scoped, _) = Build(allow: true);

        var stored = await scoped.CreateAsync(UkDocument());

        Assert.NotNull(await scoped.GetAsync(stored.Identity, 1));
        Assert.Equal([1], await scoped.VersionsAsync(stored.Identity));
    }

    [Fact]
    public async Task FN_IAM_004_a_denied_write_never_reaches_the_store()
    {
        var (scoped, inner) = Build(allow: false);

        await Assert.ThrowsAsync<AccessDeniedException>(() => scoped.CreateAsync(UkDocument()));

        Assert.Empty(inner.KnownIdentities);
    }

    [Fact]
    public async Task FN_IAM_004_out_of_scope_content_is_invisible_rather_than_refused()
    {
        // CAP-SCH-004: results never leak content outside the caller's scope. A refusal would
        // confirm the document exists, which is itself a leak.
        var inner = new InMemoryContentStore();
        var stored = await inner.CreateAsync(UkDocument());
        var scoped = new ScopedContentStore(inner, new StubPolicy(allow: false), Anna);

        Assert.Null(await scoped.GetAsync(stored.Identity, 1));
        Assert.Null(await scoped.GetLatestAsync(stored.Identity));
        Assert.Empty(await scoped.VersionsAsync(stored.Identity));
    }

    [Fact]
    public async Task FN_IAM_004_content_carrying_no_scope_cannot_be_authorised()
    {
        // Unscoped content would otherwise be reachable by everyone, which is the opposite of
        // multi-tenant isolation (CAP-IAM-007).
        var (scoped, _) = Build(allow: true);

        var denied = await Assert.ThrowsAsync<AccessDeniedException>(
            () => scoped.CreateAsync(MinimalDocument()));

        Assert.Contains("scope", denied.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task FN_IAM_004_the_decision_carries_the_documents_own_affiliate_and_market()
    {
        // The resource attributes must come from the content, not from the request: a caller
        // supplying them could claim any scope it liked.
        var recorder = new RecordingPolicy();
        var (scoped, _) = Build(recorder);

        await scoped.CreateAsync(UkDocument());

        Assert.Equal("uk-affiliate", recorder.Last!.Resource.Affiliate);
        Assert.Equal("GB", recorder.Last.Resource.Market);
        Assert.Equal("author", recorder.Last.Action);
    }

    [Fact]
    public async Task FN_IAM_004_reads_are_authorised_as_reads_not_as_writes()
    {
        var recorder = new RecordingPolicy();
        var inner = new InMemoryContentStore();
        var stored = await inner.CreateAsync(UkDocument());
        var scoped = new ScopedContentStore(inner, recorder, Anna);

        await scoped.GetAsync(stored.Identity, 1);

        Assert.Equal("read", recorder.Last!.Action);
    }

    private sealed class StubPolicy(bool allow) : IPolicyDecisionPoint
    {
        public Task<AuthorizationDecision> DecideAsync(
            AuthorizationQuery query, CancellationToken cancellationToken = default) =>
            Task.FromResult(allow
                ? new AuthorizationDecision(true, "stub")
                : AuthorizationDecision.Deny("stub"));
    }

    private sealed class RecordingPolicy : IPolicyDecisionPoint
    {
        public AuthorizationQuery? Last { get; private set; }

        public Task<AuthorizationDecision> DecideAsync(
            AuthorizationQuery query, CancellationToken cancellationToken = default)
        {
            Last = query;
            return Task.FromResult(new AuthorizationDecision(true, "recording"));
        }
    }
}
