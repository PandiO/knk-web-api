using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Telemetry;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Telemetry;

/// <summary>
/// Owner reads (IMPLEMENTATION_PLAN.md §3.3; link 6 acceptance criteria 3 and 5): filters and keyset
/// paging, event detail with correlated events and ledger links, the merged timeline (events +
/// ledger + Siege, read-time join), audit of every read, plugin config, test runs, enhanced
/// targets, health and retention.
/// </summary>
public class TelemetryQueryServiceTests : IDisposable
{
    private static readonly DateTime T0 = TelemetryTestDb.Now.AddHours(-2);
    private readonly TelemetryTestDb _db = new();

    public void Dispose() => _db.Dispose();

    private void Seed(params TelemetryEvent[] events)
    {
        _db.Context.TelemetryEvents.AddRange(events);
        _db.Context.SaveChanges();
    }

    private void SeedLedger(int userId, string publicId, string correlationId, DateTime at, long before, long after)
    {
        _db.Context.CurrencyTransactions.Add(new CurrencyTransaction
        {
            PublicId = publicId,
            Kind = CurrencyTransactionKind.Grant,
            ReasonCode = "SIEGE_REWARD",
            Reason = "Siege reward",
            IdempotencyScope = "test",
            IdempotencyKey = publicId,
            RequestHash = "h",
            CorrelationId = correlationId,
            CreatedAt = at,
            Entries = new List<CurrencyEntry>
            {
                new() { Currency = Currency.Coins, AccountKind = CurrencyAccountKind.User, UserId = userId, Operation = CurrencyOperation.Add,
                        Amount = after - before, BalanceBefore = before, BalanceAfter = after }
            }
        });
        _db.Context.SaveChanges();
    }

    private void SeedSiege(int userId, int matchId, DateTime joined, DateTime? left, DateTime? ended)
    {
        _db.Context.SiegeMatches.Add(new SiegeMatch
        {
            Id = matchId, SiegeLobbyId = 1, SiegeScenarioId = 1, Status = SiegeMatchStatus.Completed, StartedAt = joined, EndedAt = ended
        });
        _db.Context.SiegeMatchParticipants.Add(new SiegeMatchParticipant
        {
            SiegeMatchId = matchId, UserId = userId, JoinedAt = joined, LeftAt = left, Kills = 3, Deaths = 1, Captures = 1, SiegeTeamId = 5
        });
        _db.Context.SaveChanges();
    }

    [Fact]
    public async Task Search_FiltersAndPagesNewestFirst_AndAuditsEveryRead()
    {
        Seed(Enumerable.Range(0, 5).Select(i => TelemetryTestDb.Stored("session.join", 1, T0.AddMinutes(i))).ToArray());
        Seed(TelemetryTestDb.Stored("session.join", 2, T0.AddMinutes(10)), TelemetryTestDb.Stored("menu.opened", 1, T0.AddMinutes(11)));
        var query = _db.Query();

        var first = await query.SearchAsync(9, new TelemetrySearchFilter(UserId: 1, Name: "session.join"), null, 2);
        var second = await query.SearchAsync(9, new TelemetrySearchFilter(UserId: 1, Name: "session.join"), first.NextBefore, 2);
        var last = await query.SearchAsync(9, new TelemetrySearchFilter(UserId: 1, Name: "session.join"), second.NextBefore, 2);

        Assert.Equal(new[] { T0.AddMinutes(4), T0.AddMinutes(3) }, first.Items.Select(e => e.OccurredAt));
        Assert.Equal(new[] { T0.AddMinutes(2), T0.AddMinutes(1) }, second.Items.Select(e => e.OccurredAt));
        Assert.Equal(new[] { T0 }, last.Items.Select(e => e.OccurredAt));
        Assert.Null(last.NextBefore);
        Assert.All(first.Items, e => Assert.Equal("alice", e.Username));
        _db.Audit.Verify(a => a.RecordAsync(9, 1, AuditAction.TelemetryViewed, It.Is<string>(d => d.Contains("\"endpoint\":\"events\""))), Times.Exactly(3));
    }

