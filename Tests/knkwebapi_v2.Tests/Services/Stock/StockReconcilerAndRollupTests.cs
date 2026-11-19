using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static knkwebapi_v2.Tests.Services.Stock.StockTestDb;

namespace knkwebapi_v2.Tests.Services.Stock;

/// <summary>
/// The reconciler (DESIGN § 4.3: per blueprint the sum is zero; each line equals the net of its moves), the 30-day
/// roll-up of Production moves (G3, A22, smoke § 2.1 step 8) and the flag gate of the background work (G16).
/// </summary>
public class StockReconcilerAndRollupTests : IDisposable
{
    private readonly KnKDbContext _db = Create();
    private readonly TestClock _clock = new();
    private readonly StockService _stock;

    public StockReconcilerAndRollupTests()
    {
        _stock = new StockService(_db, _clock);
    }

    public void Dispose() => _db.Dispose();

    private Task<StockReconciliationReport> Reconcile() => new StockReconciler(_db, _clock).RunAsync();

    [Fact]
    public async Task Reconciler_IsClean_AfterEveryKindOfPath()
    {
        int production = await _stock.GetSystemStorageIdAsync(Models.Storage.SysProduction);
        int world = await _stock.GetSystemStorageIdAsync(Models.Storage.SysWorld);
        var random = new Random(97);
        int[] real = { S1, S2, Vault, ClanVault };
        await _stock.MoveAsync(StockLineRequest.Fungible(production, Vault, OakLog, 500), Ctx(StockReason.Production));
        await _stock.MoveAsync(StockLineRequest.Unique(world, ClanVault, SwordInstance), Ctx(StockReason.Deposit));
        for (int i = 0; i < 200; i++)
        {
            int from = real[random.Next(real.Length)], to = real[random.Next(real.Length)];
            if (from == to) continue;
            await _stock.MoveAsync(StockLineRequest.Fungible(from, to, i % 3 == 0 ? Stone : OakLog, random.Next(1, 40)), Ctx(),
                StockMoveMode.Partial);
            if (i % 25 == 0) await _stock.AdjustAsync(real[random.Next(real.Length)], Stone, null, random.Next(1, 5), Adjust());
        }
        var anyTx = await _db.StockTransactions.Where(t => t.Reason == StockReason.Distribution).Select(t => t.Id).FirstAsync();
        await _stock.ReverseAsync(anyTx, Ctx(StockReason.Correction));

        var report = await Reconcile();

        Assert.True(report.Clean, System.Text.Json.JsonSerializer.Serialize(report));
        Assert.True(report.LinesChecked > 4);
    }

    [Fact]
    public async Task Reconciler_FindsALineThatWasChangedOutsideTheLedger()
    {
        await _stock.AdjustAsync(S1, OakLog, null, 10, Adjust());
        var line = await _db.StockLines.SingleAsync(l => l.StorageId == S1);
        line.Amount = 12;
        await _db.SaveChangesAsync();

        var report = await Reconcile();

        Assert.False(report.Clean);
        var diff = Assert.Single(report.LineDifferences);
        Assert.Equal((S1, OakLog, 12L, 10L), (diff.StorageId, diff.ItemBlueprintId, diff.LineAmount, diff.LedgerNet));
        Assert.Equal(2, Assert.Single(report.BlueprintImbalances).Sum);
        Assert.Equal(S1, Assert.Single(report.TotalDifferences).StorageId);
    }

    [Fact]
    public async Task Reconciler_FindsAUniqueItemThatMovedOutsideTheLedger()
    {
        int world = await _stock.GetSystemStorageIdAsync(Models.Storage.SysWorld);
        await _stock.MoveAsync(StockLineRequest.Unique(world, S1, SwordInstance), Ctx(StockReason.Deposit));
        var unit = await _db.StockUnits.SingleAsync();
        unit.StorageId = S2;
        await _db.SaveChangesAsync();

        var report = await Reconcile();

        var diff = Assert.Single(report.UnitDifferences);
        Assert.Equal((SwordInstance, (int?)S2, (int?)S1), (diff.ItemInstanceId, diff.StorageId, diff.LedgerStorageId));
    }

