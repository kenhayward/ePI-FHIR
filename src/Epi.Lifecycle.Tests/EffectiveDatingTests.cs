using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Epi.Lifecycle.Tests;

// FN-LCM-004 Effective dating, per market (ADR-029).
//   CAP-LCM-004 When an approved version becomes effective
//   CAP-SCH-002 Which version applies, as distinct from which has been approved
public sealed class EffectiveDatingTests
{
    private static readonly DateTimeOffset March =
        new(2026, 3, 1, 9, 0, 0, TimeSpan.Zero);

    private static LifecycleModel Model() => LifecycleModelConfiguration.LoadFrom(
        Path.Combine(RepositoryRoot(), "config", "lifecycle", "market-approval-states.json"));

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EpiPlatform.sln")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }

    private sealed class AnySignature : ISignatureCheck
    {
        public Task<SignatureCheckResult> IsValidAsync(
            string reference, VersionRef version, string actor, string meaning,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(SignatureCheckResult.Valid);
    }

    private static (MarketApprovalService Service, FakeTimeProvider Clock) Build()
    {
        var clock = new FakeTimeProvider(March);
        return (new MarketApprovalService(
            Model(), new InMemoryMarketApprovalStore(), new HashSet<string>(["GB", "EU"]),
            clock, new AnySignature()), clock);
    }

    /// <summary>Walks a version to approved in a market, taking effect when told.</summary>
    private static async Task ApproveAsync(
        MarketApprovalService service, FakeTimeProvider clock, int version, string market,
        DateTimeOffset effectiveFrom)
    {
        var reference = new VersionRef("doc-1", version);
        await service.TransitionAsync(
            reference, market, "submit", "user-rae", signatureReference: $"sig-{version}-{market}");
        await service.TransitionAsync(reference, market, "begin-assessment", "user-rae");
        await service.TransitionAsync(
            reference, market, "record-approval", "user-rae", effectiveFrom: effectiveFrom);
        _ = clock;
    }

    [Fact]
    public async Task CAP_LCM_004_nothing_is_in_force_before_the_first_version_takes_effect()
    {
        var (service, clock) = Build();
        await ApproveAsync(service, clock, 1, "GB", March.AddMonths(3));

        Assert.Null(await service.InForceAsync("doc-1", "GB", March.AddMonths(1)));
    }

    [Fact]
    public async Task CAP_LCM_004_the_version_in_force_changes_when_a_later_one_takes_effect()
    {
        // Acceptance criterion 4: before, between, and after two effective dates.
        var (service, clock) = Build();
        await ApproveAsync(service, clock, 1, "GB", March.AddMonths(1));
        await ApproveAsync(service, clock, 2, "GB", March.AddMonths(6));

        Assert.Null(await service.InForceAsync("doc-1", "GB", March));
        Assert.Equal(1, await service.InForceAsync("doc-1", "GB", March.AddMonths(3)));
        Assert.Equal(2, await service.InForceAsync("doc-1", "GB", March.AddMonths(9)));
    }

    [Fact]
    public async Task CAP_LCM_004_a_version_is_in_force_from_the_instant_it_takes_effect()
    {
        // The boundary, stated rather than left to whichever comparison was written first.
        var (service, clock) = Build();
        var effective = March.AddMonths(1);
        await ApproveAsync(service, clock, 1, "GB", effective);

        Assert.Null(await service.InForceAsync("doc-1", "GB", effective.AddTicks(-1)));
        Assert.Equal(1, await service.InForceAsync("doc-1", "GB", effective));
    }

    [Fact]
    public async Task CAP_LCM_004_effect_is_per_market_on_the_same_content()
    {
        // The normal case (ADR-005): the same version in force on different days in different
        // markets, and in one market before the other has it at all.
        var (service, clock) = Build();
        await ApproveAsync(service, clock, 1, "GB", March.AddMonths(1));
        await ApproveAsync(service, clock, 1, "EU", March.AddMonths(6));

        Assert.Equal(1, await service.InForceAsync("doc-1", "GB", March.AddMonths(3)));
        Assert.Null(await service.InForceAsync("doc-1", "EU", March.AddMonths(3)));
    }

    [Fact]
    public async Task CAP_LCM_004_a_withdrawn_approval_stops_being_in_force_without_erasing_history()
    {
        // "In force on the third of March" still answers correctly for a date before the
        // withdrawal. What changes is only what is in force now (ADR-029 decision 6).
        var (service, clock) = Build();
        await ApproveAsync(service, clock, 1, "GB", March.AddMonths(1));

        clock.SetUtcNow(March.AddMonths(3));
        await service.TransitionAsync(
            new VersionRef("doc-1", 1), "GB", "withdraw-approval", "user-rae");

        Assert.Equal(1, await service.InForceAsync("doc-1", "GB", March.AddMonths(2)));
        Assert.Null(await service.InForceAsync("doc-1", "GB", March.AddMonths(4)));
    }

    [Fact]
    public async Task CAP_LCM_004_recording_an_approval_without_an_effective_date_is_refused()
    {
        // A missing date defaulted to now is a guess that reads as a fact, and the difference
        // is invisible afterwards (ADR-029 decision 3).
        var (service, _) = Build();
        var reference = new VersionRef("doc-1", 1);
        await service.TransitionAsync(reference, "GB", "submit", "user-rae", signatureReference: "sig-1");
        await service.TransitionAsync(reference, "GB", "begin-assessment", "user-rae");

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(reference, "GB", "record-approval", "user-rae"));

        Assert.Contains("when it takes effect", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CAP_LCM_004_an_effective_date_before_the_approval_is_refused()
    {
        // Tolerated, it produces a history in which effect precedes cause, and every answer
        // computed from it is wrong in a way no later check can detect.
        var (service, _) = Build();
        var reference = new VersionRef("doc-1", 1);
        await service.TransitionAsync(reference, "GB", "submit", "user-rae", signatureReference: "sig-1");
        await service.TransitionAsync(reference, "GB", "begin-assessment", "user-rae");

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(
                reference, "GB", "record-approval", "user-rae",
                effectiveFrom: March.AddDays(-1)));

        Assert.Contains("precedes the approval", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CAP_LCM_004_a_transition_that_does_not_bring_a_version_into_force_takes_no_date()
    {
        // A date on a submission would be a field nobody reads, until somebody does.
        var (service, _) = Build();

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(
                new VersionRef("doc-1", 1), "GB", "submit", "user-rae",
                signatureReference: "sig-1", effectiveFrom: March.AddMonths(1)));

        Assert.Contains("cannot carry an effective date", refused.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CAP_SCH_002_approved_and_in_force_are_different_questions()
    {
        // They differ exactly during a notice period, which is when someone asks.
        var (service, clock) = Build();
        await ApproveAsync(service, clock, 1, "GB", March.AddMonths(6));

        Assert.Equal("approved", await service.CurrentStateAsync(new VersionRef("doc-1", 1), "GB"));
        Assert.Null(await service.InForceAsync("doc-1", "GB", March.AddMonths(1)));
    }

    [Fact]
    public async Task CAP_LCM_004_a_document_no_market_has_approved_is_in_force_nowhere()
    {
        var (service, _) = Build();

        Assert.Null(await service.InForceAsync("never-approved", "GB", March));
    }
}
