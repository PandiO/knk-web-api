using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Stock;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static knkwebapi_v2.Tests.Services.Production.ProductionEngineTestDb;

namespace knkwebapi_v2.Tests.Services.Production;

/// <summary>
/// Transport core P5 (PandiO/knk-workspace#79, production DESIGN § 7): the lifecycle with the ledger balanced at every
/// step, the simulated trip time (route length ÷ speed, G14), workers away (§ 7.7), the automatic trigger, sizing and
/// destination (V2 defects 7-9), overflow (AwaitingSpace → re-route → Returning → ReturnBlocked, V2 defect 2),
/// per-stop amounts (V2 defect 3), cancel in each state, restarts mid-transit, unconnected producers (D5) and the flag
/// (A12, A13, A21; smoke § 6.1).
/// </summary>
public class TransportTests : IDisposable
{
    private readonly TransportTestDb _t = new();

    public void Dispose() => _t.Dispose();

    // ================= Lifecycle and ETA (A12) =================

    [Fact]
    public async Task AManualOrder_MovesSourceToTransitToStop_WithTheLedgerBalancedAtEveryStep_AndTakesRoadLengthOverSpeed()
    {
        await _t.SetUpAsync();
        await _t.Put(_t.QuarryOutput, 50);
        var order = await _t.Transport.CreateAsync(_t.Manual(_t.NorthStorage, 40), actorUserId: 7);
        Assert.Equal("Planned", order.Status);
        Assert.Equal(_t.Quarry, order.CarrierStructureId);
        Assert.True(_t.LedgerClean());

        await _t.Sweep(); // assigned, loaded, departed
        var o = _t.Order(order.Id);
        Assert.Equal(TransportStatus.InTransit, o.Status);
        Assert.Equal(10, _t.Amount(_t.QuarryOutput, Cobblestone));
        Assert.Equal(40, _t.Amount(o.CargoStorageId, Cobblestone)); // cargo exists only in the transit storage
        Assert.Equal(0, _t.Amount(_t.NorthStorage, Cobblestone));
        Assert.Equal(TimeSpan.FromSeconds(100), o.EtaAt!.Value - o.DepartedAt!.Value); // 432 blocks ÷ 4.32 b/s
        Assert.Equal(TransportDistanceSource.Lane, o.Stops.Single().DistanceSource);
        Assert.True(_t.LedgerClean());

        _t.Advance(99);
        await _t.Sweep();
        Assert.Equal(TransportStatus.InTransit, _t.Order(order.Id).Status); // not there yet
        _t.Advance(1);
        await _t.Sweep();
        o = _t.Order(order.Id);
        Assert.Equal(TransportStatus.Delivered, o.Status);
        Assert.Equal(40, _t.Amount(_t.NorthStorage, Cobblestone));
        Assert.Equal(0, _t.Amount(o.CargoStorageId, Cobblestone));
        Assert.Equal((40L, 40L, 40L, 0L), (o.Lines.Single().Requested, o.Lines.Single().Loaded, o.Lines.Single().Delivered, o.Lines.Single().Returned));
        Assert.Null(o.NextActionAt);
        Assert.True(_t.LedgerClean());

        var kinds = _t.Events(order.Id).Select(e => e.Kind).ToList();
        Assert.Equal(new[]
        {
            TransportEventKind.Created, TransportEventKind.Assigned, TransportEventKind.Loaded, TransportEventKind.Departed,
            TransportEventKind.ArrivedAtStop, TransportEventKind.Delivered, TransportEventKind.WorkerReturned, TransportEventKind.Completed,
        }, kinds);
        var reasons = _t.E.Db.StockTransactions.AsNoTracking().Where(x => x.SourceType == nameof(TransportOrder) && x.SourceRef == order.Id.ToString())
            .Select(x => x.Reason).ToList();
        Assert.Equal(new[] { StockReason.TransportLoad, StockReason.TransportDeliver }, reasons);
    }

