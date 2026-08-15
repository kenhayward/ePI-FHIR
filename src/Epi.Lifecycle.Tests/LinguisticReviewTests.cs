using Microsoft.Extensions.Time.Testing;
using Xunit;

namespace Epi.Lifecycle.Tests;

// Linguistic review, assembled entirely from configuration (ADR-032, ADR-031).
//   CAP-LOC-002 In-system translation management, including linguistic review
//   CAP-LOC-007 Segregation of translator from approver
//   CAP-WFL-001 Configurable review and approval workflows, config-as-data
//
// The point of this file is what is absent from it: no new engine, no new gate, no new store.
// A whole review process for a different kind of content, performed by different people with a
// different signature meaning, is two configuration files (capability 21, ADR-012). If any of
// these cases needed a code change, config-as-data would be a claim rather than a property.
public sealed class LinguisticReviewTests
{
    private static readonly DateTimeOffset Noon = new(2026, 8, 15, 12, 0, 0, TimeSpan.Zero);

    private static readonly VersionRef French = new("variant-fr", 1);

    private static readonly ApprovalContext Approved = new(
        "sha-256:abc123",
        [new PinnedPackage("hl7.fhir.uv.emedicinal-product-info", "1.0.0", "c99767")],
        "https://epi.example.org/identifier/document");

    /// <summary>Records what the gate was asked, so a test can assert the meaning it demanded.</summary>
    private sealed class Signatures : ISignatureCheck
    {
        public List<(VersionRef Version, string Actor, string Meaning)> Asked { get; } = [];

        public Task<SignatureCheckResult> IsValidAsync(
            string reference, VersionRef version, string actor, string meaning,
            CancellationToken cancellationToken = default)
        {
            Asked.Add((version, actor, meaning));
            return Task.FromResult(SignatureCheckResult.Valid);
        }
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "EpiPlatform.sln")))
        {
            directory = directory.Parent;
        }

        return directory!.FullName;
    }

    private static (LifecycleService Service, InMemoryWorkflowStore Tasks, Signatures Gate) Build()
    {
        var tasks = new InMemoryWorkflowStore();
        var gate = new Signatures();

        return (new LifecycleService(
            LifecycleModelConfiguration.LoadFrom(
                Path.Combine(RepositoryRoot(), "config", "lifecycle", "variant-states.json")),
            new InMemoryLifecycleStore(),
            new FakeTimeProvider(Noon),
            gate,
            workflow: WorkflowConfiguration.LoadFrom(
                Path.Combine(RepositoryRoot(), "config", "workflow", "variant-routing.json")),
            tasks: tasks), tasks, gate);
    }

    /// <summary>The translator writes the variant and submits it for linguistic review.</summary>
    private static async Task SubmitAsync(LifecycleService service)
    {
        await service.RegisterAsync(French, "user-translator");
        await service.TransitionAsync(French, "submit", "user-translator");
    }

    [Fact]
    public async Task CAP_LOC_002_a_submitted_translation_waits_on_a_linguistic_reviewer()
    {
        var (service, tasks, _) = Build();

        await SubmitAsync(service);

        var task = Assert.Single(await tasks.ForVersionAsync(French));
        Assert.Equal("linguistic-reviewer", task.Assignee);
        Assert.Equal("approve", task.Action);
    }

    [Fact]
    public async Task CAP_LOC_007_the_translator_may_not_approve_their_own_translation()
    {
        // Segregation of translator from approver falls out of the mechanism that already
        // segregates author from approver: for a variant, the author is the translator.
        var (service, _, _) = Build();
        await SubmitAsync(service);

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(
                French, "approve", "user-translator", signatureReference: "sig-1",
                approvalContext: Approved));

        Assert.Contains("author", refused.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CAP_LOC_002_linguistic_review_captures_a_signature_meaning_review()
    {
        // A review is not an approval of the content: the reviewer is asserting that the
        // translation says what the source says. The model asks for the meaning that matches,
        // and the gate demands it (ADR-020).
        var (service, _, gate) = Build();
        await SubmitAsync(service);

        await service.TransitionAsync(
            French, "approve", "user-reviewer", signatureReference: "sig-1",
            approvalContext: Approved);

        var asked = Assert.Single(gate.Asked);
        Assert.Equal(French, asked.Version);
        Assert.Equal("user-reviewer", asked.Actor);
        Assert.Equal("review", asked.Meaning);
    }

    [Fact]
    public async Task CAP_LOC_002_an_unsigned_linguistic_review_is_refused()
    {
        var (service, _, _) = Build();
        await SubmitAsync(service);

        var refused = await Assert.ThrowsAsync<TransitionRefusedException>(
            () => service.TransitionAsync(
                French, "approve", "user-reviewer", approvalContext: Approved));

        Assert.Contains("signature", refused.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task CAP_WFL_001_reviewing_closes_the_task_that_asked_for_it()
    {
        var (service, tasks, _) = Build();
        await SubmitAsync(service);

        await service.TransitionAsync(
            French, "approve", "user-reviewer", signatureReference: "sig-1",
            approvalContext: Approved);

        Assert.All(await tasks.ForVersionAsync(French), task => Assert.False(task.IsOpen));
    }

    [Fact]
    public async Task CAP_LOC_002_a_reviewer_may_return_a_translation_rather_than_approve_it()
    {
        // The step that makes review a review. Returning is unsigned: sending something back is
        // not an assertion about it.
        var (service, tasks, gate) = Build();
        await SubmitAsync(service);

        await service.TransitionAsync(French, "return", "user-reviewer", reason: "term inconsistent");

        Assert.Equal("draft", await service.CurrentStateAsync(French));
        Assert.Empty(gate.Asked);
        Assert.All(await tasks.ForVersionAsync(French), task => Assert.False(task.IsOpen));
    }

    [Fact]
    public void CAP_WFL_001_the_variant_process_is_configuration_and_not_a_second_engine()
    {
        // Asserted on the configuration itself, because the claim is about where the process
        // lives rather than about what it does. A variant reaches approval through a different
        // state name, a different reviewer and a different signature meaning from a label, and
        // none of that is in code.
        var variant = LifecycleModelConfiguration.LoadFrom(
            Path.Combine(RepositoryRoot(), "config", "lifecycle", "variant-states.json"));
        var label = LifecycleModelConfiguration.LoadFrom(
            Path.Combine(RepositoryRoot(), "config", "lifecycle", "label-states.json"));

        var review = variant.Find("in-linguistic-review", "approve");
        Assert.NotNull(review);
        Assert.True(review!.RequiresSignature);
        Assert.True(review.SegregatedFromAuthor);
        Assert.Equal("review", review.SignatureMeaning);

        // The label's gate asks for an approval, not a review, and neither model knows about
        // the other.
        Assert.Equal("approval", label.Find("in-review", "approve")!.SignatureMeaning);
        Assert.Null(variant.Find("in-review", "approve"));
    }

    [Fact]
    public void CAP_LOC_002_the_shipped_variant_routing_asks_a_linguistic_reviewer()
    {
        var model = WorkflowConfiguration.LoadFrom(
            Path.Combine(RepositoryRoot(), "config", "workflow", "variant-routing.json"));

        var rule = model.For("in-linguistic-review");

        Assert.NotNull(rule);
        Assert.Equal("linguistic-reviewer", rule!.Assignee);
        Assert.Equal(TimeSpan.FromHours(72), rule.Within);
    }
}
