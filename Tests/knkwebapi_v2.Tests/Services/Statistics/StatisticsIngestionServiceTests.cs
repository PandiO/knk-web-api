using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Statistics;

/// <summary>POST api/statistics/batches semantics (IMPLEMENTATION_PLAN.md §3.1; link 2 acceptance
/// criterion 2) on EF InMemory. The MySQL upsert path: MySql/StatisticsUpsertMySqlTests.</summary>
public class StatisticsIngestionServiceTests : IDisposable
{
    private readonly StatisticsTestDb _db = new();

    public void Dispose() => _db.Dispose();

    private static DateTime Utc(int d, int h, int min = 0) => new(2026, 10, d, h, min, 0, DateTimeKind.Utc);

    private static readonly Guid Session = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static StatisticsBatchDto MixedBatch(Guid? batchId = null) => new()
    {
        BatchId = batchId ?? Guid.NewGuid(),
        ServerName = "survival",
        PluginVersion = "test",
        SentAt = StatisticsTestDb.Now,
        Sessions = new()
        {
            new() { Type = "start", SessionKey = Session, UserId = 1, At = Utc(2, 21) },
            new() { Type = "end", SessionKey = Session, UserId = 1, At = Utc(2, 22, 40), EndReason = "Quit" }
        },
        Durations = new()
        {
            // 23:00 → 00:30 Amsterdam: 3,600 s on Oct 2, 1,800 s on Oct 3.
            new() { SessionKey = Session, UserId = 1, Metric = "active_playtime", From = Utc(2, 21), To = Utc(2, 22, 30) },
            new() { SessionKey = Session, UserId = 1, Metric = "afk_time", From = Utc(2, 22, 30), To = Utc(2, 22, 40) }
        },
        Counters = new()
        {
            new() { UserId = 1, Metric = "pve_kills", Context = "open_world", Value = 3, OccurredAt = Utc(3, 10) },
            new() { UserId = 1, Metric = "pve_kills", Context = "open_world", Value = 2, OccurredAt = Utc(3, 11) },
            new() { UserId = 1, Metric = "distance.foot", Context = "", Value = 120.5m, OccurredAt = Utc(3, 10) }
        },
        Records = new()
        {
            new() { UserId = 1, Metric = "highest_fall", Value = 12.3m, OccurredAt = Utc(3, 9) },
            new() { UserId = 1, Metric = "highest_fall", Value = 15m, OccurredAt = Utc(3, 10) },
            new() { UserId = 1, Metric = "highest_killstreak", Context = "open_world", Value = 4, OccurredAt = Utc(3, 10) }
        },
        PvpKills = new()
        {
            new() { KillerUserId = 1, VictimUserId = 2, Context = "open_world", OccurredAt = Utc(3, 10) },
            new() { KillerUserId = 1, VictimUserId = 2, Context = "open_world", OccurredAt = Utc(3, 10, 5) }
        }
    };

    [Fact]
    public async Task MixedBatch_AppliesEverySection()
    {
        var result = await _db.Ingestion().IngestAsync(MixedBatch());

        Assert.False(result.Duplicate);
        Assert.Empty(result.Rejected);
        Assert.Equal(12, result.Accepted);

        var session = _db.Context.PlayerStatSessions.AsNoTracking().Single();
        Assert.Equal((1, Utc(2, 21), Utc(2, 22, 40), PlayerSessionEndReason.Quit), (session.UserId, session.StartedAt, session.EndedAt, session.EndReason));
        Assert.Equal((5400, 600), (session.ActiveSeconds, session.AfkSeconds));
        Assert.Equal("survival", session.ServerName);

        var active = _db.Daily(1, "active_playtime");
        Assert.Equal(new[] { (new DateOnly(2026, 10, 2), 3600m), (new DateOnly(2026, 10, 3), 1800m) }, active.Select(d => (d.Day, d.Value)));
        Assert.Equal(5400m, _db.Total(1, "active_playtime"));
        Assert.Equal(600m, _db.Total(1, "afk_time"));

        // Login on the start's local day (23:00 Amsterdam on Oct 2); first session recorded.
        Assert.Equal(new DateOnly(2026, 10, 2), _db.Daily(1, "logins").Single().Day);
        Assert.Equal(1m, _db.Total(1, "logins"));
        Assert.Equal(Utc(2, 21), _db.Context.PlayerStatProfiles.AsNoTracking().Single(p => p.UserId == 1).FirstSessionAt);

        Assert.Equal(5m, _db.Total(1, "pve_kills", "open_world"));
        Assert.Equal(120.5m, _db.Total(1, "distance.foot"));
        Assert.Equal(15m, _db.Total(1, "highest_fall")); // max, not sum
        Assert.Equal(4m, _db.Total(1, "highest_killstreak", "open_world"));

        Assert.Equal(2m, _db.Total(1, "pvp_kills", "open_world"));
        var pair = _db.Context.PlayerPvpKillPairDailies.AsNoTracking().Single();
        Assert.Equal((1, 2, new DateOnly(2026, 10, 3), "open_world", 2), (pair.KillerUserId, pair.VictimUserId, pair.Day, pair.ContextKey, pair.Count));

        var batch = _db.Context.PlayerStatBatches.AsNoTracking().Single();
        Assert.Equal((12, 0, "survival"), (batch.EntryCount, batch.RejectedCount, batch.ServerName));
    }