    [Fact]
    public async Task WithoutALane_TheRouteIsTheStraightDistanceTimesTheFactor_AndWithoutPositionsTheFallback()
    {
        await _t.SetUpAsync(lanes: false);
        var plan = await _t.Transport.DryRunAsync(_t.Manual(_t.NorthStorage, 1));
        Assert.Equal(("Fallback", 200m, 47L), (plan.Legs.Single().DistanceSource, plan.Legs.Single().DistanceBlocks, plan.Legs.Single().Seconds));

        var a = _t.E.Db.Locations.Add(new Location { X = 0, Y = 64, Z = 0, World = "world" }).Entity;
        var b = _t.E.Db.Locations.Add(new Location { X = 300, Y = 64, Z = 400, World = "world" }).Entity;
        _t.E.Db.SaveChanges();
        _t.E.Db.Domains.Single(d => d.Id == _t.Quarry).LocationId = a.Id;
        _t.E.Db.Domains.Single(d => d.Id == _t.North).LocationId = b.Id;
        _t.E.Db.SaveChanges();
        _t.E.Db.ChangeTracker.Clear();
        plan = await _t.Transport.DryRunAsync(_t.Manual(_t.NorthStorage, 1));
        Assert.Equal(("Estimated", 700m), (plan.Legs.Single().DistanceSource, plan.Legs.Single().DistanceBlocks)); // 500 × 1.4
        Assert.Contains(plan.Warnings, w => w.Contains("no measured lane"));
    }

    [Fact]
    public async Task ALaneIsReadInBothDirections_AndAnUnreachableLaneIsNotARoute()
    {
        await _t.SetUpAsync(lanes: false);
        await _t.Lane(_t.North, _t.Quarry, 86.4m);
        var plan = await _t.Transport.DryRunAsync(_t.Manual(_t.NorthStorage, 1));
        Assert.Equal(("Lane", 20L), (plan.Legs.Single().DistanceSource, plan.Legs.Single().Seconds));
        var lane = _t.E.Db.TransportLanes.AsNoTracking().Single();
        await _t.Lanes.UpdateAsync(lane.Id, new TransportLaneDto { Reachable = false });
        plan = await _t.Transport.DryRunAsync(_t.Manual(_t.NorthStorage, 1));
        Assert.Equal("Fallback", plan.Legs.Single().DistanceSource);
    }

    // ================= Workers (§ 7.7, A12) =================

    [Fact]
    public async Task AWorkerCarryingAnOrder_LowersOutputTo50Percent_BothAway_StopIt_AndItRecoversOnReturn()
    {
        await _t.SetUpAsync(producing: true, northCap: null);
        _t.E.Update(db => db.Storages.Single(s => s.Id == _t.QuarryOutput), s => s.MaxUnits = null);
        await _t.Put(_t.QuarryOutput, 20);
        // A long lane: 4320 blocks = 1000 s ≈ 16.7 minutes each way.
        _t.E.Update(db => db.TransportLanes.Single(l => l.FromStructureId == _t.Quarry && l.ToStructureId == _t.North), l => l.RoadDistance = 25920m);
        var first = await _t.Transport.CreateAsync(_t.Manual(_t.NorthStorage, 10), null);
        await _t.Sweep();
        Assert.Equal(1, _t.Structure(_t.Quarry).WorkersPresent);

        _t.Advance(TimeSpan.FromMinutes(60));
        await _t.E.Engine.SweepAsync();
        var record = _t.E.Records(_t.QuarryLine).Last();
        Assert.Equal(0.5m, record.WorkerFactor);
        Assert.Equal(30, record.Emitted); // 60/h at 50 %

        var second = await _t.Transport.CreateAsync(_t.Manual(_t.SouthStorage, 10), null);
        await _t.Sweep();
        Assert.Equal(0, _t.Structure(_t.Quarry).WorkersPresent);
        _t.Advance(TimeSpan.FromMinutes(10));
        await _t.E.Engine.SweepAsync();
        Assert.Equal(0m, _t.E.Records(_t.QuarryLine).Last().WorkerFactor);

        _t.Advance(TimeSpan.FromHours(2)); // both delivered
        await _t.Sweep();
        Assert.Equal(TransportStatus.Delivered, _t.Order(first.Id).Status);
        Assert.Equal(TransportStatus.Delivered, _t.Order(second.Id).Status);
        Assert.Equal(2, _t.Structure(_t.Quarry).WorkersPresent);
        _t.Advance(TimeSpan.FromMinutes(10));
        await _t.E.Engine.SweepAsync();
        Assert.Equal(1m, _t.E.Records(_t.QuarryLine).Last().WorkerFactor);
        Assert.True(_t.LedgerClean());
    }

