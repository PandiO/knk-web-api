using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Services.Stock;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using static knkwebapi_v2.Tests.Services.Production.ProductionEngineTestDb;

namespace knkwebapi_v2.Tests.Services.Production;

/// <summary>
/// The production engine (PandiO/knk-workspace#86, PRODUCTION_KINDS § 4.3, § 5): drift-free anchored accrual, the
/// catch-up cap, stall when full without accruing (D4), inputs, the worker factor, level multipliers, cycle speed and
/// unlocks, the cycle trace, notifications and the Production:Enabled gate (A08, A09, A21; smoke § 4.1).
/// </summary>
public class ProductionEngineTests : IDisposable
{
    private readonly ProductionEngineTestDb _t = new();

    public void Dispose() => _t.Dispose();

    private Task<ProductionSweepResultDto> Sweep(bool all = false) => _t.Engine.SweepAsync(all);

    [Fact]
    public async Task Accrual_IsDriftFree_OverSixHoursOfTenMinuteSweeps_EvenWhenTheSweepRunsLate()
    {
        var (_, line, output) = await _t.QuarryAsync(cap: null);
        var anchor = _t.Line(line).AnchorAt;
        for (int i = 1; i <= 36; i++)
        {
            _t.Advance(TimeSpan.FromMinutes(10).Add(TimeSpan.FromSeconds(i % 3 == 0 ? 7 : 0)));
            await Sweep();
        }
        // 36 cycles plus 12 × 7 s late: the grid stays anchored at creation (no drift), output follows real time.
        var l = _t.Line(line);
        long seconds = (long)(l.LastSettledAt - anchor).TotalSeconds;
        Assert.Equal((long)Math.Floor(seconds / 60m), _t.Amount(output, Cobblestone));
        Assert.Equal(0, (l.NextSettleAt - anchor).Ticks % TimeSpan.FromMinutes(10).Ticks);
        Assert.True(l.NextSettleAt > l.LastSettledAt && l.NextSettleAt - l.LastSettledAt <= TimeSpan.FromMinutes(10));
    }

    [Fact]
    public async Task Accrual_AtIrregularSettles_KeepsTheFraction_AndSumsToTheRateTimesTheTime()
    {
        var (_, line, output) = await _t.QuarryAsync(rate: 0.3m, cap: null);
        foreach (var minutes in new[] { 7, 13, 41, 120, 59, 360, 1 })
        {
            _t.Advance(minutes);
            await _t.Engine.SettleLineAsync(line);
        }
        // 601 minutes at 0.3 per hour = 3.005: three units emitted, 0.005 kept.
        Assert.Equal(3, _t.Amount(output, Cobblestone));
        Assert.Equal(0.005m, _t.Line(line).Accrued);
    }

    [Fact]
    public async Task ASweepOnlySettlesDueLines_AndASecondSettleAtTheSameMoment_ProducesNothingMore()
    {
        var (_, line, output) = await _t.QuarryAsync(cap: null);
        _t.Advance(5);
        Assert.Equal(0, (await Sweep()).LinesSettled); // not due yet (cycle 10 min)
        _t.Advance(5);
        Assert.Equal(1, (await Sweep()).LinesSettled);
        Assert.Null(await _t.Engine.SettleLineAsync(line));
        Assert.Equal(10, _t.Amount(output, Cobblestone));
        Assert.Single(_t.Records(line));
    }

    [Fact]
    public async Task CatchUp_IsCapped_AndTheTraceSaysHowMuchWasSkipped()
    {
        var (_, line, output) = await _t.QuarryAsync(cap: null);
        _t.Advance(TimeSpan.FromHours(48));
        await Sweep();
        Assert.Equal(24 * 60, _t.Amount(output, Cobblestone));
        var record = Assert.Single(_t.Records(line));
        Assert.Equal(24 * 3600, record.ElapsedSeconds);
        Assert.Equal(24 * 3600, record.SkippedSeconds);
        Assert.Contains("Catch-up capped", record.ShortfallReason);
    }

