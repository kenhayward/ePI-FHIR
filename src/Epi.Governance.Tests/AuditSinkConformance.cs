using Epi.Governance.Audit;
using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Epi.Governance.Tests;

/// <summary>
/// The behaviour every audit sink must exhibit, whatever backs it (FN-AUD-002, FN-AUD-003).
/// </summary>
/// <remarks>
/// Shared source, run once against the in-memory sink and once against a real PostgreSQL, on the
/// same reasoning as the content store's conformance suite: two implementations of one contract
/// drift unless the same assertions are run against both.
/// </remarks>
public abstract class AuditSinkConformance : IAsyncDisposable
{
    private readonly List<IAuditSink> _created = [];

    /// <summary>A sink ready to use, with its schema in place if it needs one.</summary>
    protected abstract Task<IAuditSink> CreateSinkAsync(TimeProvider? time = null);

    /// <summary>
    /// A sink, remembered so it is disposed when the case finishes. A durable sink owns a
    /// connection pool; leaving one per case open exhausted the server's connections partway
    /// through the suite, which surfaced as a connection torn down mid-handshake rather than as
    /// anything resembling "too many clients".
    /// </summary>
    private async Task<IAuditSink> NewSinkAsync(TimeProvider? time = null)
    {
        var sink = await CreateSinkAsync(time);
        _created.Add(sink);
        return sink;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var sink in _created)
        {
            if (sink is IAsyncDisposable disposable)
            {
                await disposable.DisposeAsync();
            }
        }

        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task FN_AUD_002_the_sink_stamps_the_time_rather_than_trusting_the_caller()
    {
        // A contemporaneous record is one the system timed. A caller-supplied timestamp is a
        // claim, not evidence (ALCOA+).
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 8, 13, 9, 30, 0, TimeSpan.Zero));
        var sink = await NewSinkAsync(clock);

        await sink.AppendAsync(new AuditRecord(
            "user-anna", "content.create", "doc", AuditOutcome.Succeeded,
            RecordedAt: new DateTimeOffset(1999, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        var recorded = Assert.Single(await sink.ReadAsync());
        Assert.Equal(clock.GetUtcNow(), recorded.RecordedAt);
    }

    [Fact]
    public async Task FN_AUD_002_every_field_of_a_record_survives_being_stored_and_read_back()
    {
        // The before and after pair is the point of the record (ADR-018). A sink that dropped
        // either would still look like it worked from every other angle.
        var sink = await NewSinkAsync();

        await sink.AppendAsync(new AuditRecord(
            "user-anna", "content.version", "doc-1@2", AuditOutcome.Denied, default,
            Before: "the previous content", After: "the new content", Reason: "out of scope"));

        var recorded = Assert.Single(await sink.ReadAsync());
        Assert.Equal("user-anna", recorded.Actor);
        Assert.Equal("content.version", recorded.Action);
        Assert.Equal("doc-1@2", recorded.Target);
        Assert.Equal(AuditOutcome.Denied, recorded.Outcome);
        Assert.Equal("the previous content", recorded.Before);
        Assert.Equal("the new content", recorded.After);
        Assert.Equal("out of scope", recorded.Reason);
    }

    [Fact]
    public async Task FN_AUD_002_records_are_returned_oldest_first()
    {
        // Order is evidence. A trail that cannot say what happened before what cannot support
        // a reconstruction (CAP-AUD-004).
        var sink = await NewSinkAsync();

        foreach (var target in new[] { "first", "second", "third" })
        {
            await sink.AppendAsync(
                new AuditRecord("user-anna", "content.create", target, AuditOutcome.Succeeded, default));
        }

        Assert.Equal(["first", "second", "third"], (await sink.ReadAsync()).Select(r => r.Target));
    }

    [Fact]
    public async Task FN_AUD_003_a_reader_cannot_change_history_through_the_list_it_is_given()
    {
        var sink = await NewSinkAsync();
        await sink.AppendAsync(
            new AuditRecord("user-anna", "content.create", "doc", AuditOutcome.Succeeded, default));

        var first = await sink.ReadAsync();
        Assert.IsNotType<AuditRecord[]>(first as object);

        Assert.Single(await sink.ReadAsync());
    }
}
