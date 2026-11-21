using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Tests.Services.Production;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static knkwebapi_v2.Tests.Services.Logistics.LogisticsTestDb;
using static knkwebapi_v2.Tests.Services.Production.ProductionEngineTestDb;

namespace knkwebapi_v2.Tests.Services.Logistics;

/// <summary>
/// Town logistics P9 (PandiO/knk-workspace#127, TOWN_LOGISTICS § 5, A17; smoke § 10.1): reserve and collect with the
/// consumer's own worker (D-L1), reserves never breached, a reservation can't be taken twice, priority under scarcity,
/// the worker away lowers the consumer's output, expired reservations are released at the next run (F11), the plan
/// explanation, service areas (also the producers' inbound preference), the worker's capacity, the 10-minute schedule
/// (D-L2), dry runs and the flag (A21).
/// </summary>
public class LogisticsPlannerTests : IDisposable
{
    private readonly LogisticsTestDb _l = new();

    public void Dispose() => _l.Dispose();

    private TransportTestDb T => _l.T;

    // ================= Reserve and collect (D-L1, A17) =================

    [Fact]
    public async Task AConsumerAtItsReorderPoint_GetsAReservation_AndItsOwnWorkerCollectsIt()
    {
        await _l.SetUpAsync();
        await T.Put(T.NorthStorage, 100, Flour);
        var setting = await _l.MinMax(_l.BakeryInput, Flour, 20, 60);

        var plan = await _l.Run();
        var e = Entry(plan, _l.BakeryInput, Flour);
        Assert.Equal(("Reserved", 60L, 60L, T.North), (e.Outcome, e.Shortfall, e.Reserved, e.WarehouseId!.Value));
        var r = Assert.Single(_l.Reservations());
        Assert.Equal((LogisticsReservationStatus.Open, 60L, T.NorthStorage, _l.BakeryInput), (r.Status, r.Amount, r.WarehouseStorageId, r.ConsumerStorageId));
        Assert.Equal(100, T.Amount(T.NorthStorage, Flour)); // a reservation holds stock without moving it
        var order = Assert.Single(_l.Distribution());
        Assert.Equal((T.NorthStorage, _l.Bakery, _l.BakeryInput, 60L), (order.SourceStorageId, order.CarrierStructureId!.Value,
            order.Stops.Single().StorageId, order.Lines.Single().Requested));
        Assert.Equal(order.Id, r.TransportOrderId);
        Assert.Equal(order.Id, e.TransportOrderId);
        Assert.Contains($"replenishment setting #{setting.Id}", e.Explanation);

        await T.Sweep(); // the bakery's worker loads at North
        Assert.Equal(1, T.Structure(_l.Bakery).WorkersPresent);
        Assert.Equal(40, T.Amount(T.NorthStorage, Flour));
        T.Advance(100);
        await T.Sweep();
        Assert.Equal(60, T.Amount(_l.BakeryInput, Flour));
        Assert.Equal(2, T.Structure(_l.Bakery).WorkersPresent);

        T.Advance(TimeSpan.FromMinutes(10));
        plan = await _l.Run();
        Assert.Equal(new[] { r.Id }, plan.Collected);
        r = Assert.Single(_l.Reservations());
        Assert.Equal((LogisticsReservationStatus.Collected, 60L, (string?)null), (r.Status, r.Collected, r.OpenKey));
        Assert.Equal("NotNeeded", Entry(plan, _l.BakeryInput, Flour).Outcome); // 60 > the reorder point
        Assert.True(T.LedgerClean());
    }

    [Fact]
    public async Task AReservationCantBeTakenTwice_ASecondRunSeesItWaiting()
    {
        await _l.SetUpAsync();
        await T.Put(T.NorthStorage, 100, Flour);
        await _l.MinMax(_l.BakeryInput, Flour, 20, 60);
        await _l.Run();
        var plan = await _l.Run();
        Assert.Equal("AlreadyReserved", Entry(plan, _l.BakeryInput, Flour).Outcome);
        Assert.Single(_l.Reservations());
        Assert.Single(_l.Distribution());
        // What is on the way counts as stock: once loaded, the line is not short any more.
        await T.Sweep();
        T.Advance(TimeSpan.FromMinutes(10));
        plan = await _l.Run();
        var e = Entry(plan, _l.BakeryInput, Flour);
        Assert.Equal(("NotNeeded", 60L), (e.Outcome, e.Stock));
        Assert.Single(_l.Distribution());
    }

