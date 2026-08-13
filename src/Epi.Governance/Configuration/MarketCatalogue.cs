namespace Epi.Governance.Configuration;

/// <summary>
/// The set of markets the platform is configured to operate in (capability 21).
/// Loaded from configuration data so that onboarding a market is a configuration change
/// rather than a code release (CAP-CFG-004, ADR-012).
/// </summary>
public sealed class MarketCatalogue
{
    /// <summary>Loads and validates every market definition in a directory.</summary>
    /// <exception cref="MarketConfigurationException">
    /// If the directory is missing, or any definition is invalid. Loading is all or nothing:
    /// an invalid definition means no catalogue rather than a partial one (CAP-CFG-006).
    /// </exception>
    public static MarketCatalogue LoadFrom(string directory) =>
        throw new NotImplementedException();

    public IReadOnlyCollection<MarketDefinition> Markets => throw new NotImplementedException();

    public int Count => throw new NotImplementedException();

    public MarketDefinition? Find(string code) => throw new NotImplementedException();
}