    [Fact]
    public async Task Rollup_SumsOldProductionMovesPerLinePerDay_AndKeepsTotals()
    {
        int production = await _stock.GetSystemStorageIdAsync(Models.Storage.SysProduction);
        int consumption = await _stock.GetSystemStorageIdAsync(Models.Storage.SysConsumption);
        var start = _clock.Now;
        _clock.Now = start.AddDays(-40).AddHours(-1);
        for (int i = 0; i < 5; i++) await _stock.MoveAsync(StockLineRequest.Fungible(production, Vault, OakLog, 10), Ctx(StockReason.Production));
        await _stock.MoveAsync(StockLineRequest.Fungible(Vault, consumption, OakLog, 4), Ctx(StockReason.ProductionInput));
        await _stock.MoveAsync(StockLineRequest.Fungible(Vault, ClanVault, OakLog, 6), Ctx(StockReason.Distribution));
        _clock.Now = start.AddDays(-39);
        await _stock.MoveAsync(StockLineRequest.Fungible(production, Vault, OakLog, 7), Ctx(StockReason.Production));
        _clock.Now = start.AddDays(-2);
        await _stock.MoveAsync(StockLineRequest.Fungible(production, Vault, OakLog, 3), Ctx(StockReason.Production));
        _clock.Now = start;
        var before = (Amount(_db, Vault, OakLog), Storage(_db, Vault).TotalUnits);

        var result = await new StockRollupService(_db, _clock).RollUpAsync();

        Assert.Equal(7, result.TransactionsRolledUp);
        Assert.Equal(3, result.RollupRowsWritten); // day -40 production, day -40 input, day -39 production
        var rows = await _db.StockMoveRollups.AsNoTracking().OrderBy(r => r.Day).ThenBy(r => r.Reason).ToListAsync();
        Assert.Equal((StockReason.Production, 50L, 5), (rows[0].Reason, rows[0].Amount, rows[0].MoveCount));
        Assert.Equal((StockReason.ProductionInput, 4L, 1), (rows[1].Reason, rows[1].Amount, rows[1].MoveCount));
        Assert.Equal((7L, 1), (rows[2].Amount, rows[2].MoveCount));
        Assert.Equal(new DateTime(2026, 10, 10), rows[0].Day);
        // Distribution and the recent production keep every row; totals are unchanged.
        Assert.Equal(1, await _db.StockTransactions.CountAsync(t => t.Reason == StockReason.Distribution));
        Assert.Equal(1, await _db.StockTransactions.CountAsync(t => t.Reason == StockReason.Production));
        Assert.Equal(before, (Amount(_db, Vault, OakLog), Storage(_db, Vault).TotalUnits));
        Assert.True((await Reconcile()).Clean);

        var again = await new StockRollupService(_db, _clock).RollUpAsync();
        Assert.Equal(0, again.TransactionsRolledUp);
    }

    [Fact]
    public async Task Rollup_AddsToTheSameDaysRow_AndSkipsReversedTransactions()
    {
        int production = await _stock.GetSystemStorageIdAsync(Models.Storage.SysProduction);
        var start = _clock.Now;
        _clock.Now = start.AddDays(-35);
        await _stock.MoveAsync(StockLineRequest.Fungible(production, Vault, OakLog, 10), Ctx(StockReason.Production));
        var reversed = await _stock.MoveAsync(StockLineRequest.Fungible(production, Vault, OakLog, 2), Ctx(StockReason.Production));
        await _stock.ReverseAsync(reversed.TransactionId!.Value, Ctx(StockReason.Correction));
        _clock.Now = start;
        await new StockRollupService(_db, _clock).RollUpAsync();
        _clock.Now = start.AddDays(-35);
        await _stock.MoveAsync(StockLineRequest.Fungible(production, Vault, OakLog, 5), Ctx(StockReason.Production));
        _clock.Now = start;

        await new StockRollupService(_db, _clock).RollUpAsync();

        var row = await _db.StockMoveRollups.AsNoTracking().SingleAsync();
        Assert.Equal((15L, 2), (row.Amount, row.MoveCount));
        Assert.True(await _db.StockTransactions.AnyAsync(t => t.Id == reversed.TransactionId), "a reversed transaction keeps its rows");
        Assert.True((await Reconcile()).Clean);
    }

    [Fact]
    public void RollupCutoff_IsTheStartOfTheDay30DaysBack()
    {
        Assert.Equal(new DateTime(2026, 10, 20), StockRollupService.CutoffFor(new DateTime(2026, 11, 19, 18, 30, 0, DateTimeKind.Utc)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MaintenanceJob_RunsOnlyWithProductionEnabled(bool enabled)
    {
        using var db = Create(productionEnabled: enabled);
        var job = new StockMaintenanceJob(new ProductionSettingsReader(db), new StockReconciler(db, _clock), new StockRollupService(db, _clock));

        var outcome = await job.RunAsync(reconcile: true, rollUp: true);

        Assert.Equal(enabled, outcome.ProductionEnabled);
        Assert.Equal(enabled, outcome.Reconciliation != null);
        Assert.Equal(enabled, outcome.Rollup != null);
    }
}
