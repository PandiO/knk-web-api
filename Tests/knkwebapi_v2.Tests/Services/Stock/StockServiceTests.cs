using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Stock;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static knkwebapi_v2.Tests.Services.Stock.StockTestDb;

namespace knkwebapi_v2.Tests.Services.Stock;

/// <summary>
/// The stock service (production DESIGN § 4.3-4.5, PandiO/knk-workspace#97): double-entry moves in one transaction,
/// idempotency, capacity in raw units × storage weight as a result value, partial vs all-or-nothing, locks, unique items,
/// compensating corrections and the stored totals. Acceptance A01-A03; smoke § 2.1 steps 2-7.
/// </summary>
public class StockServiceTests : IDisposable
{
    private readonly KnKDbContext _db = Create();
    private readonly TestClock _clock = new();
    private readonly StockService _stock;

    public StockServiceTests()
    {
        _stock = new StockService(_db, _clock);
    }

    public void Dispose() => _db.Dispose();

    private async Task<long> Fill(int storage, int blueprint, long amount)
    {
        var r = await _stock.AdjustAsync(storage, blueprint, null, amount, Adjust());
        Assert.Equal(StockResultStatus.Applied, r.Status);
        return r.TransactionId!.Value;
    }

    private async Task AssertReconciled()
    {
        var report = await new StockReconciler(_db, _clock).RunAsync();
        Assert.True(report.Clean, System.Text.Json.JsonSerializer.Serialize(report));
    }

    [Fact]
    public async Task Adjust_PutsStockIn_FromSysAdjustment_AsOneAdminAdjustTransaction()
    {
        var result = await _stock.AdjustAsync(S1, OakLog, null, 64, Adjust("found in the cellar"));

        Assert.Equal(StockResultStatus.Applied, result.Status);
        Assert.Equal(64, Amount(_db, S1, OakLog));
        int sys = await _stock.GetSystemStorageIdAsync(Models.Storage.SysAdjustment);
        Assert.Equal(-64, Amount(_db, sys, OakLog));
        var tx = await _db.StockTransactions.Include(t => t.Moves).SingleAsync();
        Assert.Equal(StockReason.AdminAdjust, tx.Reason);
        Assert.Equal(PlayerId, tx.ActorUserId);
        Assert.Equal("found in the cellar", tx.Note);
        var move = Assert.Single(tx.Moves);
        Assert.Equal((sys, S1, 64L), (move.FromStorageId, move.ToStorageId, move.Amount));
        await AssertReconciled();
    }

