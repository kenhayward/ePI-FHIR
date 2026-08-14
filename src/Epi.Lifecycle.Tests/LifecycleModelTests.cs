using Xunit;

namespace Epi.Lifecycle.Tests;

// FN-LCM-001 Load the lifecycle state model from configuration
// FN-LCM-002 Reject a transition the state model does not permit
public sealed class LifecycleModelTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("epi-lifecycle-").FullName;

    private const string Valid = """
        {
          "name": "label",
          "initial": "draft",
          "states": ["draft", "in-review", "approved"],
          "transitions": [
            {"from": "draft", "to": "in-review", "action": "submit"},
            {"from": "in-review", "to": "approved", "action": "approve",
             "requiresSignature": true, "signatureMeaning": "approval",
             "segregatedFromAuthor": true}
          ]
        }
        """;

    private string Write(string content)
    {
        var path = Path.Combine(_directory, "model.json");
        File.WriteAllText(path, content);
        return path;
    }

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    [Fact]
    public void FN_LCM_001_loads_states_and_transitions_from_configuration()
    {
        var model = LifecycleModelConfiguration.LoadFrom(Write(Valid));

        Assert.Equal("label", model.Name);
        Assert.Equal("draft", model.Initial);
        Assert.Equal(["draft", "in-review", "approved"], model.States);
        Assert.Equal(2, model.Transitions.Count);
    }

    [Fact]
    public void FN_LCM_001_carries_the_conditions_a_transition_imposes()
    {
        // Whether a transition demands a signature, and whether it must be made by someone
        // other than the author, are properties of the model rather than of the engine: a
        // different organisation may gate different transitions (ADR-019 decision 3).
        var model = LifecycleModelConfiguration.LoadFrom(Write(Valid));

        var approval = model.Find("in-review", "approve");
        Assert.NotNull(approval);
        Assert.True(approval!.RequiresSignature);
        Assert.True(approval.SegregatedFromAuthor);

        var submission = model.Find("draft", "submit");
        Assert.False(submission!.RequiresSignature);
        Assert.False(submission.SegregatedFromAuthor);
    }

    [Fact]
    public void FN_LCM_002_a_transition_the_model_does_not_permit_is_not_found()
    {
        var model = LifecycleModelConfiguration.LoadFrom(Write(Valid));

        Assert.Null(model.Find("draft", "approve"));
        Assert.Null(model.Find("approved", "submit"));
        Assert.Null(model.Find("draft", "invent-a-state"));
    }

    [Fact]
    public void FN_LCM_002_states_are_matched_exactly_rather_than_loosely()
    {
        // A state model is a control. Matching "Approved" to "approved" would mean a typo in a
        // caller silently succeeding, which is the wrong failure mode for a gate.
        var model = LifecycleModelConfiguration.LoadFrom(Write(Valid));

        Assert.Null(model.Find("Draft", "submit"));
        Assert.Null(model.Find("draft", "Submit"));
    }

    [Fact]
    public void FN_LCM_001_rejects_a_transition_referring_to_a_state_that_does_not_exist()
    {
        // Otherwise a typo produces a model with a transition nothing can ever take, and the
        // failure appears much later as "why can nobody approve anything".
        var path = Write("""
            {
              "name": "label", "initial": "draft",
              "states": ["draft", "approved"],
              "transitions": [{"from": "draft", "to": "in-reveiw", "action": "submit"}]
            }
            """);

        var error = Assert.Throws<LifecycleConfigurationException>(() => LifecycleModelConfiguration.LoadFrom(path));

        Assert.Contains(error.Problems, p => p.Contains("in-reveiw"));
    }

    [Fact]
    public void FN_LCM_001_rejects_an_initial_state_that_is_not_among_the_states()
    {
        var path = Write("""
            {
              "name": "label", "initial": "nowhere",
              "states": ["draft"],
              "transitions": []
            }
            """);

        var error = Assert.Throws<LifecycleConfigurationException>(() => LifecycleModelConfiguration.LoadFrom(path));

        Assert.Contains(error.Problems, p => p.Contains("nowhere"));
    }

    [Fact]
    public void FN_LCM_001_rejects_a_model_with_no_states_at_all()
    {
        var path = Write("""{"name": "label", "initial": "draft", "states": [], "transitions": []}""");

        Assert.Throws<LifecycleConfigurationException>(() => LifecycleModelConfiguration.LoadFrom(path));
    }

    [Fact]
    public void FN_LCM_001_rejects_two_transitions_claiming_the_same_state_and_action()
    {
        // Two rules for one move means the outcome depends on which is read first, which is
        // not something a control may leave to chance.
        var path = Write("""
            {
              "name": "label", "initial": "draft",
              "states": ["draft", "in-review", "approved"],
              "transitions": [
                {"from": "draft", "to": "in-review", "action": "submit"},
                {"from": "draft", "to": "approved", "action": "submit"}
              ]
            }
            """);

        var error = Assert.Throws<LifecycleConfigurationException>(() => LifecycleModelConfiguration.LoadFrom(path));

        Assert.Contains(error.Problems, p => p.Contains("submit"));
    }

    [Fact]
    public void FN_WFL_003_a_transition_that_requires_a_signature_must_say_what_it_means()
    {
        // A gate that demanded a signature without saying what it must assert would accept a
        // signature captured as a review in place of an approval, and record assent nobody
        // gave. The model has to be specific, and configuration that is not is refused.
        var path = Write("""
            {
              "name": "label", "initial": "draft",
              "states": ["draft", "approved"],
              "transitions": [
                {"from": "draft", "to": "approved", "action": "approve", "requiresSignature": true}
              ]
            }
            """);

        var error = Assert.Throws<LifecycleConfigurationException>(() => LifecycleModelConfiguration.LoadFrom(path));

        Assert.Contains(error.Problems, p => p.Contains("signatureMeaning"));
    }

    [Fact]
    public void FN_WFL_003_a_signature_meaning_on_a_gate_that_needs_no_signature_is_refused()
    {
        // Otherwise the model reads as though the gate were signed when it is not, which is a
        // worse failure than either honest state.
        var path = Write("""
            {
              "name": "label", "initial": "draft",
              "states": ["draft", "in-review"],
              "transitions": [
                {"from": "draft", "to": "in-review", "action": "submit", "signatureMeaning": "approval"}
              ]
            }
            """);

        var error = Assert.Throws<LifecycleConfigurationException>(() => LifecycleModelConfiguration.LoadFrom(path));

        Assert.Contains(error.Problems, p => p.Contains("signatureMeaning"));
    }

    [Fact]
    public void FN_WFL_003_the_shipped_model_requires_an_approval_at_the_approval_gate()
    {
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "EpiPlatform.sln")))
        {
            repository = repository.Parent;
        }

        var model = LifecycleModelConfiguration.LoadFrom(
            Path.Combine(repository!.FullName, "config", "lifecycle", "label-states.json"));

        Assert.Equal("approval", model.Find("in-review", "approve")!.SignatureMeaning);
        Assert.Equal("approval", model.Find("approved", "withdraw")!.SignatureMeaning);
    }

    [Fact]
    public void FN_LCM_001_the_shipped_label_model_loads_and_gates_approval()
    {
        // The model this repository ships is the demonstration's control: approval must
        // require a signature and be segregated from the author, or the whole governance
        // story is decoration.
        var repository = new DirectoryInfo(AppContext.BaseDirectory);
        while (repository is not null && !File.Exists(Path.Combine(repository.FullName, "EpiPlatform.sln")))
        {
            repository = repository.Parent;
        }

        var model = LifecycleModelConfiguration.LoadFrom(
            Path.Combine(repository!.FullName, "config", "lifecycle", "label-states.json"));

        var approval = model.Find("in-review", "approve");
        Assert.NotNull(approval);
        Assert.True(approval!.RequiresSignature);
        Assert.True(approval.SegregatedFromAuthor);
    }
}
