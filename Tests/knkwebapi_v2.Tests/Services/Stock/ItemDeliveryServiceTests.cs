using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Json;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static knkwebapi_v2.Tests.Services.Stock.StockTestDb;

namespace knkwebapi_v2.Tests.Services.Stock;

/// <summary>
/// P7 (PandiO/knk-workspace#60, production DESIGN § 4.6, D7, A15): deliveries into the reward inbox, the hard cap with
/// overflow to SYS_LOSS, premium caps, near-cap warnings, claims with partial space, unique items, expiry with
/// reminders, the stash, idempotent retries and the flag. Steve (<see cref="StockTestDb.PlayerId"/>) is the player;
/// oak logs weigh 1, stone 2, the sword is a unique item. The reconciler stays clean after every path.
/// </summary>
public class ItemDeliveryServiceTests : IDisposable
{
    private const int GoldTier = 70, OtherTier = 71;

    private readonly KnKDbContext _db;
    private readonly TestClock _clock = new();
    private readonly InMemoryPlayerNotificationQueue _notifications = new();
    private readonly ItemDeliveryService _service;

    public ItemDeliveryServiceTests()
    {
        _db = Create();
        Settings(new ProductionDeliverySettingsDto
        {
            InboxBaseCapUnits = 100,
            StashBaseCapUnits = 0,
            PremiumTiers = new() { new PremiumStorageCapDto { PermissionGroupId = GoldTier, InboxCapUnits = 500, StashCapUnits = 200 } },
        });
        _db.PermissionGroups.Add(new PermissionGroup { Id = GoldTier, Name = "Gold", Weight = 20, IsPremiumTier = true });
        _db.PermissionGroups.Add(new PermissionGroup { Id = OtherTier, Name = "Silver", Weight = 10, IsPremiumTier = true });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        _service = new ItemDeliveryService(_db, new StockService(_db, _clock), new ProductionSettingsReader(_db), _notifications, _clock);
    }

    public void Dispose() => _db.Dispose();

    private void Settings(ProductionDeliverySettingsDto delivery, bool enabled = true)
    {
        var row = _db.GameSettings.Single();
        row.ProductionSettingsJson = JsonColumn.Serialize(new ProductionSettingsDto { Enabled = enabled, Delivery = delivery });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
    }

    private static ItemDeliveryRequestDto Request(string key, params (int Blueprint, long Amount)[] lines) => new()
    {
        UserId = PlayerId,
        Reason = ItemDeliveryReason.Lootbox,
        SourceType = "LootboxClaim",
        SourceRef = "77",
        IdempotencyKey = key,
        Lines = lines.Select(l => new ItemDeliveryLineRequestDto { ItemBlueprintId = l.Blueprint, Amount = l.Amount }).ToList(),
    };

    private static PlayerStorageMoveDto Move(string key, params (int Blueprint, long Amount)[] lines) => new()
    {
        IdempotencyKey = key,
        Lines = lines.Select(l => new ItemDeliveryLineRequestDto { ItemBlueprintId = l.Blueprint, Amount = l.Amount }).ToList(),
    };

    private static readonly DeliveryActor Server = DeliveryActor.GameServer(PlayerId);

    private int InboxId => _db.Storages.AsNoTracking().Single(s => s.UserId == PlayerId && s.Purpose == StoragePurpose.Inbox).Id;

    private int Sys(string key) => _db.Storages.AsNoTracking().Single(s => s.SystemKey == key).Id;

    private async Task AssertReconciledAsync() => Assert.True((await new StockReconciler(_db, _clock).RunAsync()).Clean);

    private List<InboxNotificationDto> Inbox() =>
        _notifications.GetPending().Where(n => n.Type == PlayerNotificationTypes.InboxUpdated).Select(n => n.Inbox!).ToList();

