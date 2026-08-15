using Hl7.Fhir.Model;
using Xunit;

namespace Epi.ContentCore.Tests;

// Cross-references between sections (ADR-028).
//   CAP-SCM-005 Cross-references within and across documents, with integrity and resolution
public sealed class CrossReferencesTests
{
    private static Composition.SectionComponent Section(
        string id, string title, string narrative = "Plain text.")
    {
        return new Composition.SectionComponent
        {
            ElementId = id,
            Title = title,
            Text = new Narrative
            {
                Status = Narrative.NarrativeStatus.Generated,
                Div = $"<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>{narrative}</p></div>",
            },
        };
    }

    private static Bundle Document(params Composition.SectionComponent[] sections) => new()
    {
        Type = Bundle.BundleType.Document,
        Entry = [new Bundle.EntryComponent
        {
            Resource = new Composition { Title = "SYNTHETIC TEST LABEL", Section = [.. sections] },
        }],
    };

    private static Bundle Referring(string target) => Document(
        Section("sec-2", "2. What you need to know",
            $"See <a href=\"#{target}\">section 4.4</a> before use."),
        Section("sec-44", "4.4 Special warnings"));

    [Fact]
    public void CAP_SCM_005_a_reference_names_the_section_it_points_at_and_the_one_it_came_from()
    {
        var reference = Assert.Single(CrossReferences.In(Referring("sec-44")));

        Assert.Equal("sec-2", reference.SourceSectionIdentifier);
        Assert.Equal("sec-44", reference.TargetSectionIdentifier);
        Assert.True(reference.IsInternal);
    }

    [Fact]
    public void CAP_SCM_005_an_internal_reference_resolves_within_the_version_that_carries_it()
    {
        // Acceptance criterion 3. Resolved against the immutable bytes that contain the
        // reference, so a cross-reference cannot rot: the thing it points into cannot change.
        var bundle = Referring("sec-44");

        var target = CrossReferences.Resolve(bundle, CrossReferences.In(bundle)[0]);

        Assert.Equal("4.4 Special warnings", target!.Title);
    }

    [Fact]
    public void CAP_SCM_005_a_reference_to_a_section_the_document_does_not_have_is_dangling()
    {
        var dangling = Assert.Single(CrossReferences.Dangling(Referring("sec-99")));

        Assert.Equal("sec-99", dangling.TargetSectionIdentifier);
    }

    [Fact]
    public void CAP_SCM_005_a_document_whose_references_all_resolve_has_none_dangling()
    {
        Assert.Empty(CrossReferences.Dangling(Referring("sec-44")));
    }

    [Fact]
    public void CAP_SCM_005_several_references_in_one_paragraph_are_all_found()
    {
        // The reason these live in the narrative rather than on the section: a reference held
        // on the section could not say which words it belongs to, and there may be four.
        var bundle = Document(
            Section("sec-2", "2. What you need to know",
                "See <a href=\"#sec-44\">4.4</a> and <a href=\"#sec-45\">4.5</a>."),
            Section("sec-44", "4.4 Special warnings"),
            Section("sec-45", "4.5 Interactions"));

        Assert.Equal(["sec-44", "sec-45"],
            CrossReferences.In(bundle).Select(r => r.TargetSectionIdentifier));
    }

    [Fact]
    public void CAP_SCM_005_a_reference_from_a_nested_section_is_found_too()
    {
        var parent = Section("sec-4", "4. Clinical particulars");
        parent.Section = [Section("sec-44", "4.4 Special warnings",
            "As in <a href=\"#sec-2\">section 2</a>.")];

        var bundle = Document(Section("sec-2", "2. What you need to know"), parent);

        Assert.Equal("sec-44", Assert.Single(CrossReferences.In(bundle)).SourceSectionIdentifier);
    }