    [Fact]
    public async Task AnOrderWaitsWithoutAFreeWorker_AndLeavesWhenOneIsBack()
    {
        await _t.SetUpAsync();
        await _t.Put(_t.QuarryOutput, 20);
        _t.E.Update(db => db.ProductionStructures.Single(s => s.Id == _t.Quarry), s => s.WorkersPresent = 0);
        var order = await _t.Transport.CreateAsync(_t.Manual(_t.NorthStorage, 10), null);
        await _t.Sweep();
        await _t.Sweep();
        var o = _t.Order(order.Id);
        Assert.Equal(TransportStatus.Planned, o.Status);
        Assert.Contains("Waiting for a free worker", o.StatusReason);
        Assert.Single(_t.Events(order.Id), e => e.Kind == TransportEventKind.WaitingForWorker);
        Assert.Equal(20, _t.Amount(_t.QuarryOutput, Cobblestone));

        _t.E.Update(db => db.ProductionStructures.Single(s => s.Id == _t.Quarry), s => s.WorkersPresent = 1);
        _t.Advance(60);
        await _t.Sweep();
        Assert.Equal(TransportStatus.InTransit, _t.Order(order.Id).Status);
        Assert.Equal(0, _t.Structure(_t.Quarry).WorkersPresent);
    }

    [Fact]
    public async Task AWorkerCarriesAtMostItsCapacity()
    {
        await _t.SetUpAsync();
        _t.E.Update(db => db.ProductionStructures.Single(s => s.Id == _t.Quarry), s => s.TransportCapacity = 30);
        var plan = await _t.Transport.DryRunAsync(_t.Manual(_t.NorthStorage, 31));
        Assert.False(plan.Valid);
        Assert.Contains(plan.Errors, e => e.Contains("carries at most 30"));
        await Assert.ThrowsAsync<ArgumentException>(() => _t.Transport.CreateAsync(_t.Manual(_t.NorthStorage, 31), null));
    }

    // ================= Automatic trigger (§ 7.4, V2 defects 7-9) =================

    [Fact]
    public async Task TheThreshold_FiresOneAutomaticOrder_SizedByTheWorkersCapacity_ToTheNearestWarehouseByRoad()
    {
        await _t.SetUpAsync();
        await _t.Put(_t.QuarryOutput, 74, Cobblestone);
        await _t.Sweep();
        Assert.Empty(_t.E.Db.TransportOrders.AsNoTracking()); // 74 < 75 % of 100
        await _t.Put(_t.QuarryOutput, 6, Coal); // 80 units of two blueprints
        var view = (await _t.Transport.StructureViewAsync(_t.Quarry))!;
        Assert.Contains(view.TriggerReasons, r => r.Contains("threshold of 75"));
        Assert.Equal(_t.North, view.DestinationWarehouseId);
        Assert.Empty(_t.E.Db.TransportOrders.AsNoTracking()); // the view is a dry run

        var result = await _t.Sweep();
        Assert.Equal(1, result.OrdersCreated);
        var o = _t.E.Db.TransportOrders.AsNoTracking().Include(x => x.Lines).Include(x => x.Stops).Single();
        Assert.Equal(TransportOrigin.Automatic, o.Origin);
        Assert.Equal(_t.NorthStorage, o.Stops.Single().StorageId);
        // Capacity 64 (default): the largest line first.
        Assert.Equal(new[] { (Cobblestone, 64L) }, o.Lines.Select(l => (l.ItemBlueprintId, l.Requested)).ToArray());

        // One open automatic order per source.
        await _t.Put(_t.QuarryOutput, 60);
        Assert.Equal(0, (await _t.Sweep()).OrdersCreated);
        Assert.Contains((await _t.Transport.StructureViewAsync(_t.Quarry))!.HeldReasons, r => r.Contains("still under way"));
        Assert.True(_t.LedgerClean());
    }