    [Fact]
    public async Task ADelivery_MovesTheItemsIntoTheInbox_AndTellsThePlayer()
    {
        var d = await _service.DeliverAsync(Request("lootbox-77", (OakLog, 40), (Stone, 5)), Server);

        Assert.Equal((ItemDeliveryStatus.Inboxed, 45L, 0L), (d.Status, d.InboxedUnits, d.LostUnits));
        Assert.Equal(40, Amount(_db, InboxId, OakLog));
        Assert.Equal(5, Amount(_db, InboxId, Stone));
        var inbox = StockTestDb.Storage(_db, InboxId);
        Assert.Equal((StorageKind.Player, StoragePurpose.Inbox, 100L), (inbox.Kind, inbox.Purpose, inbox.MaxUnits!.Value));
        var tx = _db.StockTransactions.AsNoTracking().Single(t => t.Id == d.StockTransactionId);
        Assert.Equal((StockReason.RewardInbox, StockActorKind.PluginServer, "LootboxClaim", "77"), (tx.Reason, tx.ActorKind, tx.SourceType, tx.SourceRef));
        Assert.All(_db.StockMoves.AsNoTracking().Where(m => m.TransactionId == tx.Id), m => Assert.Equal(Sys(Models.Storage.SysWorld), m.FromStorageId));
        Assert.Equal(_clock.Now.UtcDateTime.AddDays(30), d.ExpiresAt);

        var n = Assert.Single(Inbox());
        Assert.Equal((InboxNotificationDto.EventDelivered, d.Id, 45L, 0L, 50, 100L), (n.Event, n.DeliveryId, n.InboxedUnits, n.LostUnits, n.FillPercent, n.CapUnits));
        Assert.Null(n.WarningPercent); // 50 % (40 + 5 × 2) is below 80
        await AssertReconciledAsync();
    }

    [Fact]
    public async Task ARetry_AppliesOnce_AndTheSameKeyWithAnotherRequestIs409()
    {
        var first = await _service.DeliverAsync(Request("kit-claim-9", (OakLog, 10)), Server);
        var again = await _service.DeliverAsync(Request("kit-claim-9", (OakLog, 10)), Server);

        Assert.True(again.Replayed);
        Assert.Equal(first.Id, again.Id);
        Assert.Equal(10, Amount(_db, InboxId, OakLog));
        Assert.Single(_db.Set<ItemDelivery>().AsNoTracking());
        await Assert.ThrowsAsync<StockIdempotencyConflictException>(() => _service.DeliverAsync(Request("kit-claim-9", (OakLog, 11)), Server));
        await AssertReconciledAsync();
    }

    [Fact]
    public async Task AtTheHardCap_TheOverflowIsLostToSysLoss_WithALossNotificationAndAWarningBefore()
    {
        var first = await _service.DeliverAsync(Request("a", (OakLog, 85)), Server);
        Assert.Equal(80, Assert.Single(Inbox()).WarningPercent); // 0 → 85 % passes 80

        var second = await _service.DeliverAsync(Request("b", (OakLog, 10), (Stone, 5)), Server);

        // 15 units free: 10 oak logs fit, then 5 units = 2 stone (weight 2); 3 stone are lost.
        Assert.Equal(ItemDeliveryStatus.PartlyLost, second.Status);
        Assert.Equal((10L, 0L), (second.Lines[0].Inboxed, second.Lines[0].Lost));
        Assert.Equal((2L, 3L), (second.Lines[1].Inboxed, second.Lines[1].Lost));
        Assert.Equal(3, Amount(_db, Sys(Models.Storage.SysLoss), Stone));
        var overflow = _db.StockTransactions.AsNoTracking().Single(t => t.Id == second.OverflowTransactionId);
        Assert.Equal(StockReason.InboxOverflow, overflow.Reason);
        var move = _db.StockMoves.AsNoTracking().Single(m => m.TransactionId == overflow.Id);
        Assert.Equal((Sys(Models.Storage.SysWorld), Sys(Models.Storage.SysLoss), 3L), (move.FromStorageId, move.ToStorageId, move.Amount));

        var n = Inbox().Last();
        Assert.Equal((3L, 95, 99), (n.LostUnits, n.WarningPercent!.Value, n.FillPercent));

        var full = await _service.DeliverAsync(Request("c", (Stone, 5)), Server); // 1 unit free, a stone needs 2
        Assert.Equal((ItemDeliveryStatus.Lost, 5L), (full.Status, full.LostUnits));
        Assert.Null(full.StockTransactionId);
        Assert.NotEqual(first.Id, full.Id);
        await AssertReconciledAsync();
    }

