using Xunit;

namespace Epi.Rendering.Tests;

// Retention as configuration (ADR-034 decision 2).
//   CAP-RND-002 Store rendered output and ingested artwork, kept apart and write-once
//
// Every case here is about refusing rather than defaulting. A store that quietly picked a period
// when the configuration did not name one would write artefacts that look protected and are not,
// and the difference only surfaces when somebody tries to destroy one.
public sealed class AssetRetentionTests
{
    private const string Configured = """
        {"lineages": [
          {"lineage": "rendered", "retentionDays": 3650},
          {"lineage": "artwork", "retentionDays": 1825}
        ]}
        """;

    [Fact]
    public void CAP_RND_002_each_lineage_keeps_its_own_period()
    {
        var retention = AssetRetention.Parse(Configured);

        Assert.Equal(TimeSpan.FromDays(3650), retention.For(AssetKey.RenderedLineage));
        Assert.Equal(TimeSpan.FromDays(1825), retention.For(AssetKey.ArtworkLineage));
    }

    [Fact]
    public void CAP_RND_002_a_lineage_with_no_configured_period_is_refused()
    {
        var retention = AssetRetention.Parse(Configured);

        Assert.Throws<AssetRetentionException>(() => retention.For("something-else"));
    }

    [Fact]
    public void CAP_RND_002_a_period_of_zero_is_refused()
    {
        // Zero enables object-lock and then does not use it, which is the shape of the defect
        // ADR-034 found in the development stack's own bucket setup.
        Assert.Throws<AssetRetentionException>(
            () => AssetRetention.Parse("""{"lineages": [{"lineage": "rendered", "retentionDays": 0}]}"""));
    }

    [Fact]
    public void CAP_RND_002_a_lineage_configured_twice_is_refused()
    {
        // Which entry wins would otherwise depend on the order of the file, and the two could
        // differ by years without anything looking wrong.
        Assert.Throws<AssetRetentionException>(() => AssetRetention.Parse("""
            {"lineages": [
              {"lineage": "rendered", "retentionDays": 3650},
              {"lineage": "rendered", "retentionDays": 30}
            ]}
            """));
    }

    [Fact]
    public void CAP_RND_002_configuration_with_no_lineages_at_all_is_refused()
    {
        Assert.Throws<AssetRetentionException>(() => AssetRetention.Parse("""{"note": "empty"}"""));
    }

    [Fact]
    public void CAP_RND_002_a_missing_configuration_file_is_refused_rather_than_assumed()
    {
        Assert.Throws<AssetRetentionException>(
            () => AssetRetention.Load(Path.Combine(Path.GetTempPath(), "no-such-retention.json")));
    }

    [Fact]
    public void CAP_RND_002_the_shipped_configuration_covers_both_lineages()
    {
        // The one that would have caught a lineage added to the code and forgotten in the file.
        var retention = AssetRetention.Load(Path.GetFullPath(Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "config", "assets", "retention.json")));

        Assert.True(retention.For(AssetKey.RenderedLineage) > TimeSpan.Zero);
        Assert.True(retention.For(AssetKey.ArtworkLineage) > TimeSpan.Zero);
    }
}