    [Fact]
    public async Task ABlockedFullLine_AndTheDistinctCap_AreTriggersToo()
    {
        await _t.SetUpAsync();
        await _t.Put(_t.QuarryOutput, 5);
        _t.E.Update(db => db.ProductionLines.Single(l => l.Id == _t.QuarryLine), l => l.Status = ProductionLineStatus.BlockedFull);
        Assert.Contains((await _t.Transport.StructureViewAsync(_t.Quarry))!.TriggerReasons, r => r.Contains("blocked full"));
        _t.E.Update(db => db.ProductionLines.Single(l => l.Id == _t.QuarryLine), l => l.Status = ProductionLineStatus.Running);
        _t.E.Update(db => db.Storages.Single(s => s.Id == _t.QuarryOutput), s => s.MaxDistinct = 1);
        Assert.Contains((await _t.Transport.StructureViewAsync(_t.Quarry))!.TriggerReasons, r => r.Contains("different items"));
        Assert.Equal(1, (await _t.Sweep()).OrdersCreated);
    }

    [Fact]
    public async Task ThePreferredWarehouse_WinsOverTheNearest_WhenItHasRoom()
    {
        await _t.SetUpAsync();
        _t.E.Update(db => db.ProductionStructures.Single(s => s.Id == _t.Quarry), s =>
        {
            s.WarehousePreference = WarehousePreference.Preferred;
            s.PreferredWarehouseId = _t.South;
        });
        await _t.Put(_t.QuarryOutput, 80);
        Assert.Equal(_t.South, (await _t.Transport.StructureViewAsync(_t.Quarry))!.DestinationWarehouseId);
        // A full nearest warehouse is passed over for one with room.
        _t.E.Update(db => db.ProductionStructures.Single(s => s.Id == _t.Quarry), s => s.WarehousePreference = WarehousePreference.Nearest);
        _t.E.Update(db => db.Storages.Single(s => s.Id == _t.NorthStorage), s => s.MaxUnits = 10);
        Assert.Equal(_t.South, (await _t.Transport.StructureViewAsync(_t.Quarry))!.DestinationWarehouseId);
    }

    [Theory]
    [InlineData(RoadConnectionState.Unconnected, "Not connected")]
    [InlineData(RoadConnectionState.Unmeasured, "not been measured")]
    public async Task AnUnconnectedRuralProducer_HoldsTransport_FillsUp_AndStops_WithBothWarnings(RoadConnectionState state, string warning)
    {
        await _t.SetUpAsync(connected: false, producing: true);
        _t.E.Update(db => db.ProductionStructures.Single(s => s.Id == _t.Quarry), s => s.RoadConnection = state);
        _t.Advance(TimeSpan.FromHours(3));
        await _t.E.Engine.SweepAsync();
        Assert.Equal(0, (await _t.Sweep()).OrdersCreated);
        Assert.Equal(100, _t.Amount(_t.QuarryOutput, Cobblestone));
        Assert.Equal(ProductionLineStatus.BlockedFull, _t.E.Line(_t.QuarryLine).Status); // A13: fills and stops
        var view = (await _t.Transport.StructureViewAsync(_t.Quarry))!;
        Assert.Contains(view.Warnings, w => w.Contains(warning));
        Assert.Contains(view.Warnings, w => w.Contains("Output storage is full"));
        Assert.Null(view.WouldCreate);
        var dto = (await _t.E.Structures.GetByIdAsync(_t.Quarry))!;
        Assert.Contains(dto.Warnings!, w => w.Contains("Output storage is full"));
        if (state == RoadConnectionState.Unconnected) Assert.Contains(dto.Warnings!, w => w.Contains("road network"));

        // Connected: the next sweep plans the order.
        _t.E.Update(db => db.ProductionStructures.Single(s => s.Id == _t.Quarry), s => s.RoadConnection = RoadConnectionState.Connected);
        Assert.Equal(1, (await _t.Sweep()).OrdersCreated);
    }

