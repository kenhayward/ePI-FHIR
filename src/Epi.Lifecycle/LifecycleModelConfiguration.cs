namespace Epi.Lifecycle;

/// <summary>Loads a lifecycle state model from configuration data (FN-LCM-001).</summary>
public static class LifecycleModelConfiguration
{
    public static LifecycleModel LoadFrom(string path) => throw new NotImplementedException();
}

/// <summary>Raised when a state model cannot be activated.</summary>
public sealed class LifecycleConfigurationException(IReadOnlyList<string> problems)
    : Exception($"The lifecycle model is not valid and was not activated:{Environment.NewLine}  "
        + string.Join($"{Environment.NewLine}  ", problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;
}