    [Fact]
    public async Task ReplayingTheSameBatchId_ChangesNothing()
    {
        var id = Guid.NewGuid();
        await _db.Ingestion().IngestAsync(MixedBatch(id));

        var replay = await _db.Ingestion().IngestAsync(MixedBatch(id));

        Assert.True(replay.Duplicate);
        Assert.Equal(0, replay.Accepted);
        Assert.Equal(5m, _db.Total(1, "pve_kills", "open_world"));
        Assert.Equal(5400m, _db.Total(1, "active_playtime"));
        Assert.Equal(1m, _db.Total(1, "logins"));
        Assert.Single(_db.Context.PlayerStatBatches.AsNoTracking());
    }

    [Fact]
    public async Task ASecondBatch_AddsSumsAndKeepsMaxima()
    {
        await _db.Ingestion().IngestAsync(MixedBatch());
        var second = new StatisticsBatchDto
        {
            BatchId = Guid.NewGuid(),
            Counters = new() { new() { UserId = 1, Metric = "pve_kills", Context = "open_world", Value = 1, OccurredAt = Utc(3, 11) } },
            Records = new()
            {
                new() { UserId = 1, Metric = "highest_fall", Value = 9m, OccurredAt = Utc(3, 11) },
                new() { UserId = 1, Metric = "highest_killstreak", Context = "open_world", Value = 7, OccurredAt = Utc(3, 11) }
            }
        };

        await _db.Ingestion().IngestAsync(second);

        Assert.Equal(6m, _db.Total(1, "pve_kills", "open_world"));
        Assert.Equal(15m, _db.Total(1, "highest_fall"));
        Assert.Equal(7m, _db.Total(1, "highest_killstreak", "open_world"));
        var streakTotal = _db.Context.PlayerStatTotals.AsNoTracking().Single(t => t.MetricKey == "highest_killstreak");
        Assert.Equal(Utc(3, 11), streakTotal.ReachedAt);
    }