    [Fact]
    public async Task AFullOutputStorage_StallsTheLineWithoutAccruing_AndItResumesWithoutCatchUp()
    {
        var (sid, line, output) = await _t.QuarryAsync(); // cap 100 (smoke § 4.1 steps 4-5)
        _t.Advance(TimeSpan.FromHours(2));
        await Sweep();
        Assert.Equal(100, _t.Amount(output, Cobblestone));
        var l = _t.Line(line);
        Assert.Equal(ProductionLineStatus.BlockedFull, l.Status);
        Assert.Equal(0m, l.Accrued);
        var full = _t.Records(line).Last();
        Assert.Equal((120L, 100L), (full.Emitted, full.Accepted));
        // The 20 refused units stay in SYS_PRODUCTION: never produced, the ledger stays balanced.
        int sys = await _t.Stock.GetSystemStorageIdAsync(Storage.SysProduction);
        Assert.Equal(-100, _t.Amount(sys, Cobblestone));

        _t.Advance(TimeSpan.FromHours(3));
        await Sweep();
        Assert.Equal(100, _t.Amount(output, Cobblestone));
        Assert.Equal(ProductionLineStatus.BlockedFull, _t.Line(line).Status);

        await _t.AdjustAsync(output, Cobblestone, -100);
        _t.Advance(10);
        await Sweep();
        Assert.Equal(ProductionLineStatus.Running, _t.Line(line).Status);
        Assert.Equal(0, _t.Amount(output, Cobblestone)); // no catch-up for the stalled time
        _t.Advance(10);
        await Sweep();
        Assert.Equal(10, _t.Amount(output, Cobblestone));

        var overview = (await _t.Engine.OverviewAsync(sid))!;
        Assert.Empty(overview.Lines.Single().NotProducingReasons);
        Assert.True(new StockReconciler(_t.Db, _t.Clock).RunAsync().Result.Clean);
    }

    [Fact]
    public async Task BlockedAndResumed_AreQueuedAsNotifications()
    {
        var (_, _, output) = await _t.QuarryAsync(cap: 10);
        _t.Advance(30);
        await Sweep();
        var blocked = Assert.Single(_t.Notifications.GetPending(), n => n.Type == PlayerNotificationTypes.ProductionLineStatusChanged);
        Assert.Equal(("Running", "BlockedFull", 0), (blocked.ProductionLineStatus!.PreviousStatus, blocked.ProductionLineStatus.Status, blocked.UserId));
        await _t.AdjustAsync(output, Cobblestone, -10);
        _t.Advance(10);
        await Sweep();
        Assert.Contains(_t.Notifications.GetPending(), n => n.ProductionLineStatus?.Status == "Running");
    }

    [Theory]
    [InlineData(2, 60)]
    [InlineData(1, 30)]
    [InlineData(0, 0)]
    public async Task WorkerFactor_IsPresentOverTotal(int present, long expected)
    {
        var (sid, line, output) = await _t.QuarryAsync(cap: null);
        _t.Update(db => db.ProductionStructures.Single(s => s.Id == sid), s => s.WorkersPresent = present);
        _t.Advance(60);
        await Sweep();
        Assert.Equal(expected, _t.Amount(output, Cobblestone));
        Assert.Equal(present / 2m, _t.Records(line).Single().WorkerFactor);
    }

    [Fact]
    public async Task SetWorkersPresent_SettlesFirst_SoTheTimeBeforeCountsAtTheOldFactor()
    {
        var (sid, _, output) = await _t.QuarryAsync(cap: null);
        _t.Advance(30);
        var overview = await _t.Engine.SetWorkersPresentAsync(sid, 1);
        Assert.Equal(30, _t.Amount(output, Cobblestone));
        Assert.Equal(1, overview.WorkersPresent);
        Assert.Contains("Only 1 of 2 workers present", overview.Lines.Single().NotProducingReasons.Single());
        _t.Advance(60);
        await Sweep();
        Assert.Equal(60, _t.Amount(output, Cobblestone));
        await Assert.ThrowsAsync<ArgumentException>(() => _t.Engine.SetWorkersPresentAsync(sid, 3));
    }

    [Fact]
    public async Task TheLevelModifier_MultipliesTheRate_AndTheTraceListsIt()
    {
        var (sid, line, output) = await _t.QuarryAsync(cap: null);
        await _t.UpdateStructureAsync(sid, d => d.Level = 2);
        _t.Advance(60);
        await Sweep();
        Assert.Equal(69, _t.Amount(output, Cobblestone)); // 60 × 1.15 (seeded level 2)
        var trace = ProductionCycleRecordMapping.ToDto(_t.Records(line).Last(), null);
        var level = Assert.Single(trace.Modifiers);
        Assert.Equal(("Level", "Level 2", "Multiply", 1.15m), (level.Source, level.Name, level.Effect, level.Value));
        Assert.Equal(69m, trace.EffectiveRatePerHour);
    }

