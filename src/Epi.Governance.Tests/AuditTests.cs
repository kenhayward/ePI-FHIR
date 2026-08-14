using Epi.ContentCore;
using Epi.Governance.Audit;
using Hl7.Fhir.Model;
using Xunit;

namespace Epi.Governance.Tests;

// The evidentiary record (capability 19).
//   FN-AUD-001 Compose an audit record with actor, action, target, before and after
//   FN-AUD-002 Append it to the append-only store
//   FN-AUD-003 Reject update or delete of an existing record
//   FN-AUD-004 Record every access-control decision
//   IT-003 A content write and an access decision each produce an immutable audit record
public sealed class AuditTests
{
    private static Bundle Document(string title = "SYNTHETIC TEST LABEL") => new()
    {
        Type = Bundle.BundleType.Document,
        Entry = [new Bundle.EntryComponent
        {
            FullUrl = "urn:uuid:0195f3a0-0000-7000-8000-000000000001",
            Resource = new Composition { Title = title },
        }],
    };

    [Fact]
    public async Task FN_AUD_003_the_sink_offers_no_way_to_change_or_remove_a_record()
    {
        // Immutability is a property of the interface, not a rule callers follow: there is no
        // update and no delete to call, so no code path can exist that uses one.
        var members = typeof(IAuditSink).GetMethods().Select(m => m.Name).ToArray();

        Assert.Equal(["AppendAsync", "ReadAsync"], members.Order());
        await Task.CompletedTask;
    }

    // Sink-level behaviour lives in AuditSinkConformance, so that the durable sink is held to
    // the same assertions as the in-memory one. What remains here is the decorators, which have
    // one implementation each.

    [Fact]
    public async Task IT_003_a_content_write_produces_a_record_with_before_and_after()
    {
        var sink = new InMemoryAuditSink();
        var store = new AuditingContentStore(new InMemoryContentStore(), sink, "user-anna");

        var first = await store.CreateAsync(ContentIdentity.Mint(), Document());
        await store.CreateVersionAsync(first.Identity, 2, Document("AMENDED"));

        var records = await sink.ReadAsync();
        Assert.Equal(2, records.Count);

        var creation = records[0];
        Assert.Equal("user-anna", creation.Actor);
        Assert.Equal("content.create", creation.Action);
        Assert.Equal(AuditOutcome.Succeeded, creation.Outcome);
        Assert.Null(creation.Before);
        Assert.Contains("SYNTHETIC TEST LABEL", creation.After);

        var amendment = records[1];
        Assert.Equal("content.version", amendment.Action);
        Assert.Contains("SYNTHETIC TEST LABEL", amendment.Before);
        Assert.Contains("AMENDED", amendment.After);
    }

    [Fact]
    public async Task IT_003_a_failed_write_is_recorded_too()
    {
        // An audit trail containing only what worked cannot answer what was attempted, which
        // is where an investigation usually starts.
        var sink = new InMemoryAuditSink();
        var store = new AuditingContentStore(new InMemoryContentStore(), sink, "user-anna");
        var unknown = new DocumentIdentity(ContentCoreDefaults.DocumentIdentifierSystem, Guid.NewGuid().ToString());

        await Assert.ThrowsAsync<UnknownDocumentException>(
            () => store.CreateVersionAsync(unknown, 2, Document()));

        var record = Assert.Single(await sink.ReadAsync());
        Assert.Equal(AuditOutcome.Failed, record.Outcome);
        Assert.False(string.IsNullOrWhiteSpace(record.Reason));
    }

    [Fact]
    public async Task FN_AUD_004_access_decisions_are_recorded_whether_allowed_or_denied()
    {
        // CAP-IAM-009 asks for all access decisions. A trail of refusals alone shows attacks
        // but not misuse by someone entitled to be there, which is the harder case.
        var sink = new InMemoryAuditSink();
        var audited = new AuditedPolicyDecisions<string, bool>(
            decide: (query, _) => Task.FromResult(query == "allowed"),
            audit: sink,
            describeActor: _ => "user-anna",
            describeAction: _ => "read",
            describeTarget: query => query,
            isAllowed: decision => decision,
            describeReason: decision => decision ? "permitted" : "refused");

        await audited.DecideAsync("allowed");
        await audited.DecideAsync("refused");

        var records = await sink.ReadAsync();
        Assert.Equal(2, records.Count);
        Assert.Equal("access.read", records[0].Action);
        Assert.Equal(AuditOutcome.Succeeded, records[0].Outcome);
        Assert.Equal(AuditOutcome.Denied, records[1].Outcome);
    }
}
