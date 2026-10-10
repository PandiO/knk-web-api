using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Privacy;
using knkwebapi_v2.Tests.Services.Telemetry;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// The MySQL paths of link 6: the AddDiagnosticTelemetryAndPrivacy migration applies (json columns,
/// check constraint), event inserts keep eventId unique, the timeline window query runs, and the
/// GDPR erasure deletes with ExecuteDelete inside one transaction. Logic is covered on EF InMemory.
/// The database is shared, so assertions look at this test's own users only.
/// </summary>
[Trait("Category", "requires-mysql")]
public class TelemetryPrivacyMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;
    private readonly DateTime _now = new(DateTime.UtcNow.Ticks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, DateTimeKind.Utc);

    public TelemetryPrivacyMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private async Task<int[]> UsersAsync(int count)
    {
        await using var ctx = _db.NewContext();
        var users = Enumerable.Range(0, count).Select(_ => new User { Username = "t" + Guid.NewGuid().ToString("N")[..12], Uuid = Guid.NewGuid().ToString() }).ToList();
        ctx.Users.AddRange(users);
        await ctx.SaveChangesAsync();
        return users.Select(u => u.Id).ToArray();
    }

    [MySqlFact]
    public async Task Events_AreInsertedOnce_AndTheTimelineQueryRuns()
    {
        var ids = await UsersAsync(1);
        var e = TelemetryTestDb.Stored("session.join", ids[0], _now.AddMinutes(-1), "corr-" + ids[0]);
        e.PayloadJson = "{\"world\":\"world\"}";
        await using (var ctx = _db.NewContext()) Assert.Equal(1, await new TelemetryRepository(ctx).InsertEventsAsync(new[] { e }));
        var replay = TelemetryTestDb.Stored("session.join", ids[0], _now.AddMinutes(-1));
        replay.EventId = e.EventId;
        await using (var ctx = _db.NewContext()) Assert.Equal(0, await new TelemetryRepository(ctx).InsertEventsAsync(new[] { replay }));

        await using var check = _db.NewContext();
        var repo = new TelemetryRepository(check);
        var events = await repo.GetUserEventsAsync(ids[0], _now.AddHours(-1), _now, 10);
        Assert.Equal("{\"world\": \"world\"}", Assert.Single(events).PayloadJson);
        Assert.Empty(await repo.GetLedgerLegsAsync(ids[0], _now.AddHours(-1), _now, 10));
        Assert.Empty(await repo.GetSiegeParticipationsAsync(ids[0], _now.AddHours(-1), _now, 10));
        Assert.Single(await repo.SearchAsync(new knkwebapi_v2.Repositories.Interfaces.TelemetrySearchFilter(CorrelationId: "corr-" + ids[0]), null, 10));
    }

    [MySqlFact]
    public async Task EnhancedTargets_NeedExactlyOneTarget()
    {
        await using var ctx = _db.NewContext();
        ctx.TelemetryEnhancedTargets.Add(new TelemetryEnhancedTarget { ExpiresAt = _now.AddHours(1), CreatedAt = _now });

        await Assert.ThrowsAsync<DbUpdateException>(() => ctx.SaveChangesAsync());
    }

    [MySqlFact]
    public async Task Erasure_DeletesTheScope_InOneTransaction()
    {
        var ids = await UsersAsync(2);
        var (target, other) = (ids[0], ids[1]);
        var day = DateOnly.FromDateTime(_now);
        await using (var ctx = _db.NewContext())
        {
            foreach (var user in ids)
            {
                ctx.PlayerStatDailies.Add(new PlayerStatDaily { UserId = user, Day = day, MetricKey = "pvp_kills", ContextKey = "", Value = 1, UpdatedAt = _now });
                ctx.PlayerStatTotals.Add(new PlayerStatTotal { UserId = user, MetricKey = "pvp_kills", ContextKey = "", Value = 1, ReachedAt = _now, UpdatedAt = _now });
                ctx.TelemetryEvents.Add(TelemetryTestDb.Stored("session.join", user, _now));
            }
            ctx.PlayerPvpKillPairDailies.Add(new PlayerPvpKillPairDaily { KillerUserId = other, VictimUserId = target, Day = day, ContextKey = "", Count = 2 });
            await ctx.SaveChangesAsync();
        }
        int requestId;
        await using (var ctx = _db.NewContext())
        {
            var request = new PrivacyDeletionRequest
            {
                UserId = target, Source = PrivacyRequestSource.Staff, RequestedAt = _now.AddDays(-6), DueAt = _now.AddDays(24),
                RequestedByUserId = other, ConfirmedAt = _now.AddDays(-6), ScheduledAt = _now.AddDays(-1)
            };
            ctx.PrivacyDeletionRequests.Add(request);
            await ctx.SaveChangesAsync();
            requestId = request.Id;
        }

        await using (var ctx = _db.NewContext())
        {
            var service = new PrivacyDeletionService(new PrivacyRepository(ctx),
                new CurrencyService(new CurrencyRepository(ctx), new UserRepository(ctx), NullLogger<CurrencyService>.Instance),
                new Mock<IAuditLogService>().Object, new Mock<IPrivacyEmailService>().Object, new PrivacyOptions(), () => _now);
            var result = await service.ExecuteAsync(requestId, other, dryRun: false);
            Assert.Equal(PrivacyRequestStatus.Completed, result.Request!.Status);
            Assert.Equal((1, 1, 1), (result.Request.Result!.Deleted["player_stat_daily"], result.Request.Result.Deleted["player_pvp_kill_pairs_daily"],
                result.Request.Result.Deleted["telemetry_events"]));
        }

        await using var check = _db.NewContext();
        Assert.False(await check.PlayerStatDailies.AnyAsync(d => d.UserId == target));
        Assert.True(await check.PlayerStatDailies.AnyAsync(d => d.UserId == other));
        Assert.False(await check.PlayerPvpKillPairDailies.AnyAsync(p => p.VictimUserId == target));
        Assert.Equal($"deleted-{target}", (await check.Users.SingleAsync(u => u.Id == target)).Username);
        Assert.NotNull((await check.PrivacyDeletionRequests.SingleAsync(r => r.Id == requestId)).ResultJson);
    }
}