    [Fact]
    public async Task CycleSpeed_ShortensTheCycle_WithoutChangingTheRatePerHour()
    {
        _t.Update(db => db.ProductionKindLevels.Single(l => l.ProductionKindId == QuarryKind && l.Level == 1), l => l.CycleSpeedMultiplier = 2m);
        var (sid, line, output) = await _t.QuarryAsync(cap: null);
        _t.Advance(5);
        Assert.Equal(1, (await Sweep()).LinesSettled);
        Assert.Equal(5, _t.Amount(output, Cobblestone));
        Assert.Equal(_t.Line(line).AnchorAt.AddMinutes(10), _t.Line(line).NextSettleAt);
        _ = sid;
    }

    [Fact]
    public async Task AnOutputUnlockedAtLevel5_ProducesNothingBelowIt_AndSaysWhy()
    {
        var (sid, line, output) = await _t.QuarryAsync(cap: null);
        var level5 = _t.Db.ProductionKindLevels.AsNoTracking().Single(l => l.ProductionKindId == QuarryKind && l.Level == 5);
        await new ProductionKindLevelService(_t.Db).UpdateAsync(level5.Id, new ProductionKindLevelDto
        {
            Items = new() { new ProductionKindLevelItemDto { Role = "Unlock", ItemBlueprintId = Cobblestone } },
        });
        _t.Advance(60);
        await Sweep();
        Assert.Equal(0, _t.Amount(output, Cobblestone));
        Assert.Equal(0m, _t.Records(line).Single().UnlockFactor);
        var overview = (await _t.Engine.OverviewAsync(sid))!;
        Assert.Contains("Unlocked at level 5", string.Join(" ", overview.Lines.Single().NotProducingReasons));

        await _t.UpdateStructureAsync(sid, d => d.Level = 5);
        _t.Advance(60);
        await Sweep();
        Assert.Equal(96, _t.Amount(output, Cobblestone)); // 60 × 1.6 (seeded level 5)
    }

    [Fact]
    public async Task Inputs_AreTakenFromTheInputStorage_AndAMissingInputBlocksTheLine()
    {
        _t.Update(db => db.ProductionKinds.Single(k => k.Id == MineKind),
            k => k.DefaultStoragesJson = "[{\"purpose\":\"Input\",\"name\":\"inputs\"}]");
        var (sid, line, output) = await _t.QuarryAsync(rate: 60m, cap: null, kind: MineKind, name: "Mine");
        int input = _t.Db.Storages.AsNoTracking().Single(s => s.DomainId == sid && s.Purpose == StoragePurpose.Input).Id;
        await _t.Lines.UpdateAsync(line, new ProductionLineDto { Inputs = new() { new ProductionLineInputDto { ItemBlueprintId = Coal, AmountPerHour = 30m } } });

        _t.Advance(60);
        await Sweep();
        var l = _t.Line(line);
        Assert.Equal(ProductionLineStatus.BlockedInputs, l.Status);
        Assert.Contains("Not enough Coal", l.StatusReason);
        Assert.Equal(0, _t.Amount(output, Cobblestone));

        await _t.AdjustAsync(input, Coal, 100);
        _t.Advance(10);
        await Sweep(); // resumes from now
        Assert.Equal(ProductionLineStatus.Running, _t.Line(line).Status);
        _t.Advance(60);
        await Sweep();
        Assert.Equal(60, _t.Amount(output, Cobblestone));
        Assert.Equal(70, _t.Amount(input, Coal));
        int sink = await _t.Stock.GetSystemStorageIdAsync(Storage.SysConsumption);
        Assert.Equal(30, _t.Amount(sink, Coal));
        Assert.Contains(_t.Db.StockTransactions.AsNoTracking(), t => t.Reason == StockReason.ProductionInput);
    }

