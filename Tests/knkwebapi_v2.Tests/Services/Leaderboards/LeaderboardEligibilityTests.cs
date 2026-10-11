using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Leaderboards;
using knkwebapi_v2.Services.Statistics;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Leaderboards;

/// <summary>
/// Pure leaderboard rules (DESIGN.md §F.11, §F.4; link 5 acceptance criterion 1): eligibility per
/// board kind, merged identities, board values per aggregation and context, discoveries, and
/// competition ranking with the reachedAt tie order.
/// </summary>
public class LeaderboardEligibilityTests
{
    private static readonly DateTime T0 = new(2026, 10, 1, 10, 0, 0, DateTimeKind.Utc);

    private static LeaderboardBoardDefinition Board(string key) => LeaderboardCatalog.Find(key)!;

    private static PlayerStatVisibility Row(string setting, StatisticVisibility visibility, string context = "") =>
        new() { UserId = 1, SettingKey = setting, ContextKey = context, Visibility = visibility };

    // ------------------------------------------------------------------ catalogue

    [Fact]
    public void Catalogue_HasTheAdoptedBoards_AndNoFarmableOnes()
    {
        var keys = LeaderboardCatalog.Boards.Select(b => b.BoardKey).ToList();

        Assert.Equal(new[]
        {
            "active_playtime", "xp_gained", "pvp_kills", "pvp_kills@open_world", "pvp_kills@siege", "pve_kills", "pve_kills@open_world",
            "pve_kills@siege", "wins@siege", "objectives_captured@siege", "gate_damage", "distance.foot", "distance.flying",
            "distance.vehicle", "discoveries", "highest_killstreak", "highest_killstreak@open_world", "highest_killstreak@siege"
        }, keys);
        Assert.DoesNotContain(LeaderboardCatalog.Boards, b => b.Metric.Key is "deaths" or "logins" or "afk_time" or "coins_earned");
        Assert.Equal(keys.Count, keys.Distinct().Count());
    }

    [Fact]
    public void PvpBoards_ReadTheCappedCounter_ExceptInProjectionOwnedContexts()
    {
        var pvp = StatisticsCatalog.FindMetric("pvp_kills")!;

        Assert.Equal("pvp_kills.ranked", LeaderboardCatalog.SourceMetricKey(pvp, "open_world"));
        Assert.Equal("pvp_kills", LeaderboardCatalog.SourceMetricKey(pvp, "siege"));
        Assert.Equal("pve_kills", LeaderboardCatalog.SourceMetricKey(StatisticsCatalog.FindMetric("pve_kills")!, "open_world"));
        Assert.Contains("pvp_kills.ranked", LeaderboardCatalog.SourceMetricKeys);
        Assert.DoesNotContain("discoveries", LeaderboardCatalog.SourceMetricKeys);
    }

    [Fact]
    public void RankedPvpCounter_IsInternal_AndNotPluginWritable()
    {
        var ranked = StatisticsCatalog.FindMetric("pvp_kills.ranked")!;

        Assert.Equal(StatisticVisibilityKind.Internal, ranked.Visibility);
        Assert.False(StatisticsCatalog.IsPluginWritable("pvp_kills.ranked", "open_world"));
    }

    // ------------------------------------------------------------------ eligibility

    [Fact]
    public void AlwaysPublicBoards_RankEveryone()
    {
        Assert.True(LeaderboardEligibility.IsEligible(Board("active_playtime"), Array.Empty<PlayerStatVisibility>()));
        Assert.True(LeaderboardEligibility.IsEligible(Board("xp_gained"), Array.Empty<PlayerStatVisibility>()));
    }

    [Theory]
    [InlineData(StatisticVisibility.Everyone, true)]
    [InlineData(StatisticVisibility.Friends, false)] // fails closed until KNG-35
    [InlineData(StatisticVisibility.Nobody, false)]
    public void ConfigurableBoards_NeedEveryone(StatisticVisibility visibility, bool eligible)
    {
        Assert.Equal(eligible, LeaderboardEligibility.IsEligible(Board("distance.foot"), new[] { Row("distance.foot", visibility) }));
        Assert.False(LeaderboardEligibility.IsEligible(Board("distance.foot"), Array.Empty<PlayerStatVisibility>()));
    }

    [Fact]
    public void PerContextBoards_UseTheContextOverride_AndTotalBoardsTheTotalRule()
    {
        var rows = new[] { Row("pvp_kills", StatisticVisibility.Everyone), Row("pvp_kills", StatisticVisibility.Nobody, "siege") };

        Assert.True(LeaderboardEligibility.IsEligible(Board("pvp_kills@open_world"), rows));
        Assert.False(LeaderboardEligibility.IsEligible(Board("pvp_kills@siege"), rows));
        Assert.False(LeaderboardEligibility.IsEligible(Board("pvp_kills"), rows)); // a hidden context must not leak through the total

        var onlySiege = new[] { Row("pvp_kills", StatisticVisibility.Nobody), Row("pvp_kills", StatisticVisibility.Everyone, "siege") };
        Assert.True(LeaderboardEligibility.IsEligible(Board("pvp_kills@siege"), onlySiege));
        Assert.False(LeaderboardEligibility.IsEligible(Board("pvp_kills"), onlySiege));
    }

    [Fact]
    public void DiscoveriesBoard_UsesTheDiscoveryCountsSetting()
    {
        Assert.True(LeaderboardEligibility.IsEligible(Board("discoveries"), new[] { Row("discoveries.counts", StatisticVisibility.Everyone) }));
        Assert.False(LeaderboardEligibility.IsEligible(Board("discoveries"), new[] { Row("discoveries.list", StatisticVisibility.Everyone) }));
    }