    [Fact]
    public async Task InvalidEntries_AreRejectedOneByOne_AndTheRestApplies()
    {
        var batch = new StatisticsBatchDto
        {
            BatchId = Guid.NewGuid(),
            Sessions = new()
            {
                new() { Type = "pause", SessionKey = Guid.NewGuid(), UserId = 1, At = Utc(3, 10) },               // 0 InvalidEntry
                new() { Type = "end", SessionKey = Guid.NewGuid(), UserId = 1, At = Utc(3, 10) },                 // 1 UnknownSession
                new() { Type = "start", SessionKey = Guid.NewGuid(), UserId = 99, At = Utc(3, 10) },              // 2 UnknownUser
            },
            Durations = new()
            {
                new() { SessionKey = Guid.NewGuid(), UserId = 1, Metric = "active_playtime", From = Utc(3, 9), To = Utc(3, 10) }, // 0 UnknownSession
                new() { SessionKey = Guid.NewGuid(), UserId = 1, Metric = "active_playtime", From = Utc(3, 10), To = Utc(3, 9) }, // 1 InvalidInterval
                new() { SessionKey = Guid.NewGuid(), UserId = 1, Metric = "pve_kills", From = Utc(3, 9), To = Utc(3, 10) },       // 2 UnknownMetric
            },
            Counters = new()
            {
                new() { UserId = 1, Metric = "no_such_metric", Value = 1, OccurredAt = Utc(3, 10) },                              // 0 UnknownMetric
                new() { UserId = 1, Metric = "pvp_kills", Context = "siege", Value = 1, OccurredAt = Utc(3, 10) },                // 1 NotPluginWritable
                new() { UserId = 1, Metric = "deaths", Context = "siege", Value = 1, OccurredAt = Utc(3, 10) },                   // 2 NotPluginWritable (Siege-owned)
                new() { UserId = 1, Metric = "xp_gained", Value = 1, OccurredAt = Utc(3, 10) },                                  // 3 NotPluginWritable (ledger)
                new() { UserId = 1, Metric = "pve_kills", Context = "Open World", Value = 1, OccurredAt = Utc(3, 10) },           // 4 InvalidContext
                new() { UserId = 1, Metric = "distance.foot", Context = "siege", Value = 1, OccurredAt = Utc(3, 10) },            // 5 InvalidContext (not contextual)
                new() { UserId = 1, Metric = "pve_kills", Context = "open_world", Value = 10_001, OccurredAt = Utc(3, 10) },      // 6 OutOfRange
                new() { UserId = 1, Metric = "pve_kills", Context = "open_world", Value = -1, OccurredAt = Utc(3, 10) },          // 7 OutOfRange
                new() { UserId = 1, Metric = "pve_kills", Context = "open_world", Value = 1, OccurredAt = Utc(3, 10).AddDays(-8) }, // 8 TooOld
                new() { UserId = 1, Metric = "pve_kills", Context = "open_world", Value = 1, OccurredAt = Utc(3, 13) },           // 9 InFuture
                new() { UserId = 1, Metric = "highest_fall", Value = 3, OccurredAt = Utc(3, 10) },                               // 10 NotPluginWritable (a record)
                new() { UserId = 1, Metric = "pve_kills", Context = "arena", Value = 2, OccurredAt = Utc(3, 10) },                // 11 accepted (future context)
            },
            Records = new()
            {
                new() { UserId = 1, Metric = "highest_killstreak", Context = "siege", Value = 9, OccurredAt = Utc(3, 10) },       // 0 NotPluginWritable
                new() { UserId = 1, Metric = "pve_kills", Context = "open_world", Value = 9, OccurredAt = Utc(3, 10) },           // 1 NotPluginWritable (a counter)
            },
            PvpKills = new()
            {
                new() { KillerUserId = 1, VictimUserId = 1, Context = "open_world", OccurredAt = Utc(3, 10) },                    // 0 InvalidEntry
                new() { KillerUserId = 1, VictimUserId = 2, Context = "siege", OccurredAt = Utc(3, 10) },                         // 1 NotPluginWritable
                new() { KillerUserId = 1, VictimUserId = 42, Context = "open_world", OccurredAt = Utc(3, 10) },                   // 2 UnknownUser
            }
        };

        var result = await _db.Ingestion().IngestAsync(batch);

        Assert.Equal(new[]
        {
            ("sessions", 0, "InvalidEntry"), ("sessions", 1, "UnknownSession"), ("sessions", 2, "UnknownUser"),
            ("durations", 0, "UnknownSession"), ("durations", 1, "InvalidInterval"), ("durations", 2, "UnknownMetric"),
            ("counters", 0, "UnknownMetric"), ("counters", 1, "NotPluginWritable"), ("counters", 2, "NotPluginWritable"),
            ("counters", 3, "NotPluginWritable"), ("counters", 4, "InvalidContext"), ("counters", 5, "InvalidContext"),
            ("counters", 6, "OutOfRange"), ("counters", 7, "OutOfRange"), ("counters", 8, "TooOld"), ("counters", 9, "InFuture"),
            ("counters", 10, "NotPluginWritable"),
            ("records", 0, "NotPluginWritable"), ("records", 1, "NotPluginWritable"),
            ("pvpKills", 0, "InvalidEntry"), ("pvpKills", 1, "NotPluginWritable"), ("pvpKills", 2, "UnknownUser"),
        }, result.Rejected.Select(r => (r.Section, r.Index, r.Code)));
        Assert.Equal(1, result.Accepted);
        Assert.Equal(2m, _db.Total(1, "pve_kills", "arena"));
        Assert.Null(_db.Total(1, "pvp_kills", "siege"));
        Assert.Null(_db.Total(1, "deaths", "siege"));
        Assert.Empty(_db.Context.PlayerStatSessions.AsNoTracking());
        Assert.Equal(22, _db.Context.PlayerStatBatches.AsNoTracking().Single().RejectedCount);
    }