    [Fact]
    public async Task APremiumTier_RaisesTheCaps_AndAnExpiredTierNoLonger()
    {
        Assert.Equal((100L, 0L), ((await _service.ViewAsync(PlayerId, StoragePurpose.Inbox)).CapUnits,
            (await _service.ViewAsync(PlayerId, StoragePurpose.Stash)).CapUnits));

        _db.UserPermissionGroups.Add(new UserPermissionGroup { UserId = PlayerId, PermissionGroupId = OtherTier });
        _db.UserPermissionGroups.Add(new UserPermissionGroup { UserId = PlayerId, PermissionGroupId = GoldTier, ExpiresAt = _clock.Now.UtcDateTime.AddDays(1) });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        var d = await _service.DeliverAsync(Request("big", (OakLog, 300)), Server);
        Assert.Equal((300L, 0L, 500L), (d.InboxedUnits, d.LostUnits, d.CapUnits));
        var view = await _service.ViewAsync(PlayerId, StoragePurpose.Inbox);
        Assert.Equal((500L, GoldTier, 60), (view.CapUnits, view.PremiumTierGroupId!.Value, view.FillPercent));

        // Gold ends: Silver has no entry, so the base cap is back; nothing already in the inbox is trimmed.
        _clock.Now = _clock.Now.AddDays(2);
        var after = await _service.DeliverAsync(Request("after", (OakLog, 1)), Server);
        Assert.Equal((0L, 1L, 100L), (after.InboxedUnits, after.LostUnits, after.CapUnits));
        Assert.Equal(300, Amount(_db, InboxId, OakLog));
        Assert.Equal(100L, StockTestDb.Storage(_db, InboxId).MaxUnits);
        await AssertReconciledAsync();
    }

    [Fact]
    public async Task AClaim_WithRoomForHalf_TakesHalf_OldestLotFirst_AndTheRestStays()
    {
        var older = await _service.DeliverAsync(Request("old", (OakLog, 20)), Server);
        _clock.Now = _clock.Now.AddDays(1);
        var newer = await _service.DeliverAsync(Request("new", (OakLog, 20)), Server);

        var claim = await _service.ClaimAsync(PlayerId, Move("click-1", (OakLog, 25)), Server);

        Assert.Equal((StockResultStatus.Applied, 25L), (claim.Result.Status, claim.Result.Accepted));
        Assert.Equal(15, Amount(_db, InboxId, OakLog));
        Assert.Equal(StockReason.InboxClaim, _db.StockTransactions.AsNoTracking().Single(t => t.Id == claim.Result.TransactionId).Reason);
        Assert.Equal((20L, 0L), ((await _service.GetAsync(older.Id))!.Lines[0].Claimed, (await _service.GetAsync(older.Id))!.Lines[0].Remaining));
        Assert.Equal((5L, 15L), ((await _service.GetAsync(newer.Id))!.Lines[0].Claimed, (await _service.GetAsync(newer.Id))!.Lines[0].Remaining));
        var entry = Assert.Single(claim.View.Entries);
        Assert.Equal((15L, newer.ExpiresAt, 15L), (entry.Amount, entry.NextExpiresAt!.Value, entry.NextExpiringAmount));

        // A retry of the same click moves nothing more; asking for more than there is moves what there is.
        var replay = await _service.ClaimAsync(PlayerId, Move("click-1", (OakLog, 25)), Server);
        Assert.True(replay.Result.Replayed);
        Assert.Equal(15, Amount(_db, InboxId, OakLog));
        var rest = await _service.ClaimAsync(PlayerId, Move("click-2", (OakLog, 64)), Server);
        Assert.Equal((StockResultStatus.PartiallyApplied, 15L), (rest.Result.Status, rest.Result.Accepted));
        Assert.Equal(StockRejectReason.NotEnoughStock, rest.Result.Lines[0].RejectReason);
        Assert.Equal(0L, (await _service.GetAsync(newer.Id))!.RemainingUnits);
        await AssertReconciledAsync();
    }

    [Fact]
    public async Task AUniqueReward_IsAStockUnitInTheInbox_AndKeepsItsIdentityWhenClaimed()
    {
        var d = await _service.DeliverAsync(new ItemDeliveryRequestDto
        {
            UserId = PlayerId, Reason = ItemDeliveryReason.Lootbox, IdempotencyKey = "sword",
            Lines = new() { new ItemDeliveryLineRequestDto { ItemInstanceId = SwordInstance } },
        }, Server);

        Assert.Equal((Sword, SwordInstance), (d.Lines[0].ItemBlueprintId, d.Lines[0].ItemInstanceId!.Value));
        Assert.Equal(InboxId, _db.StockUnits.AsNoTracking().Single(u => u.ItemInstanceId == SwordInstance).StorageId);
        var entry = Assert.Single((await _service.ViewAsync(PlayerId, StoragePurpose.Inbox)).Entries);
        Assert.Equal((SwordInstance, "Dawnbreaker"), (entry.ItemInstanceId!.Value, entry.CustomDisplayName));

        // The same instance can't be delivered while it sits in a storage.
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DeliverAsync(new ItemDeliveryRequestDto
        {
            UserId = PlayerId, IdempotencyKey = "sword-2", Lines = new() { new ItemDeliveryLineRequestDto { ItemInstanceId = SwordInstance } },
        }, Server));

