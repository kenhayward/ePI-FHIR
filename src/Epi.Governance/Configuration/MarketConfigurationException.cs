namespace Epi.Governance.Configuration;

/// <summary>
/// Raised when market configuration cannot be activated. Carries every problem found rather
/// than only the first, so a configuration author can fix a file in one pass (CAP-CFG-006,
/// and the itemised-errors expectation shared with CAP-VAL-005).
/// </summary>
public sealed class MarketConfigurationException(IReadOnlyList<string> problems)
    : Exception(BuildMessage(problems))
{
    public IReadOnlyList<string> Problems { get; } = problems;

    private static string BuildMessage(IReadOnlyList<string> problems) =>
        $"Market configuration is not valid and was not activated:{Environment.NewLine}  "
        + string.Join($"{Environment.NewLine}  ", problems);
}
