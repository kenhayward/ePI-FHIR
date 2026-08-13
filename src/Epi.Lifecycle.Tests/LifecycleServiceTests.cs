using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Epi.Lifecycle.Tests;

// FN-LCM-002 Reject a transition the state model does not permit
// FN-LCM-003 Record a transition with actor, timestamp and reason
// FN-WFL-002 Refuse an approval by the author of the version, by any route
//   IT-010 An unpermitted transition is rejected; a permitted one records actor and timestamp
//   IT-011 The author of a version cannot approve it
public sealed class LifecycleServiceTests
{
    private static readonly VersionRef Version = new("doc-1", 1);

    private static LifecycleModel Model() => LifecycleModelConfiguration.LoadFrom(
        Path.Combine(RepositoryRoot(), "config", "lifecycle", "label-states.json"));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EpiPlatform.sln")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }

    private static async Task<(LifecycleService Service, InMemoryLifecycleStore Store)> InReviewAsync(
        string author = "user-anna")
    {
        var store = new InMemoryLifecycleStore();
        var service = new LifecycleService(Model(), store);
        await service.RegisterAsync(Version, author);
        await service.TransitionAsync(Version, "submit", author);
        return (service, store);
    }

    [Fact]
    public async Task IT_010_a_permitted_transition_records_actor_and_time()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 13, 10, 0, 0, TimeSpan.Zero));
        var store = new InMemoryLifecycleStore();
        var service = new LifecycleService(Model(), store, clock);
        await service.RegisterAsync(Version, "user-anna");

        var transition = await service.TransitionAsync(
            Version, "submit", "user-anna", reason: "ready for review");

        Assert.Equal("draft", transition.From);
        Assert.Equal("in-review", transition.To);
        Assert.Equal("user-anna", transition.Actor);
        Assert.Equal(clock.GetUtcNow(), transition.At);
        Assert.Equal("ready for review", transition.Reason);
        Assert.Equal("in-review", await store.CurrentStateAsync(Version));
    }

    [Fact]
    public async Task IT_010_a_transition_the_model_does_not_permit_is_refused()
    {
        var store = new InMemoryLifecycleStore();
        var service = new LifecycleService(Model(), store);
        await service.RegisterAsync(Version, "user-anna");

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(Version, "approve", "user-ben", signatureReference: "sig-1"));

        Assert.Contains("permits no", refused.Reason);
        Assert.Equal("draft", await store.CurrentStateAsync(Version));
    }

    [Fact]
    public async Task IT_010_a_refused_transition_leaves_no_history_behind()
    {
        var (service, store) = await InReviewAsync();

        await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(Version, "supersede", "user-ben"));

        Assert.Single(await store.HistoryAsync(Version));
    }

    [Fact]
    public async Task IT_011_the_author_of_a_version_may_not_approve_it()
    {
        var (service, _) = await InReviewAsync(author: "user-anna");

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(Version, "approve", "user-anna", signatureReference: "sig-1"));

        Assert.Contains("may not approve", refused.Reason);
    }

    [Fact]
    public async Task IT_011_someone_other_than_the_author_may_approve_it()
    {
        var (service, store) = await InReviewAsync(author: "user-anna");

        await service.TransitionAsync(Version, "approve", "user-ben", signatureReference: "sig-1");

        Assert.Equal("approved", await store.CurrentStateAsync(Version));
    }

    [Fact]
    public async Task IT_011_an_unknown_author_refuses_approval_rather_than_allowing_it()
    {
        // If the platform cannot say who wrote a version it cannot assure segregation of
        // duties, and the safe answer is no. Treating unknown as "not the approver" would make
        // the control depend on data being present rather than on it being checked.
        var store = new InMemoryLifecycleStore();
        var service = new LifecycleService(Model(), store);

        var orphan = new VersionRef("doc-3", 1);
        await store.AppendAsync(new StateTransition(
            orphan, "draft", "in-review", "submit", "someone", DateTimeOffset.UtcNow));

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(orphan, "approve", "user-ben", signatureReference: "sig-1"));

        Assert.Contains("author", refused.Reason);
    }

    [Fact]
    public async Task FN_LCM_003_a_transition_the_model_says_must_be_signed_cannot_be_made_unsigned()
    {
        var (service, _) = await InReviewAsync();

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(Version, "approve", "user-ben"));

        Assert.Contains("signature", refused.Reason);
    }

    [Fact]
    public async Task FN_LCM_003_history_is_kept_in_order_and_the_store_offers_no_way_to_amend_it()
    {
        var (service, store) = await InReviewAsync();
        await service.TransitionAsync(Version, "approve", "user-ben", signatureReference: "sig-1");

        var history = await store.HistoryAsync(Version);

        Assert.Equal(["submit", "approve"], history.Select(t => t.Action));
        Assert.Equal(["draft", "in-review"], history.Select(t => t.From));
        Assert.DoesNotContain(typeof(ILifecycleStore).GetMethods(),
            m => m.Name.Contains("Update", StringComparison.Ordinal)
                 || m.Name.Contains("Delete", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FN_LCM_003_a_version_not_under_management_cannot_transition()
    {
        var service = new LifecycleService(Model(), new InMemoryLifecycleStore());

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(new VersionRef("never-registered", 1), "submit", "user-anna"));

        Assert.Contains("not under lifecycle management", refused.Reason);
    }
}
