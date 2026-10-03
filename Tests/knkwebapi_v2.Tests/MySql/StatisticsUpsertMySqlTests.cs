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
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Statistics;
using knkwebapi_v2.Tests.Services;
using knkwebapi_v2.Tests.Services.Statistics;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// The MySQL-only paths of player statistics (KNG-34 link 2): the AddPlayerStatistics migration
/// applies, the multi-row <c>INSERT … ON DUPLICATE KEY UPDATE</c> upserts (sum, max + ReachedAt,
/// kill pairs), batch idempotency under concurrency, the ledger projector, the visibility row lock,
/// ExecuteUpdate/ExecuteDelete jobs and the query translations. EF InMemory covers the logic
/// (Services/Statistics/*Tests); this covers the SQL.
/// </summary>
[Trait("Category", "requires-mysql")]
public class StatisticsUpsertMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;
    // Whole seconds: datetime(6) keeps microseconds, not .NET ticks, so round-tripped values compare equal.
    private readonly FixedTime _time = new(new DateTime(DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc));
    private readonly StatisticsOptions _options = new() { ProjectionSafetyLagSeconds = 0 };

    public StatisticsUpsertMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private StatisticsIngestionService Ingestion(KnKDbContext ctx) =>
        new(new StatisticsRepository(ctx), Options.Create(_options), new StatisticsMetrics(), NullLogger<StatisticsIngestionService>.Instance, _time);

    private async Task<int[]> UsersAsync(int count)
    {
        await using var ctx = _db.NewContext();
        var users = Enumerable.Range(0, count).Select(_ => new User { Username = "s" + Guid.NewGuid().ToString("N")[..12] }).ToList();
        ctx.Users.AddRange(users);
        await ctx.SaveChangesAsync();
        return users.Select(u => u.Id).ToArray();
    }

    private StatisticsBatchDto Batch(int user, int victim, Guid? id = null, decimal kills = 2, decimal fall = 10) => new()
    {
        BatchId = id ?? Guid.NewGuid(),
        ServerName = "mysql-test",
        Counters = new()
        {
            new() { UserId = user, Metric = "pve_kills", Context = "open_world", Value = kills, OccurredAt = _time.UtcNow.AddMinutes(-2) },
            new() { UserId = user, Metric = "pve_kills", Context = "open_world", Value = 1, OccurredAt = _time.UtcNow.AddMinutes(-1) },
            new() { UserId = user, Metric = "damage_dealt.mob", Context = "open_world", Value = 3.25m, OccurredAt = _time.UtcNow.AddMinutes(-1) }
        },
        Records = new() { new() { UserId = user, Metric = "highest_fall", Value = fall, OccurredAt = _time.UtcNow.AddMinutes(-1) } },
        PvpKills = new() { new() { KillerUserId = user, VictimUserId = victim, Context = "open_world", OccurredAt = _time.UtcNow.AddMinutes(-1) } }
    };

    private async Task<decimal?> TotalAsync(int user, string metric, string context = "")
    {
        await using var ctx = _db.NewContext();
        return (await ctx.PlayerStatTotals.AsNoTracking().SingleOrDefaultAsync(t => t.UserId == user && t.MetricKey == metric && t.ContextKey == context))?.Value;
    }

    [MySqlFact]
    public async Task Upserts_AddSums_KeepMaxima_AndCountKillPairs()
    {
        var (user, victim) = (await UsersAsync(2)) switch { var ids => (ids[0], ids[1]) };

        await using (var ctx = _db.NewContext()) await Ingestion(ctx).IngestAsync(Batch(user, victim, fall: 10));
        await using (var ctx = _db.NewContext()) await Ingestion(ctx).IngestAsync(Batch(user, victim, fall: 7));
        await using (var ctx = _db.NewContext()) await Ingestion(ctx).IngestAsync(Batch(user, victim, fall: 12.5m));

        Assert.Equal(9m, await TotalAsync(user, "pve_kills", "open_world"));
        Assert.Equal(9.75m, await TotalAsync(user, "damage_dealt.mob", "open_world"));
        Assert.Equal(12.5m, await TotalAsync(user, "highest_fall"));
        Assert.Equal(3m, await TotalAsync(user, "pvp_kills", "open_world"));

        await using var check = _db.NewContext();
        var daily = await check.PlayerStatDailies.AsNoTracking().Where(d => d.UserId == user && d.MetricKey == "pve_kills").ToListAsync();
        Assert.Equal(9m, daily.Sum(d => d.Value));
        var pair = await check.PlayerPvpKillPairDailies.AsNoTracking().SingleAsync(p => p.KillerUserId == user);
        Assert.Equal((victim, 3), (pair.VictimUserId, pair.Count));
    }

    [MySqlFact]
    public async Task MaxRecords_MoveReachedAt_OnlyWhenTheValueIncreases()
    {
        var user = (await UsersAsync(1))[0];
        var first = _time.UtcNow.AddMinutes(-30);
        async Task Record(decimal value, DateTime at)
        {
            await using var ctx = _db.NewContext();
            await Ingestion(ctx).IngestAsync(new StatisticsBatchDto
            {
                BatchId = Guid.NewGuid(),
                Records = new() { new() { UserId = user, Metric = "highest_fall", Value = value, OccurredAt = at } }
            });
        }

        await Record(20, first);
        await Record(20, first.AddMinutes(5));   // equal: reached first stays
        await Record(15, first.AddMinutes(10));  // lower: nothing changes

        await using (var ctx = _db.NewContext())
        {
            var total = await ctx.PlayerStatTotals.AsNoTracking().SingleAsync(t => t.UserId == user && t.MetricKey == "highest_fall");
            Assert.Equal((20m, first), (total.Value, total.ReachedAt));
        }

        await Record(25, first.AddMinutes(15));
        await using (var ctx = _db.NewContext())
        {
            var total = await ctx.PlayerStatTotals.AsNoTracking().SingleAsync(t => t.UserId == user && t.MetricKey == "highest_fall");
            Assert.Equal((25m, first.AddMinutes(15)), (total.Value, total.ReachedAt));
        }
    }

    [MySqlFact]
    public async Task ConcurrentReplaysOfOneBatch_ApplyOnce()
    {
        var (user, victim) = (await UsersAsync(2)) switch { var ids => (ids[0], ids[1]) };
        var id = Guid.NewGuid();

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            await using var ctx = _db.NewContext();
            return await Ingestion(ctx).IngestAsync(Batch(user, victim, id));
        }));

        Assert.Single(results, r => !r.Duplicate);
        Assert.Equal(3m, await TotalAsync(user, "pve_kills", "open_world"));
        Assert.Equal(1m, await TotalAsync(user, "pvp_kills", "open_world"));
    }

    [MySqlFact]
    public async Task ConcurrentDifferentBatches_LoseNoUpdates()
    {
        var (user, victim) = (await UsersAsync(2)) switch { var ids => (ids[0], ids[1]) };

        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            await using var ctx = _db.NewContext();
            await Ingestion(ctx).IngestAsync(Batch(user, victim));
        }));

        Assert.Equal(24m, await TotalAsync(user, "pve_kills", "open_world"));
        Assert.Equal(8m, await TotalAsync(user, "pvp_kills", "open_world"));
    }

    [MySqlFact]
    public async Task Sessions_DurationsAndTheTimeoutSweep()
    {
        var user = (await UsersAsync(1))[0];
        var key = Guid.NewGuid();
        await using (var ctx = _db.NewContext())
        {
            var result = await Ingestion(ctx).IngestAsync(new StatisticsBatchDto
            {
                BatchId = Guid.NewGuid(),
                Sessions = new() { new() { Type = "start", SessionKey = key, UserId = user, At = _time.UtcNow.AddHours(-2) } },
                Durations = new() { new() { SessionKey = key, UserId = user, Metric = "active_playtime", From = _time.UtcNow.AddHours(-2), To = _time.UtcNow.AddHours(-1) } }
            });
            Assert.Empty(result.Rejected);
        }
        Assert.Equal(3600m, await TotalAsync(user, "active_playtime"));
        Assert.Equal(1m, await TotalAsync(user, "logins"));

        await using (var ctx = _db.NewContext())
        {
            Assert.True(await new StatisticsRepository(ctx).CloseTimedOutSessionsAsync(_time.UtcNow.AddMinutes(-5)) >= 1);
        }
        await using (var ctx = _db.NewContext())
        {
            var session = await ctx.PlayerStatSessions.AsNoTracking().SingleAsync(s => s.SessionKey == key);
            Assert.Equal((PlayerSessionEndReason.Timeout, _time.UtcNow.AddHours(-1), 3600), (session.EndReason!.Value, session.EndedAt!.Value, session.ActiveSeconds));
            Assert.NotNull(await ctx.PlayerStatProfiles.AsNoTracking().SingleOrDefaultAsync(p => p.UserId == user));
        }
    }

    [MySqlFact]
    public async Task LedgerProjector_ProjectsAndAdvancesTheCursor()
    {
        var user = (await UsersAsync(1))[0];
        await using (var ctx = _db.NewContext())
        {
            var at = _time.UtcNow.AddMinutes(-10);
            // Seeded XP 0 → 3,000 crosses the migrated brackets' Peasant threshold (2,500).
            var grant = CurrencyLedgerSeed.Tx(CurrencyTransactionKind.Grant, CurrencyReasons.SiegeReward, at, (user, Currency.Coins, 40));
            var xp = new CurrencyTransaction
            {
                PublicId = CurrencyIds.NewPublicId(), Kind = CurrencyTransactionKind.Grant, ReasonCode = CurrencyReasons.DiscoveryReward, Reason = "t",
                IdempotencyScope = CurrencyIdempotencyScopes.System, IdempotencyKey = "stat:" + Guid.NewGuid().ToString("N"), RequestHash = new string('0', 64),
                Initiator = CurrencyInitiator.System, InitiatorComponent = "Test", CreatedAt = at,
                Entries =
                {
                    new CurrencyEntry { Currency = Currency.Experience, AccountKind = CurrencyAccountKind.User, UserId = user, Operation = CurrencyOperation.Add, Amount = 3000, BalanceBefore = 0, BalanceAfter = 3000 },
                    new CurrencyEntry { Currency = Currency.Experience, AccountKind = CurrencyAccountKind.System, SystemAccount = "SYS_TEST", Operation = CurrencyOperation.Remove, Amount = -3000 }
                }
            };
            await CurrencyLedgerSeed.AddAsync(ctx, grant, xp);
        }

        var projected = 0;
        for (var i = 0; i < 100; i++)
        {
            await using var ctx = _db.NewContext();
            var count = await new LedgerStatisticsProjector(new StatisticsRepository(ctx), Options.Create(_options), null, null, _time).ProjectNextAsync();
            projected += count;
            if (count == 0) break;
        }

        Assert.True(projected >= 2);
        Assert.Equal(40m, await TotalAsync(user, "coins_earned"));
        Assert.Equal(3000m, await TotalAsync(user, "xp_gained"));
        await using var check = _db.NewContext();
        var change = await check.PlayerTitleChanges.AsNoTracking().SingleAsync(c => c.UserId == user);
        Assert.Equal(TitleChangeDirection.Promotion, change.Direction);
        var cursor = await check.StatisticsProjectionCursors.AsNoTracking().SingleAsync(c => c.Name == LedgerStatisticsProjector.CursorName);
        Assert.Equal(await check.CurrencyEntries.Where(e => e.AccountKind == CurrencyAccountKind.User).MaxAsync(e => e.Id), cursor.LastSourceId);

        // Per-user rebuild on MySQL (ExecuteDelete + re-projection) reproduces the rows.
        await using (var ctx = _db.NewContext())
        {
            await new LedgerStatisticsProjector(new StatisticsRepository(ctx), Options.Create(_options), null, null, _time).RebuildAsync(user);
        }
        Assert.Equal(3000m, await TotalAsync(user, "xp_gained"));
        await using var after = _db.NewContext();
        Assert.Equal(1, await after.PlayerTitleChanges.CountAsync(c => c.UserId == user));
    }

    [MySqlFact]
    public async Task ConcurrentVisibilityUpdates_WithTheSameExpectedValue_OneWins()
    {
        var user = (await UsersAsync(1))[0];
        var results = await Task.WhenAll(new[] { StatisticVisibility.Everyone, StatisticVisibility.Friends, StatisticVisibility.Everyone }.Select(async to =>
        {
            await using var ctx = _db.NewContext();
            var service = new StatisticsVisibilityService(new StatisticsRepository(ctx),
                new CurrencyService(new CurrencyRepository(ctx), new UserRepository(ctx), NullLogger<CurrencyService>.Instance), _time);
            try
            {
                await service.UpdateAsync(user, new StatisticsVisibilityUpdateDto
                {
                    Changes = new() { new() { SettingKey = "logins", Context = "", Expected = StatisticVisibility.Nobody, Visibility = to } }
                });
                return true;
            }
            catch (StatisticsVisibilityConflictException)
            {
                return false;
            }
        }));

        Assert.Single(results, r => r);
        await using var check = _db.NewContext();
        Assert.Single(await check.PlayerStatVisibilities.AsNoTracking().Where(v => v.UserId == user).ToListAsync());
    }

    [MySqlFact]
    public async Task Queries_Retention_AndSiegeLookups_TranslateOnMySql()
    {
        var (user, victim) = (await UsersAsync(2)) switch { var ids => (ids[0], ids[1]) };
        await using (var ctx = _db.NewContext()) await Ingestion(ctx).IngestAsync(Batch(user, victim));

        await using (var ctx = _db.NewContext())
        {
            var query = new StatisticsQueryService(new StatisticsRepository(ctx),
                new CurrencyService(new CurrencyRepository(ctx), new UserRepository(ctx), NullLogger<CurrencyService>.Instance),
                new TitleService(new TitleBracketRepository(ctx)), new DiscoveryRepository(ctx), Options.Create(_options), _time);
            var self = new StatisticsViewer(StatisticsViewerKind.Self, user);
            var lifetime = await query.GetAsync(user, self, null, null);
            Assert.Equal(3m, lifetime!.Metrics.Single(m => m.Key == "pve_kills").RawValue);
            var week = await query.GetAsync(user, self, "week", null);
            Assert.NotNull(week);
            var series = await query.GetSeriesAsync(user, self, "pve_kills", "open_world", "day", null, null);
            Assert.Equal(3m, series!.Points.Sum(p => p.Value));
            Assert.Equal(0, (await query.GetTitleHistoryAsync(user, self, 1, 20))!.TotalCount);

            var repo = new StatisticsRepository(ctx);
            Assert.NotNull(await repo.GetUnprojectedSiegeMatchesAsync(5));
            Assert.Empty(await repo.GetProjectedSiegeMatchesOfUserAsync(user));
        }

        await using (var ctx = _db.NewContext())
        {
            var repo = new StatisticsRepository(ctx);
            var tomorrow = DateOnly.FromDateTime(_time.UtcNow).AddDays(2);
            Assert.True(await repo.PurgeDailyBeforeAsync(tomorrow) >= 1);
            Assert.True(await repo.PurgeKillPairsBeforeAsync(tomorrow) >= 1);
            Assert.True(await repo.PurgeBatchesBeforeAsync(_time.UtcNow.AddDays(1)) >= 1);
            await repo.PurgeSessionsStartedBeforeAsync(_time.UtcNow.AddDays(-365));
        }
        Assert.Equal(3m, await TotalAsync(user, "pve_kills", "open_world")); // totals survive retention
    }
}
