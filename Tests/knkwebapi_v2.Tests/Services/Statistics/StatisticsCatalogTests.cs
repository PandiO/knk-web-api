using knkwebapi_v2.Enums;
using knkwebapi_v2.Services.Statistics;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Statistics;

/// <summary>The catalogue matches DESIGN.md §F.1 (metric keys, setting keys, contextual settings,
/// menu groups) and the plan's plugin-writability rule (IMPLEMENTATION_PLAN.md §2).</summary>
public class StatisticsCatalogTests
{
    // DESIGN.md §F.1 "Visibility setting keys".
    private static readonly string[] DesignSettingKeys =
    {
        "logins", "pvp_kills", "pve_kills", "deaths", "damage_dealt", "damage_received", "gate_damage", "arrows_fired",
        "headshots", "highest_killstreak", "wins", "losses", "draws", "objectives_captured", "distance.foot",
        "distance.flying", "distance.vehicle", "highest_fall", "economy", "discoveries.counts", "discoveries.list",
        "title_history"
    };

    // DESIGN.md §F.1 "Contextual settings".
    private static readonly string[] DesignContextualSettings =
    {
        "pvp_kills", "pve_kills", "damage_dealt", "damage_received", "gate_damage", "arrows_fired", "headshots",
        "highest_killstreak", "wins", "losses", "draws", "objectives_captured"
    };

    [Fact]
    public void SettingKeys_AreExactlyTheDesignedOnes()
    {
        Assert.Equal(DesignSettingKeys.OrderBy(k => k), StatisticsCatalog.SettingKeys.OrderBy(k => k));
    }

    [Fact]
    public void ContextualSettings_AreExactlyTheDesignedOnes()
    {
        Assert.Equal(DesignContextualSettings.OrderBy(k => k), StatisticsCatalog.ContextualSettingKeys.OrderBy(k => k));
    }

    [Fact]
    public void Groups_CoverEverySettingOnce_InTheDesignedGroups()
    {
        var grouped = StatisticsCatalog.Groups.SelectMany(g => g.SettingKeys).ToList();
        Assert.Equal(grouped.Count, grouped.Distinct().Count());
        Assert.Equal(StatisticsCatalog.SettingKeys.OrderBy(k => k), grouped.OrderBy(k => k));

        string GroupOf(string setting) => StatisticsCatalog.Groups.Single(g => g.SettingKeys.Contains(setting)).Key;
        Assert.Equal("activity", GroupOf("logins"));
        Assert.Equal("combat", GroupOf("deaths"));
        Assert.Equal("minigames", GroupOf("gate_damage"));
        Assert.Equal("exploration", GroupOf("discoveries.list"));
        Assert.Equal("progression", GroupOf("economy"));
        Assert.Equal("progression", GroupOf("title_history"));
    }

    [Fact]
    public void MetricKeys_AreUnique_AndConfigurableOnesPointToAnExistingSetting()
    {
        var keys = StatisticsCatalog.Metrics.Select(m => m.Key).ToList();
        Assert.Equal(keys.Count, keys.Distinct().Count());

        foreach (var metric in StatisticsCatalog.Metrics)
        {
            switch (metric.Visibility)
            {
                case StatisticVisibilityKind.Configurable:
                    Assert.NotNull(StatisticsCatalog.FindSetting(metric.SettingKey));
                    break;
                default:
                    Assert.Null(metric.SettingKey);
                    break;
            }
            // A contextual setting needs contextual metrics behind it.
            if (metric.SettingKey != null && StatisticsCatalog.FindSetting(metric.SettingKey)!.Contextual)
            {
                Assert.True(metric.Contextual, metric.Key);
            }
        }
    }

    [Theory]
    [InlineData("active_playtime", StatisticVisibilityKind.AlwaysPublic)]
    [InlineData("afk_time", StatisticVisibilityKind.AlwaysPublic)]
    [InlineData("xp_gained", StatisticVisibilityKind.AlwaysPublic)] // L1-21
    [InlineData("deaths_by_cause.player", StatisticVisibilityKind.Internal)]
    [InlineData("deaths_by_cause.mob", StatisticVisibilityKind.Internal)]
    [InlineData("deaths_by_cause.environment", StatisticVisibilityKind.Internal)]
    [InlineData("distance.swim", StatisticVisibilityKind.Internal)]
    [InlineData("logins", StatisticVisibilityKind.Configurable)]
    public void Visibility_IsAsDesigned(string key, StatisticVisibilityKind expected)
    {
        Assert.Equal(expected, StatisticsCatalog.FindMetric(key)!.Visibility);
    }

    [Fact]
    public void Records_AreMaxMetrics()
    {
        Assert.Equal(new[] { "highest_fall", "highest_killstreak" },
            StatisticsCatalog.Metrics.Where(m => m.Aggregation == StatisticAggregation.Max).Select(m => m.Key).OrderBy(k => k));
    }

    [Theory]
    [InlineData("pvp_kills", "open_world", true)]
    [InlineData("pvp_kills", "siege", false)]
    [InlineData("deaths", "siege", false)]
    [InlineData("highest_killstreak", "siege", false)]
    [InlineData("deaths", "open_world", true)]
    [InlineData("pve_kills", "siege", true)]
    [InlineData("gate_damage", "siege", true)]
    [InlineData("deaths_by_cause.player", "siege", true)]
    [InlineData("wins", "siege", false)]
    [InlineData("wins", "arena", false)]
    [InlineData("objectives_captured", "siege", false)]
    [InlineData("xp_gained", "", false)]
    [InlineData("coins_earned", "", false)]
    [InlineData("discoveries", "", false)]
    [InlineData("logins", "", false)]
    [InlineData("distance.foot", "", true)]
    [InlineData("nope", "", false)]
    public void IsPluginWritable(string metric, string context, bool expected)
    {
        Assert.Equal(expected, StatisticsCatalog.IsPluginWritable(metric, context));
    }

    [Theory]
    [InlineData("open_world", true)]
    [InlineData("siege", true)]
    [InlineData("siege_survival", true)]
    [InlineData("a", true)]
    [InlineData("", false)]
    [InlineData("Siege", false)]
    [InlineData("1arena", false)]
    [InlineData("open-world", false)]
    [InlineData("abcdefghijklmnopqrstuvwxyz0123456", false)] // 33 chars
    public void IsValidContext(string context, bool expected)
    {
        Assert.Equal(expected, StatisticsCatalog.IsValidContext(context));
    }

    [Fact]
    public void MaxPerEntry_FollowsThePlan()
    {
        Assert.Equal(86_400m, StatisticsCatalog.FindMetric("active_playtime")!.MaxPerEntry);
        Assert.Equal(100_000m, StatisticsCatalog.FindMetric("distance.vehicle")!.MaxPerEntry);
        Assert.Equal(100_000m, StatisticsCatalog.FindMetric("damage_dealt.mob")!.MaxPerEntry);
        Assert.Equal(10_000m, StatisticsCatalog.FindMetric("pve_kills")!.MaxPerEntry);
        Assert.Equal(10_000m, StatisticsCatalog.FindMetric("highest_fall")!.MaxPerEntry);
    }
}