    // ------------------------------------------------------------------ merged identities

    [Fact]
    public void PrimaryResolver_FollowsMergesTransitively_AndSurvivesCycles()
    {
        var primaryOf = LeaderboardEligibility.PrimaryResolver(new[] { (3, 2), (2, 1), (8, 9), (9, 8) });

        Assert.Equal(1, primaryOf(3));
        Assert.Equal(1, primaryOf(2));
        Assert.Equal(1, primaryOf(1));
        Assert.Equal(7, primaryOf(7));
        Assert.Contains(primaryOf(8), new[] { 8, 9 });
    }

    // ------------------------------------------------------------------ values

    [Fact]
    public void SumBoards_AddContextsAndIdentities_ReachedAtIsTheLatestContribution()
    {
        var rows = new[]
        {
            new LeaderboardInputRow(1, "pve_kills", "open_world", 4, T0),
            new LeaderboardInputRow(1, "pve_kills", "siege", 2, T0.AddHours(2)),
            new LeaderboardInputRow(3, "pve_kills", "open_world", 1, T0.AddHours(1)),
            new LeaderboardInputRow(2, "pve_kills", "open_world", 9, T0),
            new LeaderboardInputRow(1, "pvp_kills", "open_world", 50, T0)
        };
        Func<int, int> primaryOf = id => id == 3 ? 1 : id;

        var total = LeaderboardEligibility.Values(Board("pve_kills"), rows, primaryOf).ToDictionary(c => c.UserId);
        Assert.Equal((7m, T0.AddHours(2)), (total[1].Value, total[1].ReachedAt));
        Assert.Equal(9m, total[2].Value);

        var openWorld = LeaderboardEligibility.Values(Board("pve_kills@open_world"), rows, primaryOf).ToDictionary(c => c.UserId);
        Assert.Equal((5m, T0.AddHours(1)), (openWorld[1].Value, openWorld[1].ReachedAt));
    }

    [Fact]
    public void PvpBoards_CountCappedOpenWorldKills_PlusSiegeMatchKills()
    {
        var rows = new[]
        {
            new LeaderboardInputRow(1, "pvp_kills", "open_world", 10, T0),         // uncapped personal count: ignored
            new LeaderboardInputRow(1, "pvp_kills.ranked", "open_world", 6, T0),
            new LeaderboardInputRow(1, "pvp_kills", "siege", 4, T0.AddHours(1))
        };

        Assert.Equal(10m, LeaderboardEligibility.Values(Board("pvp_kills"), rows, id => id).Single().Value);
        Assert.Equal(6m, LeaderboardEligibility.Values(Board("pvp_kills@open_world"), rows, id => id).Single().Value);
        Assert.Equal(4m, LeaderboardEligibility.Values(Board("pvp_kills@siege"), rows, id => id).Single().Value);
    }

    [Fact]
    public void MaxBoards_TakeTheHighest_ReachedAtOfItsEarliestHolder()
    {
        var rows = new[]
        {
            new LeaderboardInputRow(1, "highest_killstreak", "open_world", 5, T0.AddHours(3)),
            new LeaderboardInputRow(1, "highest_killstreak", "siege", 7, T0.AddHours(2)),
            new LeaderboardInputRow(3, "highest_killstreak", "open_world", 7, T0.AddHours(1))
        };

        var overall = LeaderboardEligibility.Values(Board("highest_killstreak"), rows, id => id == 3 ? 1 : id).Single();
        Assert.Equal((7m, T0.AddHours(1)), (overall.Value, overall.ReachedAt));
        Assert.Equal(5m, LeaderboardEligibility.Values(Board("highest_killstreak@open_world"), rows, id => id).Single(c => c.UserId == 1).Value);
    }

    [Fact]
    public void Discoveries_CountEachDomainOnceAcrossIdentities_InThePeriodOfItsFirstDiscovery()
    {
        var discoveries = new[]
        {
            (1, 10, T0.AddDays(-20)), // first found by alice long ago
            (3, 10, T0),             // merged carol found it again: not counted twice nor in the period
            (3, 11, T0.AddHours(1)),
            (2, 10, T0.AddHours(2))
        };
        Func<int, int> primaryOf = id => id == 3 ? 1 : id;

        var lifetime = LeaderboardEligibility.DiscoveryValues(discoveries, primaryOf, null, null).ToDictionary(c => c.UserId);
        Assert.Equal(2m, lifetime[1].Value);
        Assert.Equal(1m, lifetime[2].Value);

        var week = LeaderboardEligibility.DiscoveryValues(discoveries, primaryOf, T0.AddDays(-1), T0.AddDays(1)).ToDictionary(c => c.UserId);
        Assert.Equal((1m, T0.AddHours(1)), (week[1].Value, week[1].ReachedAt));
    }

    // ------------------------------------------------------------------ ranking

    [Fact]
    public void Rank_UsesCompetitionRanks_TiesByEarlierReachedAt_AndSkipsZero()
    {
        var ranked = LeaderboardEligibility.Rank(new[]
        {
            new LeaderboardCandidate(1, 10, T0.AddHours(2)),
            new LeaderboardCandidate(2, 10, T0.AddHours(1)),
            new LeaderboardCandidate(3, 7, T0),
            new LeaderboardCandidate(4, 12, T0),
            new LeaderboardCandidate(5, 0, T0)
        });

        Assert.Equal(new[] { (1, 4), (2, 2), (2, 1), (4, 3) }, ranked.Select(r => (r.Rank, r.UserId)));
    }
}