    [Fact]
    public async Task TheFeatureOff_SettlesNothing_AndTheOverviewSaysSo()
    {
        var (sid, line, output) = await _t.QuarryAsync(cap: null);
        _t.SetSettings(s => s.Enabled = false);
        _t.Advance(60);
        var result = await Sweep(all: true);
        Assert.False(result.FeatureEnabled);
        Assert.Null(await _t.Engine.SettleLineAsync(line));
        var overview = (await _t.Engine.OverviewAsync(sid))!;
        Assert.Contains("Production:Enabled", overview.NotProducingReasons.Single());
        Assert.Equal(0, _t.Amount(output, Cobblestone));
        Assert.Empty(_t.Db.WeatherRolls);
        Assert.Empty(_t.Records(line));
    }

    [Fact]
    public async Task TheStructureSwitch_AndTheLineSwitch_PauseWithoutAccruing()
    {
        var (sid, line, output) = await _t.QuarryAsync(cap: null);
        await _t.UpdateStructureAsync(sid, d => d.ProductionEnabled = false);
        _t.Advance(60);
        await Sweep();
        Assert.Equal(ProductionLineStatus.Paused, _t.Line(line).Status);
        Assert.Equal(0, _t.Amount(output, Cobblestone)); // the hour switched off accrued nothing
        await _t.UpdateStructureAsync(sid, d => d.ProductionEnabled = true); // resumes from now
        _t.Advance(10);
        await Sweep();
        Assert.Equal(10, _t.Amount(output, Cobblestone));
        await _t.Lines.UpdateAsync(line, new ProductionLineDto { Enabled = false });
        _t.Advance(60);
        await Sweep();
        Assert.Equal(10, _t.Amount(output, Cobblestone));
        Assert.Equal("The line is paused.", _t.Line(line).StatusReason);
        await _t.Lines.UpdateAsync(line, new ProductionLineDto { Enabled = true });
        _t.Advance(30);
        await _t.Engine.SettleLineAsync(line);
        Assert.Equal(40, _t.Amount(output, Cobblestone));
    }

    [Fact]
    public async Task ARunNowSweep_SettlesEveryLine_NotOnlyTheDueOnes()
    {
        var (_, _, output) = await _t.QuarryAsync(cap: null);
        _t.Advance(3);
        var result = await Sweep(all: true);
        Assert.Equal(1, result.LinesSettled);
        Assert.Equal(3, _t.Amount(output, Cobblestone));
    }

    [Fact]
    public async Task ANewStructure_GetsItsKindsWorkers_StoragesAndLevelCaps()
    {
        _t.Update(db => db.ProductionKinds.Single(k => k.Id == QuarryKind), k =>
        {
            k.DefaultWorkers = 3;
            k.DefaultStoragesJson = "[{\"purpose\":\"Input\",\"name\":\"tools\",\"maxUnits\":50}]";
        });
        _t.Update(db => db.ProductionKindLevels.Single(l => l.ProductionKindId == QuarryKind && l.Level == 1), l => l.OutputMaxUnits = 300);
        _t.Update(db => db.ProductionKindLevels.Single(l => l.ProductionKindId == QuarryKind && l.Level == 2), l =>
        {
            l.OutputMaxUnits = 600;
            l.TransportCapacity = 32;
        });
        var s = await _t.Structures.CreateAsync(new ProductionStructureDto
        {
            Name = "Big quarry", Description = "", WgRegionId = "tmp_bq", TownId = ProductionWorldTestDb.Phoenix, ProductionKindId = QuarryKind,
        });
        Assert.Equal((3, 3), (s.WorkersTotal, s.WorkersPresent));
        var storages = _t.Db.Storages.AsNoTracking().Where(x => x.DomainId == s.Id).OrderBy(x => x.Id).ToList();
        Assert.Equal(new[] { StoragePurpose.Output, StoragePurpose.Input }, storages.Select(x => x.Purpose));
        Assert.Equal((300L, 50L), (storages[0].MaxUnits!.Value, storages[1].MaxUnits!.Value));

        await _t.UpdateStructureAsync(s.Id!.Value, d => d.Level = 2);
        Assert.Equal(600, _t.Db.Storages.AsNoTracking().Single(x => x.Id == storages[0].Id).MaxUnits);
        Assert.Equal(32, _t.Db.ProductionStructures.AsNoTracking().Single(x => x.Id == s.Id).TransportCapacity);
        await Assert.ThrowsAsync<ArgumentException>(() => _t.UpdateStructureAsync(s.Id!.Value, d => d.Level = 11));
        await Assert.ThrowsAsync<ArgumentException>(() => _t.UpdateStructureAsync(s.Id!.Value, d => d.ProductionKindId = 999));
    }
}