    [Fact]
    public async Task ADistrictProducer_NeedsNoRoadConnection()
    {
        await _t.SetUpAsync(connected: false);
        _t.E.Update(db => db.ProductionStructures.Single(s => s.Id == _t.Quarry), s =>
        {
            s.TownId = null;
            s.DistrictId = ProductionWorldTestDb.Market;
        });
        await _t.Put(_t.QuarryOutput, 80);
        Assert.Equal(1, (await _t.Sweep()).OrdersCreated);
    }

    // ================= Overflow (V2 defect 2) =================

    [Fact]
    public async Task AFullStop_WaitsThenReroutesToAnotherWarehouseOfTheTown()
    {
        await _t.SetUpAsync(northCap: 30);
        await _t.Put(_t.QuarryOutput, 50);
        var order = await _t.Transport.CreateAsync(_t.Manual(_t.NorthStorage, 50), null);
        await _t.Sweep();
        _t.Advance(100);
        await _t.Sweep();
        var o = _t.Order(order.Id);
        Assert.Equal(TransportStatus.AwaitingSpace, o.Status);
        Assert.Equal(30, _t.Amount(_t.NorthStorage, Cobblestone));
        Assert.Equal(20, _t.Amount(o.CargoStorageId, Cobblestone));

        _t.Advance(TimeSpan.FromMinutes(29));
        await _t.Sweep();
        Assert.Equal(TransportStatus.AwaitingSpace, _t.Order(order.Id).Status);
        _t.Advance(TimeSpan.FromMinutes(1));
        await _t.Sweep();
        o = _t.Order(order.Id);
        Assert.Equal(TransportStatus.InTransit, o.Status);
        Assert.Equal(1, o.Reroutes);
        var added = o.Stops.Single(s => s.ReroutedFromStopId != null);
        Assert.Equal((_t.SouthStorage, 20L), (added.StorageId, added.Lines.Single().Planned));
        Assert.Equal(TransportStopStatus.Partial, o.Stops.Single(s => s.Id == added.ReroutedFromStopId).Status);
        Assert.Equal(TimeSpan.FromSeconds(50), o.EtaAt!.Value - o.LegStartedAt!.Value); // North → South 216 blocks

        _t.Advance(50);
        await _t.Sweep();
        o = _t.Order(order.Id);
        Assert.Equal(TransportStatus.Delivered, o.Status);
        Assert.Equal(20, _t.Amount(_t.SouthStorage, Cobblestone));
        Assert.True(_t.LedgerClean());
    }

    [Fact]
    public async Task WithNowhereToGo_TheCargoComesBack_AndAFullSourceBlocksTheReturnUntilItHasRoom()
    {
        await _t.SetUpAsync(northCap: 30);
        _t.E.Update(db => db.Warehouses.Single(w => w.Id == _t.South), w => w.AcceptsDeliveries = false);
        // Only the manual order here (a refilled source would otherwise also trigger an automatic one).
        _t.E.Update(db => db.ProductionStructures.Single(s => s.Id == _t.Quarry), s => s.AutomaticTransport = false);
        await _t.Put(_t.QuarryOutput, 50);
        var order = await _t.Transport.CreateAsync(_t.Manual(_t.NorthStorage, 50), null);
        await _t.Sweep();
        _t.Advance(100);
        await _t.Sweep();
        _t.Advance(TimeSpan.FromMinutes(30));
        await _t.Sweep();
        var o = _t.Order(order.Id);
        Assert.Equal(TransportStatus.Returning, o.Status);
        Assert.Equal(TimeSpan.FromSeconds(100), o.EtaAt!.Value - o.LegStartedAt!.Value);

        // Meanwhile the source filled up.
        await _t.Put(_t.QuarryOutput, 95);
        _t.Advance(100);
        await _t.Sweep();
        o = _t.Order(order.Id);
        Assert.Equal(TransportStatus.ReturnBlocked, o.Status);
        Assert.Equal(100, _t.Amount(_t.QuarryOutput, Cobblestone));
        Assert.Equal(15, _t.Amount(o.CargoStorageId, Cobblestone)); // never dropped
        Assert.True(_t.LedgerClean());

        await _t.Put(_t.QuarryOutput, -50);
        _t.Advance(60);
        await _t.Sweep();
        o = _t.Order(order.Id);
        Assert.Equal(TransportStatus.Returned, o.Status);
        Assert.Equal((50L, 30L, 20L), (o.Lines.Single().Loaded, o.Lines.Single().Delivered, o.Lines.Single().Returned));
        Assert.Equal(2, _t.Structure(_t.Quarry).WorkersPresent);
        Assert.True(_t.LedgerClean());
    }

