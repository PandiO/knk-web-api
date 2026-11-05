using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Realtime;
using knkwebapi_v2.Realtime.Contracts;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Privacy;
using knkwebapi_v2.Tests.Services.Telemetry;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Privacy;

/// <summary>
/// Realtime S5 (PandiO/knk-workspace#184, DESIGN §7.4): an executed erasure writes one feed row per erased account with
/// the UUID it had and pushes <c>AccountErased</c> for each; the feed pages by id for the game server's catch-up; the
/// former UUID is cleared after the retention period. Nothing is written or pushed for a dry run, a request in its grace
/// period or a repeated execution.
/// </summary>
public class PrivacyErasureFeedTests : IDisposable
{
    private static readonly DateTime Now = TelemetryTestDb.Now;
    private readonly TelemetryTestDb _db = new();
    private readonly RecordingSink _sink = new();

    private sealed class RecordingSink : IRealtimeEventSink
    {
        public List<RealtimeEvent> Events { get; } = new();
        public void Publish(RealtimeEvent realtimeEvent) => Events.Add(realtimeEvent);
    }

    public PrivacyErasureFeedTests()
    {
        // User 3 was merged into user 1 (MERGE_FORFEIT ledger leg sourced from user 1), so erasing 1 erases 3 too.
        _db.Context.CurrencyTransactions.Add(new CurrencyTransaction
        {
            PublicId = "merge", Kind = CurrencyTransactionKind.Grant, ReasonCode = CurrencyReasons.MergeForfeit,
            Reason = CurrencyReasons.MergeForfeit, SourceType = "User", SourceRef = "1", IdempotencyScope = "t",
            IdempotencyKey = "merge", RequestHash = "h", CreatedAt = Now.AddDays(-10),
            Entries = new List<CurrencyEntry>
            {
                new() { Currency = Currency.Coins, AccountKind = CurrencyAccountKind.User, UserId = 3, Operation = CurrencyOperation.Add,
                    Amount = 5, BalanceBefore = 0, BalanceAfter = 5 }
            }
        });
        _db.Context.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private PrivacyDeletionService Service() =>
        new(new PrivacyRepository(_db.Context),
            new CurrencyService(new CurrencyRepository(_db.Context), new UserRepository(_db.Context), NullLogger<CurrencyService>.Instance),
            _db.Audit.Object, _db.Email.Object, _db.PrivacyOptions, () => _db.Clock, "https://knk.example", _db.Cache,
            realtime: _sink);

    private async Task<int> ScheduledFor(int userId)
    {
        var result = await Service().FileForPlayerAsync(9, PrivacyRequestSource.Staff, userId, null);
        Assert.Equal(PrivacyOutcome.Ok, result.Outcome);
        return result.Request!.Id;
    }

    private List<AccountErasedPayload> Pushed() =>
        _sink.Events.Where(e => e.Type == PrivacyEventTypes.AccountErased).Select(e => (AccountErasedPayload)e.Payload!).ToList();

    [Fact]
    public async Task Execute_WritesOneFeedRowPerErasedAccount_WithTheFormerUuid_AndPushesEach()
    {
        var id = await ScheduledFor(1);
        _db.Clock = Now.AddDays(5);

        var result = await Service().ExecuteAsync(id, 7, dryRun: false);

        Assert.Equal(PrivacyRequestStatus.Completed, result.Request!.Status);
        var feed = await _db.NewContext().PrivacyErasureEvents.OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(new[] { (1, "uuid-a"), (3, "uuid-c") }, feed.Select(e => (e.UserId, e.FormerUuid!)));
        Assert.All(feed, e => Assert.Equal((id, Now.AddDays(5)), (e.DeletionRequestId, e.ErasedAt)));
        // The accounts lost their UUIDs; only the feed remembers them.
        Assert.All(await _db.NewContext().Users.Where(u => u.Id == 1 || u.Id == 3).ToListAsync(), u => Assert.Null(u.Uuid));

        var pushed = Pushed();
        Assert.Equal(feed.Select(e => (e.UserId, e.FormerUuid, e.Id)), pushed.Select(p => (p.UserId, p.FormerUuid, p.ErasureId)));
        Assert.All(_sink.Events.Where(e => e.Type == PrivacyEventTypes.AccountErased), e =>
        {
            Assert.Null(e.ServerId); // every game server
            Assert.Equal(7, e.ActorUserId);
            Assert.Null(e.WebGroups);
        });
    }

    [Fact]
    public async Task DryRun_GracePeriod_AndRepeatedExecution_WriteAndPushNothingNew()
    {
        var id = await ScheduledFor(2);

        Assert.Equal(PrivacyOutcome.Ok, (await Service().ExecuteAsync(id, 7, dryRun: true)).Outcome);
        Assert.Equal(PrivacyOutcome.GracePeriod, (await Service().ExecuteAsync(id, 7, dryRun: false)).Outcome);
        Assert.Empty(await _db.NewContext().PrivacyErasureEvents.ToListAsync());
        Assert.Empty(Pushed());

        _db.Clock = Now.AddDays(5);
        await Service().ExecuteAsync(id, null, dryRun: false);
        await Service().ExecuteAsync(id, null, dryRun: false);

        var row = Assert.Single(await _db.NewContext().PrivacyErasureEvents.ToListAsync());
        Assert.Equal((2, "uuid-b"), (row.UserId, row.FormerUuid));
        Assert.Single(Pushed());
    }

    [Fact]
    public async Task AnAccountWithoutUuid_GetsARowAndAPushWithoutOne()
    {
        _db.Context.Users.Add(new User { Id = 4, Username = "webonly" });
        _db.Context.SaveChanges();
        var id = await ScheduledFor(4);
        _db.Clock = Now.AddDays(5);

        await Service().ExecuteAsync(id, null, dryRun: false);

        Assert.Null(Assert.Single(await _db.NewContext().PrivacyErasureEvents.ToListAsync()).FormerUuid);
        var pushed = Assert.Single(Pushed());
        Assert.Equal((4, (string?)null), (pushed.UserId, pushed.FormerUuid));
    }

    private void Feed(int count, DateTime erasedAt)
    {
        for (var i = 0; i < count; i++)
        {
            _db.Context.PrivacyErasureEvents.Add(new PrivacyErasureEvent
            {
                UserId = 100 + i, FormerUuid = $"uuid-{i}", DeletionRequestId = 1, ErasedAt = erasedAt
            });
        }
        _db.Context.SaveChanges();
    }

    [Fact]
    public async Task Feed_PagesById_OldestFirst()
    {
        Feed(5, Now);
        var ids = await _db.NewContext().PrivacyErasureEvents.OrderBy(e => e.Id).Select(e => e.Id).ToListAsync();

        var first = await Service().GetErasuresSinceAsync(0, 2);
        Assert.Equal(ids.Take(2), first.Items.Select(i => i.ErasureId));
        Assert.Equal((ids[1], true, ids[4]), (first.LastId, first.HasMore, first.LatestId));
        Assert.Equal(("uuid-0", 100), (first.Items[0].FormerUuid, first.Items[0].UserId));

        var rest = await Service().GetErasuresSinceAsync(first.LastId, 10);
        Assert.Equal(ids.Skip(2), rest.Items.Select(i => i.ErasureId));
        Assert.Equal((ids[4], false), (rest.LastId, rest.HasMore));

        var none = await Service().GetErasuresSinceAsync(ids[4], 10);
        Assert.Empty(none.Items);
        Assert.Equal((ids[4], false, ids[4]), (none.LastId, none.HasMore, none.LatestId));

        // A position from another database (the feed was reset): nothing above it, and LatestId shows the reset.
        var ahead = await Service().GetErasuresSinceAsync(ids[4] + 50, 10);
        Assert.Empty(ahead.Items);
        Assert.True(ahead.LatestId < ahead.LastId);
    }

    [Fact]
    public async Task Feed_LimitIsClamped()
    {
        Feed(3, Now);
        Assert.Single((await Service().GetErasuresSinceAsync(0, 0)).Items);
        Assert.Equal(3, (await Service().GetErasuresSinceAsync(-5, 10_000)).Items.Count);
    }

    [Fact]
    public async Task FormerUuids_AreClearedAfterTheRetention_RowsStay()
    {
        Feed(1, Now.AddDays(-36));
        Feed(1, Now.AddDays(-34));

        Assert.Equal(1, await Service().ClearExpiredErasureUuidsAsync(Now));

        var rows = await _db.NewContext().PrivacyErasureEvents.OrderBy(e => e.Id).ToListAsync();
        Assert.Equal(2, rows.Count);
        Assert.Equal(((string?)null, (DateTime?)Now), (rows[0].FormerUuid, rows[0].UuidClearedAt));
        Assert.Equal(("uuid-0", (DateTime?)null), (rows[1].FormerUuid, rows[1].UuidClearedAt));
        Assert.Equal(0, await Service().ClearExpiredErasureUuidsAsync(Now));
    }

    [Fact]
    public async Task Retention_IsAtLeast31Days()
    {
        _db.PrivacyOptions.ErasureFeedUuidRetentionDays = 1;
        Feed(1, Now.AddDays(-30));

        Assert.Equal(0, await Service().ClearExpiredErasureUuidsAsync(Now));
    }

    [Fact]
    public async Task DueJob_ClearsExpiredUuids()
    {
        Feed(1, Now.AddDays(-40));
        using var provider = _db.Provider();
        var job = new PrivacyDeletionDueService(provider, NullLogger<PrivacyDeletionDueService>.Instance,
            Microsoft.Extensions.Options.Options.Create(_db.PrivacyOptions));

        await job.RunOnceAsync(Now);

        Assert.Null((await _db.NewContext().PrivacyErasureEvents.SingleAsync()).FormerUuid);
    }

    [Fact]
    public async Task Controller_RefusesANegativeSince_AndReturnsThePage()
    {
        Feed(2, Now);
        var controller = new PrivacyErasuresController(Service());

        Assert.IsType<BadRequestObjectResult>((await controller.GetSince(-1)).Result);
        var page = Assert.IsType<PrivacyErasureFeedDto>(Assert.IsType<OkObjectResult>((await controller.GetSince(0, 1)).Result).Value);
        Assert.True(page.HasMore);
        Assert.Single(page.Items);
    }
}