    [Fact]
    public void CAP_SCM_005_references_are_found_before_section_identity_has_been_assigned()
    {
        // Section identity is assigned by the store, so at the write gate the sections have
        // none. Requiring a source identifier to find a reference made the integrity check
        // find nothing in exactly the normal case, and pass everything.
        var bundle = Document(
            new Composition.SectionComponent
            {
                Title = "2. What you need to know",
                Text = new Narrative
                {
                    Status = Narrative.NarrativeStatus.Generated,
                    Div = "<div xmlns=\"http://www.w3.org/1999/xhtml\"><p>See "
                          + "<a href=\"#sec-99\">section 9</a>.</p></div>",
                },
            });

        Assert.Single(CrossReferences.In(bundle));
        Assert.Single(CrossReferences.Dangling(bundle));
    }

    [Fact]
    public void CAP_SCM_005_an_ordinary_link_out_to_the_web_is_not_a_cross_reference()
    {
        // Not the platform's to interpret. Whether narrative may carry one at all is the
        // profile's business, not this code's.
        var bundle = Document(Section("sec-2", "2. Information",
            "See <a href=\"https://example.org/leaflet\">the website</a>."));

        Assert.Empty(CrossReferences.In(bundle));
    }

    [Fact]
    public void CAP_SCM_005_a_cross_document_reference_names_the_document_and_the_version()
    {
        // Pinned, for the reason every reference here is pinned: an unversioned one points at
        // whatever that document says today.
        var bundle = Document(Section("sec-2", "2. Information",
            "See <a href=\"epi:01a00000-0000-7000-8000-00000000000a/3#sec-7\">the SmPC</a>."));

        var reference = Assert.Single(CrossReferences.In(bundle));

        Assert.False(reference.IsInternal);
        Assert.Equal("01a00000-0000-7000-8000-00000000000a", reference.Document);
        Assert.Equal(3, reference.Version);
        Assert.Equal("sec-7", reference.TargetSectionIdentifier);
    }

    [Fact]
    public void CAP_SCM_005_a_cross_document_reference_is_not_checked_against_this_document()
    {
        // Its target is another aggregate, possibly not yet written and possibly out of the
        // caller's scope. Checking it here would fail a write because of something entirely
        // outside it (ADR-028 decision 4).
        var bundle = Document(Section("sec-2", "2. Information",
            "See <a href=\"epi:01a00000-0000-7000-8000-00000000000a/3#sec-7\">the SmPC</a>."));

        Assert.Empty(CrossReferences.Dangling(bundle));
        Assert.Null(CrossReferences.Resolve(bundle, CrossReferences.In(bundle)[0]));
    }

    [Fact]
    public void CAP_SCM_005_a_cross_document_reference_without_a_version_is_not_a_reference()
    {
        var bundle = Document(Section("sec-2", "2. Information",
            "See <a href=\"epi:01a00000-0000-7000-8000-00000000000a#sec-7\">the SmPC</a>."));

        Assert.Empty(CrossReferences.In(bundle));
    }

    [Fact]
    public async Task CAP_SCM_005_content_with_a_dangling_reference_is_refused_at_the_write_gate()
    {
        // A label pointing at a section it does not have is a label with a broken instruction
        // in it, and this is the last place that is cheap to catch.
        var store = new CrossReferenceCheckingContentStore(new InMemoryContentStore());

        var refused = await Assert.ThrowsAsync<InvalidEpiBundleException>(
            () => store.CreateAsync(ContentIdentity.Mint(), Referring("sec-99")));

        Assert.Contains(refused.Problems, p => p.Contains("sec-99", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CAP_SCM_005_content_whose_references_resolve_is_stored()
    {
        var store = new CrossReferenceCheckingContentStore(new InMemoryContentStore());

        var stored = await store.CreateAsync(ContentIdentity.Mint(), Referring("sec-44"));

        Assert.Equal(1, stored.Version);
    }

    [Fact]
    public async Task CAP_SCM_005_a_new_version_is_checked_as_well_as_the_first()
    {
        // Every route that stores content, not only the first one. A reference broken by an
        // edit is the normal way this happens.
        var store = new CrossReferenceCheckingContentStore(new InMemoryContentStore());
        var first = await store.CreateAsync(ContentIdentity.Mint(), Referring("sec-44"));

        await Assert.ThrowsAsync<InvalidEpiBundleException>(
            () => store.CreateVersionAsync(first.Identity, 2, Referring("sec-99")));
    }
}