    // ================= Per-stop amounts (V2 defect 3) =================

    [Fact]
    public async Task EachStopGetsItsOwnPlannedAmounts_AndAShortLoadServesTheFirstStopsFirst()
    {
        await _t.SetUpAsync();
        await _t.Put(_t.QuarryOutput, 25);
        var order = await _t.Transport.CreateAsync(new TransportOrderCreateDto
        {
            SourceStorageId = _t.QuarryOutput,
            Stops = new()
            {
                new() { StorageId = _t.NorthStorage, Lines = new() { new() { ItemBlueprintId = Cobblestone, Amount = 20 } } },
                new() { StorageId = _t.SouthStorage, Lines = new() { new() { ItemBlueprintId = Cobblestone, Amount = 10 } } },
            },
        }, null);
        await _t.Sweep();
        var o = _t.Order(order.Id);
        Assert.Equal(25, o.Lines.Single().Loaded);
        Assert.Equal(new[] { 20L, 5L }, o.Stops.OrderBy(s => s.Sequence).Select(s => s.Lines.Single().Planned).ToArray());
        _t.Advance(100);
        await _t.Sweep();
        Assert.Equal(20, _t.Amount(_t.NorthStorage, Cobblestone));
        Assert.Equal(0, _t.Amount(_t.SouthStorage, Cobblestone));
        _t.Advance(50);
        await _t.Sweep();
        Assert.Equal(5, _t.Amount(_t.SouthStorage, Cobblestone)); // not 20 again
        Assert.Equal(TransportStatus.Delivered, _t.Order(order.Id).Status);
        Assert.True(_t.LedgerClean());
    }

    [Fact]
    public async Task NothingToLoad_CancelsTheOrder_AndTheWorkerStays()
    {
        await _t.SetUpAsync();
        var order = await _t.Transport.CreateAsync(_t.Manual(_t.NorthStorage, 10), null);
        await _t.Sweep();
        var o = _t.Order(order.Id);
        Assert.Equal(TransportStatus.Cancelled, o.Status);
        Assert.Contains("Nothing to load", o.StatusReason);
        Assert.Equal(2, _t.Structure(_t.Quarry).WorkersPresent);
    }

    // ================= Cancel in each state =================

