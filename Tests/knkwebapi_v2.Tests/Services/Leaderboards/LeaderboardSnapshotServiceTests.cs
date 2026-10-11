using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Leaderboards;
using knkwebapi_v2.Tests.Services.Statistics;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Leaderboards;

/// <summary>
/// Snapshot building end to end on EF InMemory (link 5 acceptance criterion 1): periods from daily
/// rows vs. lifetime from totals, eligibility, owner exclusions, inactive and merged accounts,
/// competition ranks, atomic replacement with closed periods kept, retention, and the repeat-victim
/// cap maintained at ingestion. "Now" is Saturday 2026-10-03 (week from Monday 09-28, month from 10-01).
/// </summary>
public class LeaderboardSnapshotServiceTests : IDisposable
{
    private readonly StatisticsTestDb _db = new();

    public LeaderboardSnapshotServiceTests()
    {
        var ctx = _db.Context;
        ctx.Users.AddRange(
            new User { Id = 4, Username = "dave", Uuid = "uuid-d" },
            new User { Id = 5, Username = "erin", Uuid = "uuid-e", IsActive = false });
        // carol (3) was merged into alice (1).
        ctx.Users.Single(u => u.Id == 3).IsActive = false;
        var forfeit = CurrencyLedgerSeed.Tx(CurrencyTransactionKind.Merge, CurrencyReasons.MergeForfeit, new DateTime(2026, 9, 20, 0, 0, 0, DateTimeKind.Utc),
            (3, Currency.Coins, -10));
        forfeit.SourceType = "User";
        forfeit.SourceRef = "1";
        ctx.CurrencyTransactions.Add(forfeit);

        // Lifetime totals.
        Total(1, "active_playtime", "", 3600, 1);
        Total(3, "active_playtime", "", 600, 2);
        Total(2, "active_playtime", "", 5000, 3);
        Total(4, "active_playtime", "", 4200, 1); // ties with alice + carol (4200, reached at 2:00) but earlier → listed first
        Total(5, "active_playtime", "", 99999, 1); // inactive, not merged: never ranks
        Total(1, "distance.foot", "", 500, 1);
        Total(2, "distance.foot", "", 900, 1);

        // Daily rows: this week (from Monday 09-28) and this month (from 10-01).
        Day(1, new DateOnly(2026, 9, 29), "active_playtime", "", 100);
        Day(2, new DateOnly(2026, 9, 27), "active_playtime", "", 50);
        Day(2, new DateOnly(2026, 10, 2), "active_playtime", "", 40);
        Day(4, new DateOnly(2026, 9, 15), "active_playtime", "", 70);
        ctx.SaveChanges();

        // Only bob shows his foot distance.
        ctx.PlayerStatVisibilities.Add(new PlayerStatVisibility { UserId = 2, SettingKey = "distance.foot", ContextKey = "", Visibility = StatisticVisibility.Everyone });
        ctx.PlayerStatVisibilities.Add(new PlayerStatVisibility { UserId = 1, SettingKey = "distance.foot", ContextKey = "", Visibility = StatisticVisibility.Friends });
        ctx.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private void Total(int user, string metric, string context, decimal value, int reachedHour) =>
        _db.Context.PlayerStatTotals.Add(new PlayerStatTotal
        {
            UserId = user, MetricKey = metric, ContextKey = context, Value = value,
            ReachedAt = new DateTime(2026, 10, 1, reachedHour, 0, 0, DateTimeKind.Utc)
        });

    private void Day(int user, DateOnly day, string metric, string context, decimal value) =>
        _db.Context.PlayerStatDailies.Add(new PlayerStatDaily
        {
            UserId = user, Day = day, MetricKey = metric, ContextKey = context, Value = value,
            UpdatedAt = day.ToDateTime(new TimeOnly(12, 0), DateTimeKind.Utc)
        });

    private async Task<LeaderboardViewDto> View(string board, LeaderboardPeriod period, int? viewer = null) =>
        await _db.LeaderboardQuery().GetBoardAsync(LeaderboardCatalog.Find(board)!, period, 10, viewer);

    private static (int Rank, string Name, decimal Value)[] Rows(LeaderboardViewDto view) =>
        view.Entries.Select(e => (e.Rank, e.Username, e.RawValue)).ToArray();

    [Fact]
    public async Task Build_WritesOneCurrentSnapshotPerBoardAndPeriod()
    {
        var written = await _db.LeaderboardBuilder().BuildAllAsync();

        Assert.Equal(LeaderboardCatalog.Boards.Count * 3, written);
        Assert.Equal(written, _db.Context.LeaderboardSnapshots.Count(s => s.IsCurrent));
        Assert.Equal(new DateOnly(2026, 9, 28), _db.Context.LeaderboardSnapshots.First(s => s.Period == LeaderboardPeriod.Weekly).PeriodStart);
        Assert.Equal(new DateOnly(2026, 10, 1), _db.Context.LeaderboardSnapshots.First(s => s.Period == LeaderboardPeriod.Monthly).PeriodStart);
        Assert.Null(_db.Context.LeaderboardSnapshots.First(s => s.Period == LeaderboardPeriod.Lifetime).PeriodStart);
    }

    [Fact]
    public async Task Lifetime_MergesIdentities_SkipsInactiveAccounts_AndRanksTiesByReachedAt()
    {
        await _db.LeaderboardBuilder().BuildAllAsync();

        var view = await View("active_playtime", LeaderboardPeriod.Lifetime, viewer: 1);

        // alice 3600 + merged carol 600 (carol herself isn't listed); erin (inactive) is not listed; dave
        // reached 4200 before alice: same rank, listed first.
        Assert.Equal(new[] { (1, "bob", 5000m), (2, "dave", 4200m), (2, "alice", 4200m) }, Rows(view));
        Assert.Equal(3, view.TotalRanked);
        Assert.Equal((2, 4200m), (view.Viewer!.Rank, view.Viewer.RawValue));
    }

    [Fact]
    public async Task Periods_ReadTheDailyRowsOfTheCurrentWeekAndMonth()
    {
        await _db.LeaderboardBuilder().BuildAllAsync();

        Assert.Equal(new[] { (1, "alice", 100m), (2, "bob", 40m) }, Rows(await View("active_playtime", LeaderboardPeriod.Weekly)));
        Assert.Equal(new[] { (1, "bob", 40m) }, Rows(await View("active_playtime", LeaderboardPeriod.Monthly)));
    }

    [Fact]
    public async Task ConfigurableBoards_RankOnlyPlayersWhoChoseEveryone()
    {
        await _db.LeaderboardBuilder().BuildAllAsync();

        var view = await View("distance.foot", LeaderboardPeriod.Lifetime, viewer: 1);

        Assert.Equal(new[] { (1, "bob", 900m) }, Rows(view)); // alice chose Friends (fails closed)
        Assert.Null(view.Viewer);
    }

    [Fact]
    public async Task OwnerExclusions_ApplyFromTheNextRefresh_AndCanBeLifted()
    {
        var query = _db.LeaderboardQuery();
        Assert.True(await query.ExcludeAsync(4, "  alt farming  ", 1));
        Assert.False(await query.ExcludeAsync(999, null, 1));
        await _db.LeaderboardBuilder().BuildAllAsync();

        Assert.DoesNotContain("dave", Rows(await View("active_playtime", LeaderboardPeriod.Lifetime)).Select(r => r.Name));
        var exclusion = Assert.Single(await query.GetExclusionsAsync());
        Assert.Equal((4, "dave", "alt farming", (int?)1), (exclusion.UserId, exclusion.Username, exclusion.Reason, exclusion.ExcludedByUserId));

        Assert.True(await query.IncludeAsync(4));
        await _db.LeaderboardBuilder().BuildAllAsync();
        Assert.Contains("dave", Rows(await View("active_playtime", LeaderboardPeriod.Lifetime)).Select(r => r.Name));
        Assert.Empty(await query.GetExclusionsAsync());
    }

    [Fact]
    public async Task Refresh_ReplacesTheCurrentSnapshot_KeepsTheFinalOneOfAClosedWeek_AndPurgesOldOnes()
    {
        await _db.LeaderboardBuilder().BuildAllAsync();
        var firstWeekly = _db.Context.LeaderboardSnapshots.AsNoTracking()
            .Single(s => s.BoardKey == "active_playtime" && s.Period == LeaderboardPeriod.Weekly).Id;

        await _db.LeaderboardBuilder().BuildAllAsync(); // same week: superseded snapshot deleted with its entries
        Assert.Equal(1, _db.Context.LeaderboardSnapshots.Count(s => s.BoardKey == "active_playtime" && s.Period == LeaderboardPeriod.Weekly));
        Assert.Empty(_db.Context.LeaderboardSnapshotEntries.Where(e => e.SnapshotId == firstWeekly));

        _db.Time.UtcNow = StatisticsTestDb.Now.AddDays(3); // Tuesday of the next week
        await _db.LeaderboardBuilder().BuildAllAsync();
        var weekly = _db.Context.LeaderboardSnapshots.AsNoTracking()
            .Where(s => s.BoardKey == "active_playtime" && s.Period == LeaderboardPeriod.Weekly).OrderBy(s => s.Id).ToList();
        Assert.Equal(new[] { (new DateOnly(2026, 9, 28), false), (new DateOnly(2026, 10, 5), true) },
            weekly.Select(s => (s.PeriodStart!.Value, s.IsCurrent)));
        Assert.Empty(Rows(await View("active_playtime", LeaderboardPeriod.Weekly)));

        _db.Time.UtcNow = StatisticsTestDb.Now.AddDays(500);
        await _db.LeaderboardBuilder(new LeaderboardsOptions { SnapshotRetentionDays = 400 }).BuildAllAsync();
        Assert.All(_db.Context.LeaderboardSnapshots.AsNoTracking().ToList(), s => Assert.True(s.IsCurrent));
    }

    [Fact]
    public async Task MaxEntriesPerBoard_CapsStoredRows_NotTheRankedCount()
    {
        await _db.LeaderboardBuilder(new LeaderboardsOptions { MaxEntriesPerBoard = 2 }).BuildAllAsync();

        var view = await View("active_playtime", LeaderboardPeriod.Lifetime, viewer: 1);
        Assert.Equal(2, view.Entries.Count);
        Assert.Equal(3, view.TotalRanked);
        Assert.Null(view.Viewer); // alice is ranked but beyond the stored rows
    }

    [Fact]
    public async Task UnknownSnapshot_IsAnEmptyView()
    {
        var view = await View("gate_damage", LeaderboardPeriod.Monthly);

        Assert.Null(view.GeneratedAt);
        Assert.Empty(view.Entries);
        Assert.Equal("monthly", view.Period);
    }

    [Fact]
    public async Task RepeatVictimCap_LimitsLeaderboardKills_PerVictimAndDay_AcrossBatches()
    {
        var at = StatisticsTestDb.Now.AddHours(-1);
        StatisticsBatchDto Kills(int count, int victim) => new()
        {
            BatchId = Guid.NewGuid(),
            PvpKills = Enumerable.Range(0, count).Select(i => new StatisticsPvpKillEntryDto
            {
                KillerUserId = 1, VictimUserId = victim, Context = "open_world", OccurredAt = at.AddSeconds(i)
            }).ToList()
        };

        await _db.Ingestion().IngestAsync(Kills(2, victim: 2));
        await _db.Ingestion().IngestAsync(Kills(4, victim: 2)); // 6 kills of bob today: 3 rank
        await _db.Ingestion().IngestAsync(Kills(1, victim: 4));

        Assert.Equal(7m, _db.Total(1, "pvp_kills", "open_world"));
        Assert.Equal(4m, _db.Total(1, "pvp_kills.ranked", "open_world"));

        _db.Context.PlayerStatVisibilities.Add(new PlayerStatVisibility { UserId = 1, SettingKey = "pvp_kills", ContextKey = "", Visibility = StatisticVisibility.Everyone });
        await _db.Context.SaveChangesAsync();
        await _db.LeaderboardBuilder().BuildAllAsync();

        Assert.Equal(new[] { (1, "alice", 4m) }, Rows(await View("pvp_kills", LeaderboardPeriod.Lifetime, viewer: 2)));
        Assert.Equal(new[] { (1, "alice", 4m) }, Rows(await View("pvp_kills@open_world", LeaderboardPeriod.Weekly, viewer: 2)));

        // Personal statistics still count every kill.
        var self = await _db.Query().GetAsync(1, new knkwebapi_v2.Services.Statistics.StatisticsViewer(
            knkwebapi_v2.Services.Statistics.StatisticsViewerKind.Self, 1), null, null);
        Assert.Equal(7m, self!.Metrics.Single(m => m.Key == "pvp_kills").Value);
        Assert.DoesNotContain(self.Metrics, m => m.Key == "pvp_kills.ranked");
    }

    [Fact]
    public async Task RepeatVictimCap_Zero_CountsEveryKill()
    {
        var ingestion = new knkwebapi_v2.Services.Statistics.StatisticsIngestionService(_db.Repository(),
            Microsoft.Extensions.Options.Options.Create(_db.Options), null, null, _db.Time,
            Microsoft.Extensions.Options.Options.Create(new LeaderboardsOptions { RepeatVictimDailyCap = 0 }));
        await ingestion.IngestAsync(new StatisticsBatchDto
        {
            BatchId = Guid.NewGuid(),
            PvpKills = Enumerable.Range(0, 5).Select(_ => new StatisticsPvpKillEntryDto
            {
                KillerUserId = 1, VictimUserId = 2, Context = "open_world", OccurredAt = StatisticsTestDb.Now
            }).ToList()
        });

        Assert.Equal(5m, _db.Total(1, "pvp_kills.ranked", "open_world"));
    }
}