    [Fact]
    public async Task TheWorkerAway_LowersTheConsumersOutput_LikeAProducers()
    {
        await _l.SetUpAsync();
        var line = await E().Lines.CreateAsync(new ProductionLineDto { ProductionStructureId = _l.Bakery, ItemBlueprintId = Cobblestone, ConfiguredRatePerHour = 60m });
        E().Update(db => db.TransportLanes.Single(x => x.FromStructureId == _l.Bakery && x.ToStructureId == T.North), x => x.RoadDistance = 25920m); // 100 min
        await T.Put(T.NorthStorage, 100, Flour);
        await _l.MinMax(_l.BakeryInput, Flour, 20, 60);
        await _l.Run();
        await T.Sweep();
        Assert.Equal(1, T.Structure(_l.Bakery).WorkersPresent);
        T.Advance(TimeSpan.FromMinutes(60));
        await E().Engine.SweepAsync();
        Assert.Equal(0.5m, E().Records(line.Id).Last().WorkerFactor);
    }

    // ================= Reserves (TOWN_LOGISTICS § 4) =================

    [Fact]
    public async Task ATownReserve_IsNeverBreached_AndTheRestGoesToTheNextConsumer()
    {
        await _l.SetUpAsync();
        await T.Put(T.NorthStorage, 100, Flour);
        var policy = await _l.TownPolicy(Reserve(Flour, units: 70));
        await _l.MinMax(_l.BakeryInput, Flour, 20, 60);
        await _l.MinMax(_l.GarrisonInput, Flour, 20, 60);

        var plan = await _l.Run();
        var bakery = Entry(plan, _l.BakeryInput, Flour);
        var garrison = Entry(plan, _l.GarrisonInput, Flour);
        Assert.Equal(30, bakery.Reserved + garrison.Reserved); // 100 − 70
        var held = new[] { bakery, garrison }.Single(x => x.Reserved == 0);
        Assert.Equal("HeldByReserve", held.Outcome);
        int ruleId = policy.Rules!.Single().Id!.Value;
        Assert.Contains($"town reserve 70 (rule #{ruleId})", held.Explanation);
        Assert.Contains(ruleId, held.RuleIds);
        Assert.Equal(policy.Id, plan.PolicyId);
        Assert.Equal("Town", plan.PolicySource);

        // Still never below the reserve on the next runs.
        T.Advance(TimeSpan.FromMinutes(10));
        await _l.Run();
        Assert.Equal(30, _l.Reservations().Sum(r => r.Amount));
    }

    [Fact]
    public async Task AReserveInDaysOfCover_UsesTheAverageDailyOutflow()
    {
        await _l.SetUpAsync();
        await T.Put(T.NorthStorage, 170, Flour);
        await T.Put(T.NorthStorage, -70, Flour); // 70 out over the 7-day window = 10 a day
        await _l.TownPolicy(Reserve(Flour, days: 3m)); // 30 units
        await _l.MinMax(_l.BakeryInput, Flour, 20, 50);
        await _l.MinMax(_l.GarrisonInput, Flour, 20, 50);

        var plan = await _l.DryRun();
        Assert.Equal(70, plan.Entries.Sum(e => e.Reserved)); // 100 − 30
        var supply = await _l.Planner.SupplyAsync(ProductionWorldTestDb.Phoenix);
        var line = supply.Lines.Single(x => x.ItemBlueprintId == Flour);
        Assert.Equal((100L, 30L, 70L, 10m, 10m), (line.Stock, line.ReserveUnits, line.Available, line.OutflowPerDay, line.DaysOfCover!.Value));
    }

    [Fact]
    public async Task AWarehousesLocalReserve_SendsTheConsumerToAWarehouseThatCanCoverIt()
    {
        await _l.SetUpAsync();
        await T.Put(T.NorthStorage, 100, Flour);
        await T.Put(T.SouthStorage, 50, Flour);
        await _l.TownPolicy(Reserve(Flour, units: 90, warehouse: T.North));
        await _l.MinMax(_l.BakeryInput, Flour, 0, 40);

        var e = Entry(await _l.Run(), _l.BakeryInput, Flour);
        Assert.Equal(("Reserved", 40L, T.South), (e.Outcome, e.Reserved, e.WarehouseId!.Value));
        Assert.Contains("local reserve 90", e.Explanation);
        Assert.Equal(T.SouthStorage, Assert.Single(_l.Distribution()).SourceStorageId);
    }

    // ================= Priority under scarcity =================