        var claim = await _service.ClaimAsync(PlayerId, new PlayerStorageMoveDto
        {
            IdempotencyKey = "take-sword", Lines = new() { new ItemDeliveryLineRequestDto { ItemInstanceId = SwordInstance } },
        }, Server);
        Assert.Equal(1L, claim.Result.Accepted);
        Assert.False(_db.StockUnits.AsNoTracking().Any(u => u.ItemInstanceId == SwordInstance));
        Assert.Equal("Dawnbreaker", _db.ItemInstances.AsNoTracking().Single(i => i.Id == SwordInstance).CustomDisplayName);
        await AssertReconciledAsync();
    }

    [Fact]
    public async Task Reminders_GoOutBeforeExpiry_ThenExpiredItemsMoveToSysLoss()
    {
        var d = await _service.DeliverAsync(Request("old", (OakLog, 30), (Stone, 4)), Server);
        await _service.ClaimAsync(PlayerId, Move("c", (OakLog, 10)), Server);
        _notifications.Acknowledge(_notifications.GetPending().Select(n => n.Id));

        _clock.Now = _clock.Now.AddDays(22); // 8 days left: no reminder yet
        Assert.Equal(0, (await _service.SweepAsync()).RemindersSent);

        _clock.Now = _clock.Now.AddDays(2); // 6 days left: the 7-day reminder
        Assert.Equal(1, (await _service.SweepAsync()).RemindersSent);
        var reminder = Assert.Single(Inbox());
        Assert.Equal((InboxNotificationDto.EventExpiryReminder, 24L, d.ExpiresAt), (reminder.Event, reminder.ExpiringUnits, reminder.ExpiresAt!.Value));
        Assert.Equal(0, (await _service.SweepAsync()).RemindersSent); // once per stage

        _clock.Now = _clock.Now.AddDays(5.5); // half a day left: the 1-day reminder
        Assert.Equal(1, (await _service.SweepAsync()).RemindersSent);

        _clock.Now = _clock.Now.AddDays(1); // past 30 days
        var swept = await _service.SweepAsync();
        Assert.Equal((2, 24L), (swept.ExpiredLines, swept.ExpiredUnits));
        Assert.Equal(0, Amount(_db, InboxId, OakLog));
        Assert.Equal(0, Amount(_db, InboxId, Stone));
        var expired = _db.StockTransactions.AsNoTracking().Where(t => t.Reason == StockReason.InboxExpired).ToList();
        Assert.Equal(2, expired.Count);
        Assert.All(_db.StockMoves.AsNoTracking().Where(m => m.Reason == StockReason.InboxExpired),
            m => Assert.Equal((InboxId, Sys(Models.Storage.SysLoss)), (m.FromStorageId, m.ToStorageId)));
        var lines = (await _service.GetAsync(d.Id))!.Lines;
        Assert.Equal((20L, 0L, 10L), (lines[0].Expired, lines[0].Remaining, lines[0].Claimed));
        Assert.Equal(InboxNotificationDto.EventExpired, Inbox().Last().Event);
        Assert.Equal(24L, Inbox().Last().LostUnits);
        Assert.Equal(0, (await _service.SweepAsync()).ExpiredLines); // a rerun finds nothing
        await AssertReconciledAsync();
    }

    [Fact]
    public async Task TheStash_IsSeparate_DepositsUpToItsCap_AndWithdrawsOncePerKey()
    {
        // Without a premium tier the stash has no room.
        var none = await _service.StashMoveAsync(PlayerId, true, Move("d0", (OakLog, 1)), Server);
        Assert.Equal((StockResultStatus.Rejected, StockRejectReason.CapacityFull), (none.Result.Status, none.Result.Lines[0].RejectReason));

        _db.UserPermissionGroups.Add(new UserPermissionGroup { UserId = PlayerId, PermissionGroupId = GoldTier });
        _db.SaveChanges();
        _db.ChangeTracker.Clear();
        var put = await _service.StashMoveAsync(PlayerId, true, Move("d1", (OakLog, 250)), Server);
        Assert.Equal((StockResultStatus.PartiallyApplied, 200L), (put.Result.Status, put.Result.Accepted));
        Assert.Equal((StoragePurpose.Stash, 200L, 100), (put.View.Purpose, put.View.CapUnits, put.View.FillPercent));
        Assert.NotEqual(put.StorageId, (await _service.ViewAsync(PlayerId, StoragePurpose.Inbox)).StorageId ?? -1);

        var take = await _service.StashMoveAsync(PlayerId, false, Move("w1", (OakLog, 50)), DeliveryActor.Staff(PlayerId));
        var again = await _service.StashMoveAsync(PlayerId, false, Move("w1", (OakLog, 50)), DeliveryActor.Staff(PlayerId));
        Assert.True(again.Result.Replayed);
        Assert.Equal(150, Amount(_db, put.StorageId, OakLog));
        var reasons = _db.StockTransactions.AsNoTracking().Where(t => t.SourceType == "Stash").Select(t => t.Reason).ToList();
        Assert.Equal(new[] { StockReason.Deposit, StockReason.Withdraw }, reasons.OrderBy(r => r).ToArray());
        Assert.Equal(StockActorKind.User, _db.StockTransactions.AsNoTracking().Single(t => t.Id == take.Result.TransactionId).ActorKind);
        await AssertReconciledAsync();
    }

    [Fact]
    public async Task WithTheFlagOff_NothingMoves_ButTheViewsWork()
    {
        await _service.DeliverAsync(Request("before", (OakLog, 5)), Server);
        Settings(new ProductionDeliverySettingsDto { InboxBaseCapUnits = 100 }, enabled: false);

        await Assert.ThrowsAsync<ProductionOffException>(() => _service.DeliverAsync(Request("x", (OakLog, 1)), Server));
        await Assert.ThrowsAsync<ProductionOffException>(() => _service.ClaimAsync(PlayerId, Move("y", (OakLog, 1)), Server));
        await Assert.ThrowsAsync<ProductionOffException>(() => _service.StashMoveAsync(PlayerId, true, Move("z", (OakLog, 1)), Server));
        _clock.Now = _clock.Now.AddDays(40);
        Assert.Equal(0, (await _service.SweepAsync()).ExpiredLines);
        Assert.Equal(5, Assert.Single((await _service.ViewAsync(PlayerId, StoragePurpose.Inbox)).Entries).Amount);
    }

    [Fact]
    public async Task BadRequests_AreRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DeliverAsync(Request("", (OakLog, 1)), Server));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DeliverAsync(Request("has space", (OakLog, 1)), Server));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DeliverAsync(Request("k"), Server));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DeliverAsync(Request("k", (OakLog, 0)), Server));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.DeliverAsync(Request("k", (9999, 1)), Server));
        var stranger = Request("k", (OakLog, 1));
        stranger.UserId = 4242;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.DeliverAsync(stranger, Server));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.ClaimAsync(PlayerId, Move("k", (OakLog, 1)), Server)); // no inbox yet
    }

    [Fact]
    public async Task ALockedInbox_RefusesTheDelivery_AndNothingMoves()
    {
        await _service.DeliverAsync(Request("first", (OakLog, 1)), Server);
        var inbox = _db.Storages.Single(s => s.Id == InboxId);
        inbox.LockedUntil = _clock.Now.UtcDateTime.AddHours(1);
        inbox.LockReason = "investigation";
        _db.SaveChanges();
        _db.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeliverAsync(Request("second", (OakLog, 1)), Server));
        Assert.Equal(1, Amount(_db, InboxId, OakLog));
        Assert.Single(_db.Set<ItemDelivery>().AsNoTracking());
    }

    [Fact]
    public void TheSettings_AreValidated_AndAMissingSectionKeepsTheStoredOne()
    {
        var stored = new ProductionSettingsDto { Delivery = new ProductionDeliverySettingsDto { InboxBaseCapUnits = 7 } };
        Assert.Equal(7, ProductionSettings.Normalize(new ProductionSettingsDto(), stored).Delivery!.InboxBaseCapUnits);
        Assert.Throws<ArgumentException>(() => ProductionSettings.Normalize(new ProductionSettingsDto
        {
            Delivery = new ProductionDeliverySettingsDto { ReminderDaysBefore = new() { 30 } },
        }));
        Assert.Throws<ArgumentException>(() => ProductionSettings.Normalize(new ProductionSettingsDto
        {
            Delivery = new ProductionDeliverySettingsDto
            {
                PremiumTiers = new() { new PremiumStorageCapDto { PermissionGroupId = 1 }, new PremiumStorageCapDto { PermissionGroupId = 1 } },
            },
        }));
        Assert.Equal(30, ProductionSettings.Read(null).Delivery!.ExpiryDays);
        Assert.Equal(80, ItemDeliveryService.PassedWarning(new[] { 80, 95 }, 70, 85, 100));
        Assert.Null(ItemDeliveryService.PassedWarning(new[] { 80, 95 }, 81, 90, 100));
    }
}