    [Fact]
    public async Task Search_WithoutAPlayer_IsAuditedAgainstTheReader()
    {
        Seed(TelemetryTestDb.Stored("session.join", 1, T0));

        var page = await _db.Query().SearchAsync(9, new TelemetrySearchFilter(Outcome: TelemetryOutcome.Failed), null, 10);

        Assert.Empty(page.Items);
        _db.Audit.Verify(a => a.RecordAsync(9, 9, AuditAction.TelemetryViewed, It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task Detail_ReturnsCorrelatedEvents_AndLedgerLinks()
    {
        var attempt = TelemetryTestDb.Stored("siege.lobby_join_attempt", 1, T0, "corr-7", seq: 1, matchId: 4);
        var failure = TelemetryTestDb.Stored("api.request_failed", 1, T0.AddSeconds(1), "corr-7", seq: 2);
        Seed(attempt, failure, TelemetryTestDb.Stored("session.join", 1, T0, "other"));
        SeedLedger(1, "tx-1", "corr-7", T0, 0, 50);

        var detail = await _db.Query().GetEventAsync(9, attempt.EventId);

        Assert.Equal(attempt.EventId, detail!.Event.EventId);
        Assert.Equal(failure.EventId, Assert.Single(detail.Related).EventId);
        Assert.Equal(new[] { "tx-1" }, detail.Links.LedgerTransactionPublicIds);
        Assert.Equal(4, detail.Links.SiegeMatchId);
        _db.Audit.Verify(a => a.RecordAsync(9, 1, AuditAction.TelemetryViewed, It.IsAny<string>()), Times.Once);
        Assert.Null(await _db.Query().GetEventAsync(9, Guid.NewGuid()));
    }

    [Fact]
    public async Task Timeline_MergesEventsLedgerAndSiege_InOrder()
    {
        Seed(TelemetryTestDb.Stored("siege.match_join", 1, T0.AddMinutes(1), seq: 1),
             TelemetryTestDb.Stored("siege.match_leave", 1, T0.AddMinutes(30), seq: 2),
             TelemetryTestDb.Stored("session.join", 2, T0.AddMinutes(2)),                // another player
             TelemetryTestDb.Stored("session.join", 1, T0.AddDays(-2)));                 // outside the window
        SeedLedger(1, "tx-reward", "c", T0.AddMinutes(31), 10, 60);
        SeedSiege(1, 4, T0.AddMinutes(1), null, T0.AddMinutes(29));

        var timeline = await _db.Query().GetTimelineAsync(9, 1, T0, TelemetryTestDb.Now, 100);

        Assert.Equal("alice", timeline!.Username);
        Assert.Equal(new[] { "event", "siege", "siege", "event", "ledger" }, timeline.Items.Select(i => i.Kind));
        Assert.Equal("joined", timeline.Items[1].Siege!.Kind);
        Assert.Equal(("ended", 4, 3), (timeline.Items[2].Siege!.Kind, timeline.Items[2].Siege!.MatchId, timeline.Items[2].Siege!.Kills));
        Assert.Equal(("tx-reward", 50L, "Coins"), (timeline.Items[4].Ledger!.PublicId, timeline.Items[4].Ledger!.Delta, timeline.Items[4].Ledger!.Currency));
        Assert.False(timeline.Truncated);
        _db.Audit.Verify(a => a.RecordAsync(9, 1, AuditAction.TelemetryViewed, It.Is<string>(d => d.Contains("timeline"))), Times.Once);
    }

    [Fact]
    public async Task Timeline_UnknownUser_IsNull_AndALimitMarksTruncation()
    {
        Assert.Null(await _db.Query().GetTimelineAsync(9, 99, T0, TelemetryTestDb.Now, 10));
        Seed(Enumerable.Range(0, 3).Select(i => TelemetryTestDb.Stored("menu.opened", 1, T0.AddMinutes(i))).ToArray());

        var timeline = await _db.Query().GetTimelineAsync(9, 1, T0, TelemetryTestDb.Now, 2);

        Assert.True(timeline!.Truncated);
        Assert.Equal(2, timeline.Items.Count);
    }

    [Fact]
    public async Task ClientConfig_ListsActiveRunsAndTargets()
    {
        _db.Context.TelemetryTestRuns.AddRange(
            new TelemetryTestRun { Id = 1, Name = "old", StartedAt = T0, EndedAt = T0.AddHours(1) },
            new TelemetryTestRun { Id = 2, Name = "alpha", StartedAt = T0 });
        _db.Context.TelemetryEnhancedTargets.AddRange(
            new TelemetryEnhancedTarget { UserId = 3, ExpiresAt = TelemetryTestDb.Now.AddHours(1) },
            new TelemetryEnhancedTarget { UserId = 2, ExpiresAt = TelemetryTestDb.Now.AddHours(-1) },
            new TelemetryEnhancedTarget { TestRunId = 2, ExpiresAt = TelemetryTestDb.Now.AddHours(1) },
            new TelemetryEnhancedTarget { TestRunId = 1, ExpiresAt = TelemetryTestDb.Now.AddHours(1) });
        _db.Context.SaveChanges();

        var config = await _db.Query().GetClientConfigAsync();

        Assert.True(config.Enabled);
        Assert.Equal(new[] { 3 }, config.EnhancedUserIds);
        Assert.Equal(new[] { 2 }, config.ActiveTestRunIds);
        Assert.Equal(new[] { 2 }, config.EnhancedTestRunIds);
        Assert.Contains("session.join", config.BaselineEventNames);
        Assert.Contains("movement.sample", config.EnhancedEventNames);

        _db.TelemetryOptions.Enabled = false;
        var disabled = await _db.Query().GetClientConfigAsync();
        Assert.False(disabled.Enabled);
        Assert.Empty(disabled.EnhancedUserIds);
    }

    [Fact]
    public async Task TestRuns_StartAndEnd_Idempotently()
    {
        var query = _db.Query();
        var run = await query.StartTestRunAsync(9, "  Siege alpha 1 ", "steps 1-6");

        _db.Clock = TelemetryTestDb.Now.AddHours(1);
        var ended = await query.EndTestRunAsync(run.Id);
        _db.Clock = TelemetryTestDb.Now.AddHours(2);
        var again = await query.EndTestRunAsync(run.Id);

        Assert.Equal(("Siege alpha 1", 9), (run.Name, run.CreatedByUserId));
        Assert.Equal(TelemetryTestDb.Now.AddHours(1), ended!.EndedAt);
        Assert.Equal(TelemetryTestDb.Now.AddHours(1), again!.EndedAt);
        Assert.Null(await query.EndTestRunAsync(999));
        Assert.Single(await query.GetTestRunsAsync());
    }

    [Fact]
    public async Task EnhancedTargets_RequireAKnownUserOrActiveRun()
    {
        var query = _db.Query();
        _db.Context.TelemetryTestRuns.Add(new TelemetryTestRun { Id = 5, Name = "ended", StartedAt = T0, EndedAt = T0 });
        _db.Context.SaveChanges();

        var target = await query.AddEnhancedTargetAsync(9, 1, null, TelemetryTestDb.Now.AddHours(4));

        Assert.Equal(("alice", 1), (target!.Username, target.UserId));
        Assert.Null(await query.AddEnhancedTargetAsync(9, 99, null, TelemetryTestDb.Now.AddHours(1)));
        Assert.Null(await query.AddEnhancedTargetAsync(9, null, 5, TelemetryTestDb.Now.AddHours(1)));
        Assert.Single(await query.GetEnhancedTargetsAsync());
        Assert.True(await query.RemoveEnhancedTargetAsync(target.Id));
        Assert.False(await query.RemoveEnhancedTargetAsync(target.Id));
    }

    [Fact]
    public async Task Health_ReportsQueueAndVolume()
    {
        var queue = _db.Queue(capacity: 1);
        queue.TryEnqueue(TelemetryTestDb.Stored("session.join", 1, T0));
        queue.TryEnqueue(TelemetryTestDb.Stored("session.join", 1, T0));
        Seed(TelemetryTestDb.Stored("session.join", 1, T0), TelemetryTestDb.Stored("session.join", 1, TelemetryTestDb.Now.AddDays(-2)));

        var health = await _db.Query(queue).GetHealthAsync();

        Assert.Equal((1, 1, 1L, 1), (health.QueueDepth, health.QueueCapacity, health.DroppedSinceStart, health.EventsLast24h));
    }

    [Fact]
    public void Cursor_RejectsGarbage()
    {
        var query = _db.Query();
        Assert.Null(query.ParseCursor("abc"));
        Assert.Null(query.ParseCursor("1_2_3"));
        Assert.Null(query.ParseCursor("-5_1"));
        Assert.Equal(new TelemetryCursor(new DateTime(100, DateTimeKind.Utc), 7), query.ParseCursor("100_7"));
    }

    [Fact]
    public async Task Retention_PurgesBaselineAndEnhancedSeparately()
    {
        var now = TelemetryTestDb.Now;
        Seed(TelemetryTestDb.Stored("session.join", 1, now.AddDays(-91)),
             TelemetryTestDb.Stored("session.join", 1, now.AddDays(-89)),
             TelemetryTestDb.Stored("movement.sample", 1, now.AddDays(-15), level: TelemetryLevel.Enhanced),
             TelemetryTestDb.Stored("movement.sample", 1, now.AddDays(-13), level: TelemetryLevel.Enhanced));
        _db.Context.TelemetryEnhancedTargets.AddRange(
            new TelemetryEnhancedTarget { UserId = 1, ExpiresAt = now.AddDays(-31) },
            new TelemetryEnhancedTarget { UserId = 1, ExpiresAt = now.AddDays(-1) });
        _db.Context.SaveChanges();

        await new TelemetryRetentionService(_db.Provider(), NullLogger<TelemetryRetentionService>.Instance,
            Options.Create(_db.TelemetryOptions)).RunOnceAsync(now);

        var left = _db.NewContext().TelemetryEvents.Select(e => e.OccurredAt).OrderBy(d => d).ToList();
        Assert.Equal(new[] { now.AddDays(-89), now.AddDays(-13) }, left);
        Assert.Single(_db.NewContext().TelemetryEnhancedTargets);
    }
}