    [Fact]
    public async Task UnderScarcity_TheHigherClassIsServedFirst()
    {
        await _l.SetUpAsync();
        await T.Put(T.NorthStorage, 50, Flour);
        await _l.TownPolicy(Priority(_l.Garrison, "Critical"));
        await _l.MinMax(_l.BakeryInput, Flour, 20, 60);
        await _l.MinMax(_l.GarrisonInput, Flour, 20, 60);

        var plan = await _l.Run();
        var garrison = Entry(plan, _l.GarrisonInput, Flour);
        var bakery = Entry(plan, _l.BakeryInput, Flour);
        Assert.Equal(("Critical", "PartlyReserved", 50L), (garrison.PriorityClass, garrison.Outcome, garrison.Reserved));
        Assert.Equal(("Normal", "HeldByReserve", 0L), (bakery.PriorityClass, bakery.Outcome, bakery.Reserved));
        Assert.Contains("50 already reserved", bakery.Explanation);
        Assert.Equal(plan.Entries[0].ConsumerStorageId, _l.GarrisonInput); // the explanation lists Critical first

        // A setting's own class wins over the rule.
        var low = await _l.Settings.UpdateAsync(_l.E.Db.Set<ConsumerReplenishment>().AsNoTracking().Single(s => s.StorageId == _l.GarrisonInput).Id,
            new ConsumerReplenishmentDto { PriorityClass = "Low" });
        Assert.Equal("Low", low.PriorityClass);
    }

    // ================= Expiry (F11) =================

    [Fact]
    public async Task AReservationNotCollectedBeforeItsExpiry_IsReleasedAtTheNextRun_AndItsOrderCancelled()
    {
        await _l.SetUpAsync();
        await T.Put(T.NorthStorage, 100, Flour);
        await _l.MinMax(_l.BakeryInput, Flour, 20, 60);
        E().Update(db => db.ProductionStructures.Single(s => s.Id == _l.Bakery), s => s.WorkersPresent = 0);
        var plan = await _l.Run();
        Assert.Contains(plan.Orders.Single().Warnings, w => w.Contains("No worker of Bakery is free now"));
        await T.Sweep();
        var first = Assert.Single(_l.Distribution());
        Assert.Equal(TransportStatus.Planned, first.Status); // waits for a worker

        T.Advance(TimeSpan.FromMinutes(20));
        Assert.Empty((await _l.Run()).Released); // not expired yet (30 minutes)
        T.Advance(TimeSpan.FromMinutes(11));
        plan = await _l.Run();
        var released = Assert.Single(plan.Released);
        Assert.Contains("Not collected within 30 minutes", released.Reason);
        Assert.Equal(TransportStatus.Cancelled, T.Order(first.Id).Status);
        var rows = _l.Reservations();
        Assert.Equal(LogisticsReservationStatus.Released, rows[0].Status);
        Assert.Null(rows[0].OpenKey);
        Assert.Equal(LogisticsReservationStatus.Open, rows[1].Status); // the same run reserved again
        Assert.Equal(100, T.Amount(T.NorthStorage, Flour));
        Assert.True(T.LedgerClean());
    }