    [Fact]
    public async Task Adjust_NeedsANote_AndANonZeroAmount()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _stock.AdjustAsync(S1, OakLog, null, 5, Adjust(" ")));
        await Assert.ThrowsAsync<ArgumentException>(() => _stock.AdjustAsync(S1, OakLog, null, 0, Adjust()));
    }

    [Fact]
    public async Task Move_DebitsOneStorage_AndCreditsAnother_InOneTransaction()
    {
        await Fill(S1, OakLog, 64);

        var result = await _stock.MoveAsync(StockLineRequest.Fungible(S1, S2, OakLog, 30), Ctx());

        Assert.Equal(StockResultStatus.Applied, result.Status);
        Assert.Equal(34, Amount(_db, S1, OakLog));
        Assert.Equal(30, Amount(_db, S2, OakLog));
        var tx = await _db.StockTransactions.Include(t => t.Moves).SingleAsync(t => t.Id == result.TransactionId);
        var move = Assert.Single(tx.Moves);
        Assert.Equal((S1, S2, 30L, StockReason.Distribution), (move.FromStorageId, move.ToStorageId, move.Amount, move.Reason));
        await AssertReconciled();
    }

    [Fact]
    public async Task Move_WithTheSameIdempotencyKey_AppliesOnce_AndReplaysTheFirstResult()
    {
        await Fill(S1, OakLog, 64);
        var first = await _stock.MoveAsync(StockLineRequest.Fungible(S1, S2, OakLog, 30), Ctx(key: "smoke:2.1.4"));

        var again = await _stock.MoveAsync(StockLineRequest.Fungible(S1, S2, OakLog, 30), Ctx(key: "smoke:2.1.4"));

        Assert.True(again.Replayed);
        Assert.Equal(first.TransactionId, again.TransactionId);
        Assert.Equal(30, again.Lines[0].Accepted);
        Assert.Equal(34, Amount(_db, S1, OakLog));
        Assert.Equal(30, Amount(_db, S2, OakLog));
        Assert.Equal(2, await _db.StockTransactions.CountAsync());
    }

    [Fact]
    public async Task Move_WithAKeyUsedForAnotherRequest_IsRefused()
    {
        await Fill(S1, OakLog, 64);
        await _stock.MoveAsync(StockLineRequest.Fungible(S1, S2, OakLog, 30), Ctx(key: "k1"));

        await Assert.ThrowsAsync<StockIdempotencyConflictException>(() =>
            _stock.MoveAsync(StockLineRequest.Fungible(S1, S2, OakLog, 31), Ctx(key: "k1")));
        Assert.Equal(30, Amount(_db, S2, OakLog));
    }

    [Fact]
    public async Task Move_OverCapacity_IsRefusedAllOrNothing_AndPartialAcceptsWhatFits()
    {
        await Fill(S1, OakLog, 64);
        await _stock.MoveAsync(StockLineRequest.Fungible(S1, S2, OakLog, 30), Ctx());

        var strict = await _stock.MoveAsync(StockLineRequest.Fungible(S1, S2, OakLog, 40), Ctx());
        Assert.Equal(StockResultStatus.Rejected, strict.Status);
        Assert.Null(strict.TransactionId);
        Assert.Equal((0L, 40L, StockRejectReason.CapacityFull), (strict.Lines[0].Accepted, strict.Lines[0].Rejected, strict.Lines[0].RejectReason));
        Assert.Equal(30, Amount(_db, S2, OakLog));

        var partial = await _stock.MoveAsync(StockLineRequest.Fungible(S1, S2, OakLog, 40), Ctx(), StockMoveMode.Partial);
        Assert.Equal(StockResultStatus.PartiallyApplied, partial.Status);
        Assert.Equal((20L, 20L, StockRejectReason.CapacityFull), (partial.Lines[0].Accepted, partial.Lines[0].Rejected, partial.Lines[0].RejectReason));
        Assert.Equal(50, Amount(_db, S2, OakLog));
        Assert.Equal(14, Amount(_db, S1, OakLog));
        await AssertReconciled();
    }

    [Fact]
    public async Task Capacity_CountsRawUnitsTimesStorageWeight()
    {
        // Stone weighs 2: S2 (cap 50) holds 25 stones.
        var result = await _stock.MoveAsync(StockLineRequest.Fungible(await _stock.GetSystemStorageIdAsync(Models.Storage.SysProduction),
            S2, Stone, 30), Ctx(StockReason.Production), StockMoveMode.Partial);

        Assert.Equal(25, result.Lines[0].Accepted);
        Assert.Equal(StockRejectReason.CapacityFull, result.Lines[0].RejectReason);
        // Oak (weight 1) no longer fits either: 25 × 2 = 50 = the cap.
        var oak = await _stock.AdjustAsync(S2, OakLog, null, 1, Adjust());
        Assert.Equal(StockResultStatus.Rejected, oak.Status);
    }

    [Fact]
    public async Task DistinctCap_RefusesANewItem_ButTakesMoreOfAKnownOne()
    {
        var s = await _db.Storages.SingleAsync(x => x.Id == S1);
        s.MaxDistinct = 1;
        await _db.SaveChangesAsync();
        await Fill(S1, OakLog, 10);

        var stone = await _stock.AdjustAsync(S1, Stone, null, 1, Adjust());
        Assert.Equal(StockRejectReason.DistinctCapReached, stone.Lines[0].RejectReason);
        await Fill(S1, OakLog, 5);
        Assert.Equal(15, Amount(_db, S1, OakLog));
        // A unique item is one more distinct item too.
        var sword = await _stock.AdjustAsync(S1, null, SwordInstance, 1, Adjust());
        Assert.Equal(StockRejectReason.DistinctCapReached, sword.Lines[0].RejectReason);
    }

    [Fact]
    public async Task NotEnoughStock_IsAResult_AndPartialMovesWhatThereIs()
    {
        await Fill(S1, OakLog, 10);

        var strict = await _stock.MoveAsync(StockLineRequest.Fungible(S1, S2, OakLog, 15), Ctx());
        Assert.Equal(StockRejectReason.NotEnoughStock, strict.Lines[0].RejectReason);
        var partial = await _stock.MoveAsync(StockLineRequest.Fungible(S1, S2, OakLog, 15), Ctx(), StockMoveMode.Partial);
        Assert.Equal((10L, 5L, StockRejectReason.NotEnoughStock), (partial.Lines[0].Accepted, partial.Lines[0].Rejected, partial.Lines[0].RejectReason));
        Assert.Equal(0, Amount(_db, S1, OakLog));
        Assert.False(await _db.StockLines.AnyAsync(l => l.StorageId == S1), "a line at zero is deleted");
    }

    [Fact]
    public async Task LockedStorage_RefusesMovesInAndOut_UntilTheLockEnds()
    {
        await Fill(S1, OakLog, 10);
        var s = await _db.Storages.SingleAsync(x => x.Id == S2);
        s.LockedUntil = _clock.Now.UtcDateTime.AddHours(1);
        await _db.SaveChangesAsync();

        var into = await _stock.MoveAsync(StockLineRequest.Fungible(S1, S2, OakLog, 5), Ctx(), StockMoveMode.Partial);
        Assert.Equal(StockRejectReason.Locked, into.Lines[0].RejectReason);
        Assert.Equal(StockResultStatus.Rejected, into.Status);

        _clock.Now = _clock.Now.AddHours(2);
        var later = await _stock.MoveAsync(StockLineRequest.Fungible(S1, S2, OakLog, 5), Ctx());
        Assert.Equal(StockResultStatus.Applied, later.Status);
    }

    [Fact]
    public async Task UniqueItem_KeepsItsIdentity_ThroughDepositMoveAndWithdrawal()
    {
        int world = await _stock.GetSystemStorageIdAsync(Models.Storage.SysWorld);

        var deposit = await _stock.MoveAsync(StockLineRequest.Unique(world, S1, SwordInstance), Ctx(StockReason.Deposit));
        Assert.Equal(StockResultStatus.Applied, deposit.Status);
        Assert.Equal(S1, (await _db.StockUnits.AsNoTracking().SingleAsync()).StorageId);

        await _stock.MoveAsync(StockLineRequest.Unique(S1, S2, SwordInstance), Ctx());
        var unit = await _db.StockUnits.AsNoTracking().SingleAsync();
        Assert.Equal((S2, SwordInstance), (unit.StorageId, unit.ItemInstanceId));
        Assert.Equal((1L, 1), (Storage(_db, S2).TotalUnits, Storage(_db, S2).DistinctItems));

        // It is not in S1 any more, so S1 can't give it again.
        var stale = await _stock.MoveAsync(StockLineRequest.Unique(S1, world, SwordInstance), Ctx(StockReason.Withdraw));
        Assert.Equal(StockRejectReason.NotEnoughStock, stale.Lines[0].RejectReason);

        var withdraw = await _stock.MoveAsync(StockLineRequest.Unique(S2, world, SwordInstance), Ctx(StockReason.Withdraw));
        Assert.Equal(StockResultStatus.Applied, withdraw.Status);
        Assert.False(await _db.StockUnits.AnyAsync());
        Assert.Equal(3, await _db.StockMoves.CountAsync(m => m.ItemInstanceId == SwordInstance));
        Assert.Equal("Dawnbreaker", (await _db.ItemInstances.AsNoTracking().SingleAsync(i => i.Id == SwordInstance)).CustomDisplayName);
        await AssertReconciled();
    }

    [Fact]
    public async Task MoveMany_AllOrNothing_OneShortLine_MovesNothing()
    {
        await Fill(S1, OakLog, 10);
        await Fill(S1, Stone, 5);

        var result = await _stock.MoveManyAsync(new[]
        {
            StockLineRequest.Fungible(S1, Vault, OakLog, 10),
            StockLineRequest.Fungible(S1, Vault, Stone, 6),
        }, Ctx());

        Assert.Equal(StockResultStatus.Rejected, result.Status);
        Assert.Equal(StockRejectReason.OtherLineRejected, result.Lines[0].RejectReason);
        Assert.Equal(StockRejectReason.NotEnoughStock, result.Lines[1].RejectReason);
        Assert.Equal(10, Amount(_db, S1, OakLog));
        Assert.Equal(0, Amount(_db, Vault, OakLog));
    }

    [Fact]
    public async Task MoveMany_IsOneTransaction_AndLaterLinesSeeEarlierOnes()
    {
        await Fill(S1, OakLog, 10);

        var result = await _stock.MoveManyAsync(new[]
        {
            StockLineRequest.Fungible(S1, Vault, OakLog, 10),
            StockLineRequest.Fungible(Vault, ClanVault, OakLog, 4),
        }, Ctx());

        Assert.Equal(StockResultStatus.Applied, result.Status);
        Assert.Equal(6, Amount(_db, Vault, OakLog));
        Assert.Equal(4, Amount(_db, ClanVault, OakLog));
        Assert.Equal(2, await _db.StockMoves.CountAsync(m => m.TransactionId == result.TransactionId));
        await AssertReconciled();
    }

    [Fact]
    public async Task LoweredCap_BelowTheStock_AllowsRemovalsOnly()
    {
        await Fill(S1, OakLog, 80);
        var s = await _db.Storages.SingleAsync(x => x.Id == S1);
        s.MaxUnits = 50;
        await _db.SaveChangesAsync();

        Assert.Equal(StockResultStatus.Rejected, (await _stock.AdjustAsync(S1, OakLog, null, 1, Adjust())).Status);
        Assert.Equal(StockResultStatus.Applied, (await _stock.MoveAsync(StockLineRequest.Fungible(S1, Vault, OakLog, 10), Ctx())).Status);
        Assert.Equal(70, Amount(_db, S1, OakLog));
    }

    [Fact]
    public async Task Reverse_WritesACompensatingCorrection_Once()
    {
        await Fill(S1, OakLog, 20);
        var move = await _stock.MoveAsync(StockLineRequest.Fungible(S1, S2, OakLog, 8), Ctx());

        var reverse = await _stock.ReverseAsync(move.TransactionId!.Value, Ctx(StockReason.Correction));

        Assert.Equal(StockResultStatus.Applied, reverse.Status);
        Assert.Equal(20, Amount(_db, S1, OakLog));
        Assert.Equal(0, Amount(_db, S2, OakLog));
        var correction = await _db.StockTransactions.SingleAsync(t => t.Id == reverse.TransactionId);
        Assert.Equal((StockReason.Correction, move.TransactionId), (correction.Reason, correction.ReversesTransactionId));
        // The original is untouched (append-only) and can't be reversed twice; a repeat replays.
        Assert.Equal(1, await _db.StockMoves.CountAsync(m => m.TransactionId == move.TransactionId));
        Assert.True((await _stock.ReverseAsync(move.TransactionId.Value, Ctx(StockReason.Correction))).Replayed);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _stock.ReverseAsync(move.TransactionId.Value, Ctx(StockReason.Correction, key: "another-key")));
        await AssertReconciled();
    }

    [Fact]
    public async Task Reverse_WhenTheStockIsGone_IsRejected()
    {
        await Fill(S1, OakLog, 20);
        var move = await _stock.MoveAsync(StockLineRequest.Fungible(S1, S2, OakLog, 8), Ctx());
        await _stock.MoveAsync(StockLineRequest.Fungible(S2, Vault, OakLog, 8), Ctx());

        var reverse = await _stock.ReverseAsync(move.TransactionId!.Value, Ctx(StockReason.Correction));

        Assert.Equal(StockResultStatus.Rejected, reverse.Status);
        Assert.Equal(StockRejectReason.NotEnoughStock, reverse.Lines[0].RejectReason);
    }

    [Fact]
    public async Task VirtualStorages_GoNegative_RealStoragesNever_AndEachBlueprintSumsToZero()
    {
        int production = await _stock.GetSystemStorageIdAsync(Models.Storage.SysProduction);
        int consumption = await _stock.GetSystemStorageIdAsync(Models.Storage.SysConsumption);
        await _stock.MoveAsync(StockLineRequest.Fungible(production, S1, OakLog, 40), Ctx(StockReason.Production));
        await _stock.MoveAsync(StockLineRequest.Fungible(S1, consumption, OakLog, 15), Ctx(StockReason.ProductionInput));

        Assert.Equal(-40, Amount(_db, production, OakLog));
        Assert.Equal(15, Amount(_db, consumption, OakLog));
        Assert.Equal(25, Amount(_db, S1, OakLog));
        Assert.Equal(0, await _db.StockLines.Where(l => l.ItemBlueprintId == OakLog).SumAsync(l => l.Amount));
        await AssertReconciled();
    }

    [Fact]
    public async Task StoredTotals_AndRowVersion_FollowEveryMove()
    {
        long before = Storage(_db, S1).RowVersion;
        await Fill(S1, OakLog, 10);
        await Fill(S1, Stone, 3);
        await _stock.MoveAsync(StockLineRequest.Fungible(S1, Vault, OakLog, 10), Ctx());

        var s1 = Storage(_db, S1);
        Assert.Equal((3L, 1), (s1.TotalUnits, s1.DistinctItems));
        Assert.Equal(before + 3, s1.RowVersion);
        Assert.Equal((10L, 1), (Storage(_db, Vault).TotalUnits, Storage(_db, Vault).DistinctItems));
    }

    [Fact]
    public async Task TransitStorages_AreNotCapped()
    {
        var transit = new Models.Storage { Name = "Cargo", Kind = StorageKind.Transit };
        _db.Storages.Add(transit);
        await _db.SaveChangesAsync();
        int production = await _stock.GetSystemStorageIdAsync(Models.Storage.SysProduction);

        var r = await _stock.MoveAsync(StockLineRequest.Fungible(production, transit.Id, Stone, 10_000), Ctx(StockReason.TransportLoad));

        Assert.Equal(StockResultStatus.Applied, r.Status);
    }

    [Theory]
    [InlineData(S1, S1, OakLog, 5L, null)]
    [InlineData(S1, S2, OakLog, 0L, null)]
    [InlineData(S1, S2, OakLog, 1L, SwordInstance)]
    [InlineData(S1, S2, null, 2L, SwordInstance)]
    [InlineData(S1, S2, 999, 1L, null)]
    public async Task MalformedLines_AreRefused(int from, int to, int? blueprint, long amount, long? instance)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _stock.MoveAsync(new StockLineRequest(from, to, blueprint, amount, instance), Ctx()));
    }

    [Fact]
    public async Task MissingStorage_IsNotFound_AndReasonRulesHold()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _stock.MoveAsync(StockLineRequest.Fungible(S1, 9999, OakLog, 1), Ctx()));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _stock.MoveAsync(StockLineRequest.Fungible(S1, S2, OakLog, 1), Ctx(StockReason.Correction)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _stock.MoveAsync(StockLineRequest.Fungible(S1, S2, OakLog, 1), Ctx(StockReason.AdminAdjust)));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            _stock.MoveAsync(StockLineRequest.Fungible(S1, S2, OakLog, 1), Ctx(key: "has spaces")));
    }

    [Fact]
    public async Task SystemStorages_AreCreatedOnce()
    {
        int a = await _stock.GetSystemStorageIdAsync(Models.Storage.SysLoss);
        int b = await _stock.GetSystemStorageIdAsync(Models.Storage.SysLoss);

        Assert.Equal(a, b);
        Assert.Equal(StorageKind.Virtual, Storage(_db, a).Kind);
        await Assert.ThrowsAsync<ArgumentException>(() => _stock.GetSystemStorageIdAsync("SYS_NOPE"));
    }
}