    [Fact]
    public async Task TooManyEntries_IsAStructuralError()
    {
        using var db = new StatisticsTestDb(new StatisticsOptions { MaxBatchEntries = 2 });
        var batch = new StatisticsBatchDto
        {
            BatchId = Guid.NewGuid(),
            Counters = Enumerable.Range(0, 3).Select(_ => new StatisticsValueEntryDto
            {
                UserId = 1, Metric = "pve_kills", Context = "open_world", Value = 1, OccurredAt = Utc(3, 10)
            }).ToList()
        };

        await Assert.ThrowsAsync<ArgumentException>(() => db.Ingestion().IngestAsync(batch));
        await Assert.ThrowsAsync<ArgumentException>(() => db.Ingestion().IngestAsync(new StatisticsBatchDto()));
        Assert.Empty(db.Context.PlayerStatBatches.AsNoTracking());
    }

    [Fact]
    public async Task Reconnects_AreNewSessionsAndLogins_FirstSessionIsTheEarliest()
    {
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await _db.Ingestion().IngestAsync(new StatisticsBatchDto
        {
            BatchId = Guid.NewGuid(),
            Sessions = new() { new() { Type = "start", SessionKey = second, UserId = 2, At = Utc(3, 9) } }
        });
        await _db.Ingestion().IngestAsync(new StatisticsBatchDto
        {
            BatchId = Guid.NewGuid(),
            Sessions = new()
            {
                // A late (spooled) earlier session, and a repeated start of the known one.
                new() { Type = "start", SessionKey = first, UserId = 2, At = Utc(3, 8) },
                new() { Type = "start", SessionKey = second, UserId = 2, At = Utc(3, 9) }
            }
        });

        Assert.Equal(2, _db.Context.PlayerStatSessions.AsNoTracking().Count());
        Assert.Equal(2m, _db.Total(2, "logins"));
        Assert.Equal(Utc(3, 8), _db.Context.PlayerStatProfiles.AsNoTracking().Single(p => p.UserId == 2).FirstSessionAt);
    }

    [Fact]
    public async Task ADurationAfterATimeout_ReopensTheSession()
    {
        var key = Guid.NewGuid();
        await _db.Ingestion().IngestAsync(new StatisticsBatchDto
        {
            BatchId = Guid.NewGuid(),
            Sessions = new() { new() { Type = "start", SessionKey = key, UserId = 1, At = Utc(3, 9) } }
        });
        await _db.Repository().CloseTimedOutSessionsAsync(Utc(3, 10));
        Assert.Equal(PlayerSessionEndReason.Timeout, _db.Context.PlayerStatSessions.AsNoTracking().Single().EndReason);

        await _db.Ingestion().IngestAsync(new StatisticsBatchDto
        {
            BatchId = Guid.NewGuid(),
            Durations = new() { new() { SessionKey = key, UserId = 1, Metric = "active_playtime", From = Utc(3, 9), To = Utc(3, 11) } },
            Sessions = new() { new() { Type = "end", SessionKey = key, UserId = 1, At = Utc(3, 11), EndReason = "ServerStop" } }
        });

        var session = _db.Context.PlayerStatSessions.AsNoTracking().Single();
        Assert.Equal((Utc(3, 11), PlayerSessionEndReason.ServerStop, 7200, Utc(3, 11)),
            (session.EndedAt, session.EndReason, session.ActiveSeconds, session.LastHeartbeatAt));
    }

    [Fact]
    public async Task ADurationOfAnotherUsersSession_IsRejected()
    {
        var key = Guid.NewGuid();
        await _db.Ingestion().IngestAsync(new StatisticsBatchDto
        {
            BatchId = Guid.NewGuid(),
            Sessions = new() { new() { Type = "start", SessionKey = key, UserId = 1, At = Utc(3, 9) } }
        });

        var result = await _db.Ingestion().IngestAsync(new StatisticsBatchDto
        {
            BatchId = Guid.NewGuid(),
            Durations = new() { new() { SessionKey = key, UserId = 2, Metric = "active_playtime", From = Utc(3, 9), To = Utc(3, 10) } }
        });

        Assert.Equal("UnknownSession", result.Rejected.Single().Code);
        Assert.Null(_db.Total(2, "active_playtime"));
    }
}
