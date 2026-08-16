using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Xunit;

namespace Epi.Api.Tests;

// Configuration resolved at start-up rather than on first use (FN-CFG-005).
//   CAP-CFG-006 Validate configuration before activation
//
// Three defects now share one shape, and each was found by running the walkthrough rather than
// by any test: a configuration path that differed only inside a container, so the service
// started, reported healthy, and had silently loaded nothing. The failure appeared later, as
// something not happening - no task raised, no market state model, no routing.
//
// A path that cannot be loaded has to stop the service. Not because loading late is slow, but
// because a configuration error that surfaces days later as a 500 on somebody's approval is a
// configuration error nobody attributes to the deployment that caused it.
//
// Routing is the path that has actually bitten, and it is absent here on purpose: it is settled
// in the pull request that turns routing into a catalogue, where an absent directory stays
// allowed and an unreadable one stops the service.
public sealed class StartUpConfigurationTests(WebApplicationFactory<Program> factory)
    : IClassFixture<WebApplicationFactory<Program>>
{
    private static readonly string Missing =
        Path.Combine(Path.GetTempPath(), $"epi-no-such-config-{Guid.NewGuid():n}");

    private WebApplicationFactory<Program> Host(string setting, string value) =>
        factory.WithWebHostBuilder(host =>
        {
            host.UseSetting("Epi:MarketsPath", TestFixtures.RepositoryPath("config", "markets"));
            host.UseSetting("Epi:IdentifiersPath",
                TestFixtures.RepositoryPath("config", "identifiers.json"));
            host.UseSetting("Epi:Lifecycle:StatesPath",
                TestFixtures.RepositoryPath("config", "lifecycle", "label-states.json"));
            host.UseSetting("Epi:Lifecycle:MarketStatesPath",
                TestFixtures.RepositoryPath("config", "lifecycle", "market-approval-states.json"));
            host.UseSetting(setting, value);
        });

    [Theory]
    [InlineData("Epi:MarketsPath")]
    [InlineData("Epi:IdentifiersPath")]
    [InlineData("Epi:Lifecycle:StatesPath")]
    [InlineData("Epi:Lifecycle:MarketStatesPath")]
    public void FN_CFG_005_a_configuration_path_that_is_not_there_stops_the_service(string setting)
    {
        // Creating the client is what starts the host. The exception type differs by loader -
        // each says what it could not read - so what is asserted is that it does not start,
        // which is the property that matters.
        var host = Host(setting, Missing);

        var failure = Record.Exception(() => host.CreateClient());

        Assert.NotNull(failure);
        Assert.Contains(
            Path.GetFileName(Missing),
            Flatten(failure!),
            StringComparison.Ordinal);
    }

    [Fact]
    public void FN_CFG_005_a_host_whose_configuration_is_all_present_starts()
    {
        // The case that makes the others mean something. Without it, a host that refused to
        // start for any reason at all would pass every test above.
        var host = Host("Epi:MarketsPath", TestFixtures.RepositoryPath("config", "markets"));

        Assert.Null(Record.Exception(() => host.CreateClient()));
    }

    private static string Flatten(Exception error) =>
        error.InnerException is null
            ? error.Message
            : $"{error.Message} {Flatten(error.InnerException)}";
}
