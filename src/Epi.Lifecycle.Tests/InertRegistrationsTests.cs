using Epi.ContentCore;
using Hl7.Fhir.Model;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Epi.Lifecycle.Tests;

// The other half of ADR-025 (FN-LCM-008).
//   CAP-LCM-002 Version every label as immutable snapshots with a version lineage
//
// Register-before-write chose which way round to fail: a failed content write leaves a record
// that refers to nothing, which is inert rather than dangerous. Inert is not the same as
// harmless. Each one silently reserves a version number that can never be used again - the
// store refuses a second registration, so a retry fails at registration rather than reaching
// the content store - and nothing has ever looked for them. Harmless individually and invisible
// in aggregate is the wrong pair of properties to leave together.
public sealed class InertRegistrationsTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

    private static readonly IdentifierAuthority Authority = IdentifierAuthority.Demonstration;

    private static readonly TimeSpan Settle = TimeSpan.FromMinutes(15);

    private static DocumentIdentity Identity(string value) =>
        new(Authority.DocumentSystem, value);

    private static Bundle Document() => new()
    {
        Type = Bundle.BundleType.Document,
        Entry = [new Bundle.EntryComponent
        {
            Resource = new Composition
            {
                Title = "SYNTHETIC TEST LABEL - Examplinum 10 mg tablets",
                Language = "en-GB",
                Section = [new Composition.SectionComponent
                {
                    Title = "1. What Examplinum is and what it is used for",
                    Text = new Narrative
                    {
                        Status = Narrative.NarrativeStatus.Generated,
                        Div = "<div xmlns=\"http://www.w3.org/1999/xhtml\">"
                              + "<p>Synthetic test content.</p></div>",
                    },
                }],
            },
        }],
    };

    private static (InertRegistrationReport Report, InMemoryLifecycleStore Lifecycle,
        InMemoryContentStore Content, FakeTimeProvider Clock) Subject()
    {
        var clock = new FakeTimeProvider(Now);
        var lifecycle = new InMemoryLifecycleStore();
        var content = new InMemoryContentStore();

        return (
            new InertRegistrationReport(lifecycle, content, Authority.DocumentSystem, clock),
            lifecycle,
            content,
            clock);
    }

    [Fact]
    public async Task FN_LCM_008_a_registration_whose_content_was_written_is_not_reported()
    {
        var (report, lifecycle, content, _) = Subject();
        var document = await content.CreateAsync(Identity("01a00000-0000-7000-8000-00000000000a"), Document());
        await lifecycle.RegisterAsync(
            new VersionRef(document.Identity.Value, 1), "user-anna", "draft",
            Now - TimeSpan.FromHours(1));

        Assert.Empty((await report.RunAsync(Settle)).Inert);
    }

    [Fact]
    public async Task FN_LCM_008_a_registration_with_no_content_behind_it_is_reported()
    {
        var (report, lifecycle, _, _) = Subject();
        await lifecycle.RegisterAsync(
            new VersionRef("01a00000-0000-7000-8000-00000000000b", 1), "user-anna", "draft",
            Now - TimeSpan.FromHours(1));

        var inert = Assert.Single((await report.RunAsync(Settle)).Inert);

        Assert.Equal("01a00000-0000-7000-8000-00000000000b", inert.Version.DocumentIdentifier);
        Assert.Equal("user-anna", inert.Author);
    }

    [Fact]
    public async Task FN_LCM_008_a_registration_still_inside_the_settle_period_is_not_reported()
    {
        // The registration happens moments before the content write, so a report run against a
        // live system would otherwise flag every write in flight. Reporting a write that is
        // about to succeed as a failure is worse than reporting nothing: it trains whoever
        // reads the report to ignore it.
        var (report, lifecycle, _, _) = Subject();
        await lifecycle.RegisterAsync(
            new VersionRef("01a00000-0000-7000-8000-00000000000c", 1), "user-anna", "draft",
            Now - TimeSpan.FromSeconds(30));

        Assert.Empty((await report.RunAsync(Settle)).Inert);
    }

    [Fact]
    public async Task FN_LCM_008_the_settle_period_ends_and_the_registration_appears()
    {
        // The same registration, seen later. Nothing about it changed; the clock moved.
        var (report, lifecycle, _, clock) = Subject();
        await lifecycle.RegisterAsync(
            new VersionRef("01a00000-0000-7000-8000-00000000000c", 1), "user-anna", "draft",
            Now - TimeSpan.FromSeconds(30));

        Assert.Empty((await report.RunAsync(Settle)).Inert);

        clock.Advance(TimeSpan.FromHours(1));

        Assert.Single((await report.RunAsync(Settle)).Inert);
    }

    [Fact]
    public async Task FN_LCM_008_the_report_says_what_the_registration_blocks()
    {
        // The consequence an operator needs, and the one that is not obvious: the version
        // number is spent. A retry of the same write fails at registration rather than at the
        // content store, so this document can never have a version 2.
        var (report, lifecycle, content, _) = Subject();
        var document = await content.CreateAsync(Identity("01a00000-0000-7000-8000-00000000000d"), Document());
        await lifecycle.RegisterAsync(
            new VersionRef(document.Identity.Value, 1), "user-anna", "draft", Now - TimeSpan.FromDays(2));
        await lifecycle.RegisterAsync(
            new VersionRef(document.Identity.Value, 2), "user-anna", "draft", Now - TimeSpan.FromDays(1));

        var inert = Assert.Single((await report.RunAsync(Settle)).Inert);

        Assert.Equal(2, inert.Version.Version);
        Assert.True(
            inert.BlocksVersionNumber,
            "a registration over a document that exists has reserved a version number nobody "
            + "can now write");
    }

    [Fact]
    public async Task FN_LCM_008_a_registration_over_no_document_at_all_blocks_nothing_reusable()
    {
        // Version 1 of a document that was never created. The identifier is minted per
        // document, so nothing is waiting to reuse it - the author simply starts again and gets
        // a new one. Distinguished from the case above because the remedy differs.
        var (report, lifecycle, _, _) = Subject();
        await lifecycle.RegisterAsync(
            new VersionRef("01a00000-0000-7000-8000-00000000000e", 1), "user-anna", "draft",
            Now - TimeSpan.FromDays(1));

        var inert = Assert.Single((await report.RunAsync(Settle)).Inert);

        Assert.False(inert.BlocksVersionNumber);
    }

    [Fact]
    public async Task FN_LCM_008_the_report_changes_nothing()
    {
        // Read-only, and not by omission. The lifecycle record is append-only, so removing an
        // inert registration would be destroying an audit record to tidy up a report - and the
        // record is evidence that somebody attempted a write, which is worth keeping whether or
        // not the write landed. What to do about one is a human decision.
        var (report, lifecycle, _, _) = Subject();
        var version = new VersionRef("01a00000-0000-7000-8000-00000000000f", 1);
        await lifecycle.RegisterAsync(version, "user-anna", "draft", Now - TimeSpan.FromDays(1));

        await report.RunAsync(Settle);

        Assert.Equal("user-anna", await lifecycle.AuthorOfAsync(version));
        Assert.Single((await report.RunAsync(Settle)).Inert);
    }

    [Fact]
    public async Task FN_LCM_008_the_report_records_when_it_ran_and_what_it_ignored()
    {
        // A report that says "three inert registrations" without saying what it skipped cannot
        // be compared with the one from last week.
        var (report, _, _, _) = Subject();

        var ran = await report.RunAsync(Settle);

        Assert.Equal(Now, ran.RanAt);
        Assert.Equal(Settle, ran.SettlePeriod);
        Assert.Empty(ran.Inert);
    }

    [Fact]
    public async Task FN_LCM_008_inert_registrations_come_back_oldest_first()
    {
        var (report, lifecycle, _, _) = Subject();
        await lifecycle.RegisterAsync(
            new VersionRef("doc-newer", 1), "user-anna", "draft", Now - TimeSpan.FromDays(1));
        await lifecycle.RegisterAsync(
            new VersionRef("doc-older", 1), "user-anna", "draft", Now - TimeSpan.FromDays(9));

        var inert = (await report.RunAsync(Settle)).Inert;

        Assert.Equal(["doc-older", "doc-newer"], inert.Select(i => i.Version.DocumentIdentifier));
    }

    [Fact]
    public async Task FN_LCM_008_a_settle_period_of_zero_is_refused()
    {
        // Zero would report every write in flight, and the report has no way to tell those from
        // the ones that failed.
        var (report, _, _, _) = Subject();

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
            () => report.RunAsync(TimeSpan.Zero));
    }
}
