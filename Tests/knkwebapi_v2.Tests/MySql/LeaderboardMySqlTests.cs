using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Xunit;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.Leaderboards;
using knkwebapi_v2.Services.Statistics;
using knkwebapi_v2.Tests.Services.Statistics;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// The MySQL paths of leaderboards (KNG-34 link 5): the AddLeaderboards migration applies, the
/// daily GROUP BY aggregation, snapshot replacement in a transaction with ExecuteDelete of the
/// superseded rows, and the repeat-victim cap lookup of stored kill pairs. Logic is covered on EF
/// InMemory (Services/Leaderboards/*Tests). The database is shared, so assertions look at this
/// test's own users only.
/// </summary>
[Trait("Category", "requires-mysql")]
public class LeaderboardMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;
    private readonly FixedTime _time = new(new DateTime(DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc));
    private readonly StatisticsOptions _options = new();

    public LeaderboardMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private async Task<int[]> UsersAsync(int count)
    {
        await using var ctx = _db.NewContext();
        var users = Enumerable.Range(0, count).Select(_ => new User { Username = "l" + Guid.NewGuid().ToString("N")[..12] }).ToList();
        ctx.Users.AddRange(users);
        await ctx.SaveChangesAsync();
        return users.Select(u => u.Id).ToArray();
    }

    private LeaderboardSnapshotBuilder Builder(KnKDbContext ctx) =>
        new(new LeaderboardRepository(ctx), Options.Create(new LeaderboardsOptions()), Options.Create(_options), _time);

    private static async Task<LeaderboardSnapshotEntry?> EntryAsync(KnKDbContext ctx, string board, LeaderboardPeriod period, int userId)
    {
        var snapshot = await new LeaderboardRepository(ctx).GetCurrentAsync(board, period);
        return snapshot == null ? null : await new LeaderboardRepository(ctx).GetEntryAsync(snapshot.Id, userId);
    }

    [MySqlFact]
    public async Task Build_AggregatesDailyRows_AndReplacesTheCurrentSnapshot()
    {
        var ids = await UsersAsync(2);
        var zone = StatisticsPeriods.FindZone(_options.TimeZone);
        var today = StatisticsPeriods.LocalDay(_time.UtcNow, zone);
        await using (var ctx = _db.NewContext())
        {
            foreach (var (id, seconds) in new[] { (ids[0], 9_000_000m), (ids[1], 8_000_000m) })
            {
                ctx.PlayerStatTotals.Add(new PlayerStatTotal { UserId = id, MetricKey = "active_playtime", ContextKey = "", Value = seconds, ReachedAt = _time.UtcNow, UpdatedAt = _time.UtcNow });
                ctx.PlayerStatDailies.Add(new PlayerStatDaily { UserId = id, Day = today, MetricKey = "active_playtime", ContextKey = "", Value = seconds / 1000, UpdatedAt = _time.UtcNow });
            }
            // A second record row this week: the weekly max wins.
            ctx.PlayerStatDailies.Add(new PlayerStatDaily { UserId = ids[0], Day = today, MetricKey = "highest_killstreak", ContextKey = "open_world", Value = 4, UpdatedAt = _time.UtcNow });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext()) await Builder(ctx).BuildAllAsync();
        await using (var ctx = _db.NewContext()) await Builder(ctx).BuildAllAsync();

        await using var check = _db.NewContext();
        var first = await EntryAsync(check, "active_playtime", LeaderboardPeriod.Lifetime, ids[0]);
        var second = await EntryAsync(check, "active_playtime", LeaderboardPeriod.Lifetime, ids[1]);
        Assert.Equal(9_000_000m, first!.Value);
        Assert.True(first.Rank < second!.Rank);
        Assert.Equal(9_000m, (await EntryAsync(check, "active_playtime", LeaderboardPeriod.Weekly, ids[0]))!.Value);
        Assert.Equal(1, await check.LeaderboardSnapshots.CountAsync(s => s.BoardKey == "active_playtime" && s.Period == LeaderboardPeriod.Lifetime));
        var currentIds = await check.LeaderboardSnapshots.Select(s => s.Id).ToListAsync();
        Assert.False(await check.LeaderboardSnapshotEntries.AnyAsync(e => !currentIds.Contains(e.SnapshotId)));
    }

    [MySqlFact]
    public async Task Ingestion_CapsRankedKillsAgainstStoredPairs()
    {
        var ids = await UsersAsync(2);
        StatisticsBatchDto Kills(int count) => new()
        {
            BatchId = Guid.NewGuid(),
            PvpKills = Enumerable.Range(0, count).Select(_ => new StatisticsPvpKillEntryDto
            {
                KillerUserId = ids[0], VictimUserId = ids[1], Context = "open_world", OccurredAt = _time.UtcNow.AddMinutes(-1)
            }).ToList()
        };

        foreach (var batch in new[] { Kills(2), Kills(2), Kills(1) })
        {
            await using var ctx = _db.NewContext();
            await new StatisticsIngestionService(new StatisticsRepository(ctx), Options.Create(_options), null,
                NullLogger<StatisticsIngestionService>.Instance, _time).IngestAsync(batch);
        }

        await using var check = _db.NewContext();
        decimal Total(string metric) => check.PlayerStatTotals.AsNoTracking()
            .Single(t => t.UserId == ids[0] && t.MetricKey == metric && t.ContextKey == "open_world").Value;
        Assert.Equal(5m, Total("pvp_kills"));
        Assert.Equal(3m, Total("pvp_kills.ranked"));
    }
}
