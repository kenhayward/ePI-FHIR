using Epi.ContentCore;
using Epi.Contracts;
using Epi.Governance.Events;
using Hl7.Fhir.Model;
using Xunit;

namespace Epi.Governance.Tests;

// FN-EVT-001 Build a content event from a persisted document
// FN-EVT-002 Publish it to the event backbone
// IT-008 Creating a document emits a content event
public sealed class ContentEventTests
{
    private static Bundle Document() => ContentScope.Stamp(
        new Bundle
        {
            Type = Bundle.BundleType.Document,
            Entry = [new Bundle.EntryComponent
            {
                FullUrl = "urn:uuid:0195f3a0-0000-7000-8000-000000000001",
                Resource = new Composition { Title = "SYNTHETIC TEST LABEL" },
            }],
        },
        new DocumentScope("uk-affiliate", "GB"));

    private static (PublishingContentStore Store, InMemoryEventPublisher Events) Build()
    {
        var events = new InMemoryEventPublisher();
        return (new PublishingContentStore(new InMemoryContentStore(), events), events);
    }

    [Fact]
    public async Task IT_008_creating_a_document_emits_an_event_naming_it()
    {
        var (store, events) = Build();

        var stored = await store.CreateAsync(Document());

        var published = Assert.Single(events.Published);
        Assert.Equal(ContentEvent.Created, published.Type);
        Assert.Equal(stored.Identity.Value, published.DocumentIdentifier);
        Assert.Equal(stored.Identity.System, published.DocumentSystem);
        Assert.Equal(1, published.Version);
    }

    [Fact]
    public async Task FN_EVT_001_the_event_carries_the_scope_so_consumers_can_be_filtered_by_permission()
    {
        // CAP-EVT-007: notifications are permission-scoped. A consumer cannot be filtered on
        // an attribute the event does not carry.
        var (store, events) = Build();

        await store.CreateAsync(Document());

        var published = Assert.Single(events.Published);
        Assert.Equal("uk-affiliate", published.Affiliate);
        Assert.Equal("GB", published.Market);
    }

    [Fact]
    public async Task FN_EVT_001_the_event_names_the_content_rather_than_carrying_it()
    {
        // An event carrying the document would route around the authorisation applied on read:
        // being entitled to hear that a label changed is not being entitled to read it.
        var (store, events) = Build();

        await store.CreateAsync(Document());

        var published = Assert.Single(events.Published);
        var everything = string.Join(" ", typeof(ContentEvent).GetProperties().Select(p => p.GetValue(published)));
        Assert.DoesNotContain("SYNTHETIC TEST LABEL", everything);
    }

    [Fact]
    public async Task FN_EVT_002_a_new_version_is_announced_as_a_version_rather_than_a_creation()
    {
        var (store, events) = Build();
        var first = await store.CreateAsync(Document());

        await store.CreateVersionAsync(first.Identity, Document());

        Assert.Equal([ContentEvent.Created, ContentEvent.VersionCreated],
            events.Published.Select(e => e.Type));
        Assert.Equal(2, events.Published[1].Version);
    }

    [Fact]
    public async Task FN_EVT_002_a_failed_write_announces_nothing()
    {
        // Publishing before the write succeeds would have consumers reacting to content that
        // does not exist.
        var (store, events) = Build();
        var unknown = new DocumentIdentity(ContentCoreDefaults.DocumentIdentifierSystem, Guid.NewGuid().ToString());

        await Assert.ThrowsAsync<UnknownDocumentException>(() => store.CreateVersionAsync(unknown, Document()));

        Assert.Empty(events.Published);
    }

    [Fact]
    public async Task FN_EVT_001_the_event_carries_its_schema_version_from_the_first_event()
    {
        // Consumers outside this repository cannot be redeployed in step with it, so the
        // schema is versioned from the start rather than from the first breaking change.
        var (store, events) = Build();

        await store.CreateAsync(Document());

        Assert.Equal(ContentEvent.CurrentSchemaVersion, Assert.Single(events.Published).SchemaVersion);
    }
}