    [Fact]
    public async Task StaffRelease_CancelsTheReservationAndItsOrder_OnlyBeforeLoading()
    {
        await _l.SetUpAsync();
        await T.Put(T.NorthStorage, 100, Flour);
        await _l.MinMax(_l.BakeryInput, Flour, 20, 60);
        await _l.MinMax(_l.GarrisonInput, Flour, 20, 30);
        await _l.Run();
        var rows = _l.Reservations();
        var r = await _l.Records.ReleaseAsync(rows[0].Id, 7, "test");
        Assert.Equal("Cancelled", r.Status);
        Assert.Equal("Cancelled", r.TransportOrderStatus);
        Assert.Contains("user 7: test", r.StatusReason);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _l.Records.ReleaseAsync(rows[0].Id, 7, null));
        await T.Sweep(); // the other one loads
        await Assert.ThrowsAsync<InvalidOperationException>(() => _l.Records.ReleaseAsync(rows[1].Id, 7, null));
    }

    // ================= Capacity, room and who can collect =================

    [Fact]
    public async Task TheWorkersCapacity_BoundsTheCollection_OneOrderCarriesSeveralLines()
    {
        await _l.SetUpAsync();
        await T.Put(T.NorthStorage, 100, Flour);
        await T.Put(T.NorthStorage, 100, Bread);
        await _l.MinMax(_l.BakeryInput, Flour, 0, 50);
        await _l.MinMax(_l.BakeryInput, Bread, 0, 50);

        var plan = await _l.Run(); // default capacity 64
        Assert.Equal(("Reserved", 50L), (Entry(plan, _l.BakeryInput, Flour).Outcome, Entry(plan, _l.BakeryInput, Flour).Reserved));
        var bread = Entry(plan, _l.BakeryInput, Bread);
        Assert.Equal(("PartlyReserved", 14L), (bread.Outcome, bread.Reserved));
        Assert.Contains("carries at most 64", bread.Explanation);
        var order = Assert.Single(_l.Distribution());
        Assert.Equal(new[] { (Flour, 50L), (Bread, 14L) }, order.Lines.OrderBy(x => x.ItemBlueprintId).Select(x => (x.ItemBlueprintId, x.Requested)).ToArray());
    }

    [Fact]
    public async Task WithoutSettings_MinAndMaxComeFromTheLinesInputs()
    {
        await _l.SetUpAsync();
        await E().Lines.CreateAsync(new ProductionLineDto
        {
            ProductionStructureId = _l.Bakery, ItemBlueprintId = Cobblestone, ConfiguredRatePerHour = 10m,
            Inputs = new() { new ProductionLineInputDto { ItemBlueprintId = Flour, AmountPerHour = 2.5m } },
        });
        await T.Put(T.NorthStorage, 100, Flour);
        var e = Entry(await _l.DryRun(), _l.BakeryInput, Flour);
        Assert.Equal(("Inputs", 10L, 30L, 30L), (e.SettingSource, e.ReorderPoint, e.TargetLevel, e.Reserved)); // 4 h and 12 h × 2.5
        var view = (await _l.Planner.ConsumerAsync(_l.Bakery))!;
        Assert.True(view.IsConsumer && view.HasWorkers);
        Assert.Equal("Inputs", view.Storages.Single().Lines.Single().SettingSource);
    }

    [Fact]
    public async Task ARuralConsumerWithoutARoad_IsHeld_AndNoStockIsSaid()
    {
        await _l.SetUpAsync();
        int farm = await _l.ConsumerAsync("Farmstead", 0, districtId: null);
        await _l.MinMax(_l.InputOf(farm), Flour, 0, 10);
        await _l.MinMax(_l.BakeryInput, Flour, 0, 10);
        var plan = await _l.DryRun();
        Assert.Equal("NotConnected", Entry(plan, _l.InputOf(farm), Flour).Outcome);
        Assert.Equal("NoStock", Entry(plan, _l.BakeryInput, Flour).Outcome);
    }

    // ================= Service areas (TOWN_LOGISTICS § 2) =================

    [Fact]
    public async Task AServiceArea_ChoosesTheWarehouse_AndTheNearestStepsInWhenItHasNone()
    {
        await _l.SetUpAsync();
        await T.Put(T.NorthStorage, 100, Flour);
        await T.Put(T.SouthStorage, 100, Flour);
        var policy = await _l.TownPolicy(new LogisticsRuleDto
        {
            Type = "ServiceArea", AreaKind = "District", DistrictId = ProductionWorldTestDb.Market, WarehouseId = T.South,
        });
        await _l.MinMax(_l.BakeryInput, Flour, 0, 40);
        var e = Entry(await _l.DryRun(), _l.BakeryInput, Flour);
        Assert.Equal(T.South, e.WarehouseId); // North is nearer, South serves Market
        Assert.Contains($"its service area (rule #{policy.Rules!.Single().Id})", e.Explanation);

        await T.Put(T.SouthStorage, -100, Flour);
        e = Entry(await _l.DryRun(), _l.BakeryInput, Flour);
        Assert.Equal((T.North, "Reserved"), (e.WarehouseId!.Value, e.Outcome));

        var areas = await _l.Planner.ServiceAreasAsync(ProductionWorldTestDb.Phoenix);
        Assert.Equal(("Rule", T.South), (areas.Entries.Single(x => x.AreaKind == "District").Source, areas.Entries.Single(x => x.AreaKind == "District").WarehouseId!.Value));
        Assert.Equal("Nearest", areas.Entries.Single(x => x.AreaKind == "Rural").Source);
        Assert.Equal(T.North, areas.Entries.Single(x => x.StructureId == T.Quarry).WarehouseId); // nearest by road
    }

    [Fact]
    public async Task AProducersServiceArea_IsItsInboundPreference()
    {
        await _l.SetUpAsync();
        await _l.TownPolicy(new LogisticsRuleDto { Type = "ServiceArea", AreaKind = "Rural", WarehouseId = T.South });
        await T.Put(T.QuarryOutput, 80, Cobblestone);
        await T.Sweep();
        var order = E().Db.TransportOrders.AsNoTracking().Include(o => o.Stops).Single(o => o.Origin == TransportOrigin.Automatic);
        Assert.Equal(T.SouthStorage, order.Stops.Single().StorageId); // North is nearer
    }

    // ================= Schedule, dry run and the flag (D-L2, A21) =================

    [Fact]
    public async Task TheSchedule_PlansEachTownWithAWarehouseEvery10Minutes_AndPrunesOldRuns()
    {
        await _l.SetUpAsync();
        Assert.Equal(1, (await _l.Planner.SweepAsync()).TownsPlanned); // Phoenix; Rivertown has no warehouse
        Assert.Equal(0, (await _l.Planner.SweepAsync()).TownsPlanned);
        T.Advance(TimeSpan.FromMinutes(9));
        Assert.Equal(0, (await _l.Planner.SweepAsync()).TownsPlanned);
        T.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(1, (await _l.Planner.SweepAsync()).TownsPlanned);
        var supply = await _l.Planner.SupplyAsync(ProductionWorldTestDb.Phoenix);
        Assert.Equal(supply.LastRun!.StartedAt.AddMinutes(10), supply.NextRunAt);

        E().SetSettings(s => s.Logistics = new ProductionLogisticsSettingsDto { RunRetentionDays = 1 });
        T.Advance(TimeSpan.FromDays(2));
        var result = await _l.Planner.SweepAsync();
        Assert.Equal((1, 2), (result.TownsPlanned, result.RunsPruned));
        Assert.Single(E().Db.Set<LogisticsPlannerRun>().AsNoTracking());
    }

    [Fact]
    public async Task ARunStoresItsExplanation_ADryRunWritesNothing()
    {
        await _l.SetUpAsync();
        await T.Put(T.NorthStorage, 100, Flour);
        await _l.MinMax(_l.BakeryInput, Flour, 20, 60);
        await _l.MinMax(_l.GarrisonInput, Flour, 0, 10);
        await T.Put(_l.GarrisonInput, 50, Flour);
        var dry = await _l.DryRun();
        Assert.True(dry.DryRun);
        Assert.Empty(_l.Reservations());
        Assert.Empty(E().Db.Set<LogisticsPlannerRun>().AsNoTracking());

        var plan = await _l.Run();
        var run = (await ((knkwebapi_v2.Services.Logistics.ILogisticsPlannerRunService)_l.Records).GetByIdAsync(plan.RunId!.Value))!;
        Assert.Equal(("Manual", 1, 1, 1, 0), (run.Trigger, run.DemandLines, run.ReservationsCreated, run.OrdersCreated, run.Shortfalls));
        var entry = Assert.Single(run.Entries); // NotNeeded lines are not stored
        Assert.Equal(("Reserved", "Bakery", "Coal", "North"), (entry.Outcome, entry.ConsumerStructureName, entry.ItemBlueprintName, entry.WarehouseName));
        Assert.Contains("min 20, max 60", entry.Explanation);
        Assert.Contains("short 60; Normal priority (the default)", entry.Explanation);
        Assert.Contains("Reserved 60 of 60 at North", entry.Explanation);
        Assert.Contains("collected by its own worker", entry.Explanation);
        Assert.Contains("1 reserved (60 units) in 1 collection order(s)", run.Summary);
    }

    [Fact]
    public async Task WithTheFlagOff_TheDryRunWorks_ButNothingRuns()
    {
        using var off = new LogisticsTestDb(productionEnabled: false);
        await off.SetUpAsync();
        await off.T.Put(off.T.NorthStorage, 100, Flour);
        await off.MinMax(off.BakeryInput, Flour, 20, 60);
        var dry = await off.DryRun();
        Assert.False(dry.FeatureEnabled);
        Assert.Equal("Reserved", Entry(dry, off.BakeryInput, Flour).Outcome);
        await Assert.ThrowsAsync<ProductionOffException>(() => off.Run());
        var sweep = await off.Planner.SweepAsync();
        Assert.Equal((false, 0), (sweep.FeatureEnabled, sweep.TownsPlanned));
        Assert.Empty(off.Reservations());
        Assert.Null((await off.Planner.SupplyAsync(ProductionWorldTestDb.Phoenix)).NextRunAt);
    }

    private ProductionEngineTestDb E() => _l.E;
}