    [Fact]
    public async Task CancelWhilePlanned_EndsCancelled_WithoutStockMoves()
    {
        await _t.SetUpAsync();
        await _t.Put(_t.QuarryOutput, 20);
        var order = await _t.Transport.CreateAsync(_t.Manual(_t.NorthStorage, 10), null);
        var c = await _t.Transport.CancelAsync(order.Id, 7, "test");
        Assert.Equal(("Cancelled", true), (c.Status, c.IsFinal));
        Assert.Contains("test", c.StatusReason);
        await _t.Sweep();
        Assert.Equal(20, _t.Amount(_t.QuarryOutput, Cobblestone));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _t.Transport.CancelAsync(order.Id, 7, null));
    }

    [Fact]
    public async Task CancelInTransit_TurnsBack_TakingAsLongAsItWalked_AndReturnsEverything()
    {
        await _t.SetUpAsync();
        await _t.Put(_t.QuarryOutput, 20);
        var order = await _t.Transport.CreateAsync(_t.Manual(_t.NorthStorage, 10), null);
        await _t.Sweep();
        _t.Advance(30);
        var c = await _t.Transport.CancelAsync(order.Id, null, null);
        Assert.Equal("Returning", c.Status);
        Assert.Equal(TimeSpan.FromSeconds(30), c.EtaAt!.Value - c.LegStartedAt!.Value);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _t.Transport.CancelAsync(order.Id, null, null)); // already returning
        _t.Advance(30);
        await _t.Sweep();
        var o = _t.Order(order.Id);
        Assert.Equal(TransportStatus.Returned, o.Status);
        Assert.Contains("Cancelled", o.StatusReason);
        Assert.Equal(20, _t.Amount(_t.QuarryOutput, Cobblestone));
        Assert.Equal(2, _t.Structure(_t.Quarry).WorkersPresent);
        Assert.True(_t.LedgerClean());
    }

    [Fact]
    public async Task CancelWhileAwaitingSpace_ReturnsTheRest_FromTheStop()
    {
        await _t.SetUpAsync(northCap: 30);
        await _t.Put(_t.QuarryOutput, 50);
        var order = await _t.Transport.CreateAsync(_t.Manual(_t.NorthStorage, 50), null);
        await _t.Sweep();
        _t.Advance(100);
        await _t.Sweep();
        var c = await _t.Transport.CancelAsync(order.Id, null, null);
        Assert.Equal("Returning", c.Status);
        Assert.Equal(TimeSpan.FromSeconds(100), c.EtaAt!.Value - c.LegStartedAt!.Value); // the lane back
        _t.Advance(100);
        await _t.Sweep();
        var o = _t.Order(order.Id);
        Assert.Equal((TransportStatus.Returned, 30L, 20L), (o.Status, o.Lines.Single().Delivered, o.Lines.Single().Returned));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _t.Transport.CancelAsync(order.Id, null, null)); // final
        Assert.True(_t.LedgerClean());
    }

    // ================= Restart =================

    [Fact]
    public async Task ARestartMidTransit_ContinuesWhereItWas_WithoutMovingAnythingTwice()
    {
        await _t.SetUpAsync();
        await _t.Put(_t.QuarryOutput, 20);
        var order = await _t.Transport.CreateAsync(_t.Manual(_t.NorthStorage, 10), null);
        await _t.Sweep();
        _t.Advance(50);
        _t.Restart();
        await _t.Sweep();
        Assert.Equal(TransportStatus.InTransit, _t.Order(order.Id).Status);
        _t.Advance(TimeSpan.FromHours(1)); // the server was down for an hour
        _t.Restart();
        await _t.Sweep();
        await _t.Sweep();
        Assert.Equal(TransportStatus.Delivered, _t.Order(order.Id).Status);
        Assert.Equal(10, _t.Amount(_t.NorthStorage, Cobblestone));
        Assert.Equal(10, _t.Amount(_t.QuarryOutput, Cobblestone));
        Assert.Equal(2, _t.E.Db.StockTransactions.Count(x => x.SourceType == nameof(TransportOrder)));
        Assert.True(_t.LedgerClean());
    }

    // ================= Manual orders, dry runs, flag (A21) =================

    [Fact]
    public async Task ADryRun_WritesNothing_AndExplainsThePlan()
    {
        await _t.SetUpAsync(northCap: 5);
        await _t.Put(_t.QuarryOutput, 8);
        var before = (_t.E.Db.TransportOrders.Count(), _t.E.Db.Storages.Count(), _t.E.Db.StockTransactions.Count());
        var plan = await _t.Transport.DryRunAsync(_t.Manual(_t.NorthStorage, 10));
        Assert.True(plan.Valid);
        Assert.Equal((10L, 8L), (plan.Lines.Single().Requested, plan.Lines.Single().Available));
        Assert.Contains(plan.Warnings, w => w.Contains("holds 8 of 10"));
        Assert.Contains(plan.Warnings, w => w.Contains("has room for 5"));
        Assert.Equal((_t.Quarry, 100L), (plan.CarrierStructureId, plan.TotalSeconds));
        Assert.Equal(before, (_t.E.Db.TransportOrders.Count(), _t.E.Db.Storages.Count(), _t.E.Db.StockTransactions.Count()));
    }

    [Fact]
    public async Task InvalidManualOrders_AreRefusedWithTheirReasons()
    {
        await _t.SetUpAsync();
        int sys = await _t.E.Stock.GetSystemStorageIdAsync(Storage.SysProduction);
        Assert.Contains("at least one stop", (await _t.Transport.DryRunAsync(new TransportOrderCreateDto { SourceStorageId = _t.QuarryOutput })).Errors.Single());
        Assert.Contains((await _t.Transport.DryRunAsync(_t.Manual(sys, 1))).Errors, e => e.Contains("real storage"));
        Assert.Contains((await _t.Transport.DryRunAsync(_t.Manual(_t.QuarryOutput, 1))).Errors, e => e.Contains("is the source"));
        Assert.Contains((await _t.Transport.DryRunAsync(_t.Manual(_t.NorthStorage, 0))).Errors, e => e.Contains("positive"));
        Assert.Contains((await _t.Transport.DryRunAsync(_t.Manual(_t.NorthStorage, 1, blueprint: 9999))).Errors, e => e.Contains("9999"));
        var auto = _t.Manual(_t.NorthStorage, 1);
        auto.Origin = "Automatic";
        Assert.Contains((await _t.Transport.DryRunAsync(auto)).Errors, e => e.Contains("Manual or Quest"));
    }

    [Fact]
    public async Task AFromAWarehouse_IsCarriedWithoutAWorker_AndARepeatedClientKeyMakesOneOrder()
    {
        await _t.SetUpAsync();
        await _t.Put(_t.NorthStorage, 10);
        var dto = _t.Manual(_t.SouthStorage, 10, from: _t.NorthStorage);
        dto.ClientKey = "quest-42";
        dto.Origin = "Quest";
        var a = await _t.Transport.CreateAsync(dto, null);
        var b = await _t.Transport.CreateAsync(dto, null);
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(("Quest", (int?)null), (a.Origin, a.CarrierStructureId));
        await _t.Sweep();
        var o = _t.Order(a.Id);
        Assert.Equal(0, o.Assignments.Single().Workers);
        Assert.Equal(2, _t.Structure(_t.Quarry).WorkersPresent);
        _t.Advance(50);
        await _t.Sweep();
        Assert.Equal(TransportStatus.Delivered, _t.Order(a.Id).Status);
    }

    [Fact]
    public async Task WithTheFlagOff_NothingMoves_ButManualOrdersAndCancelWork()
    {
        using var t = new TransportTestDb(productionEnabled: false);
        await t.SetUpAsync();
        await t.Put(t.QuarryOutput, 90);
        var order = await t.Transport.CreateAsync(t.Manual(t.NorthStorage, 10), null);
        var r = await t.Sweep();
        Assert.False(r.FeatureEnabled);
        Assert.Equal(TransportStatus.Planned, t.Order(order.Id).Status);
        Assert.Single(t.E.Db.TransportOrders.AsNoTracking()); // no automatic order
        Assert.Contains((await t.Transport.StructureViewAsync(t.Quarry))!.HeldReasons, h => h.Contains("turned off"));
        Assert.Empty(await t.Lanes.DueAsync(100));
        Assert.Equal("Cancelled", (await t.Transport.CancelAsync(order.Id, null, null)).Status);
    }

    [Fact]
    public async Task SearchAndDetail_ShowTheOrder()
    {
        await _t.SetUpAsync();
        await _t.Put(_t.QuarryOutput, 20);
        var order = await _t.Transport.CreateAsync(_t.Manual(_t.NorthStorage, 10), null);
        await _t.Sweep();
        var page = await _t.Transport.SearchAsync(new PagedQueryDto { PageNumber = 1, PageSize = 10, Filters = new() { ["open"] = "true", ["structureId"] = _t.Quarry.ToString() } });
        var row = Assert.Single(page.Items);
        Assert.Equal(("InTransit", "North - deliveries", 10L), (row.Status, row.Destinations, row.UnitsLoaded));
        var detail = (await _t.Transport.GetByIdAsync(order.Id))!;
        Assert.Equal(10, detail.InTransit.Single().Amount);
        Assert.Equal("Cobblestone", detail.Lines.Single().ItemBlueprintName);
        Assert.Equal(4, detail.Events.Count);
        Assert.Single((await _t.Transport.StructureViewAsync(_t.Quarry))!.OpenOrders);
        Assert.Equal(1, (await _t.Transport.StructureViewAsync(_t.Quarry))!.WorkersAway);
        Assert.Empty((await _t.Transport.SearchAsync(new PagedQueryDto { PageNumber = 1, PageSize = 10, Filters = new() { ["status"] = "Delivered,Returned" } })).Items);
    }
}
