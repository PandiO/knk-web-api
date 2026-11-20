using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;
using static knkwebapi_v2.Tests.Services.Production.ProductionEngineTestDb;

namespace knkwebapi_v2.Tests.Services.Production;

/// <summary>
/// The transport smoke guide's data (§ 6.0) on top of the engine's (§ 4.0): the rural quarry in Phoenix (60 cobblestone
/// per hour, two workers, Output capped at 100, road connected) and two rural warehouses of Phoenix, North (delivery
/// storage capped at 1000) and South (uncapped). Lanes: quarry → North 432 blocks of road (100 s at the default walking
/// speed 4.32), quarry → South 864 blocks, North ↔ South 216 blocks.
/// </summary>
internal sealed class TransportTestDb : IDisposable
{
    public const decimal Speed = 4.32m;

    public ProductionEngineTestDb E { get; }
    public TransportService Transport { get; private set; }
    public TransportLaneService Lanes { get; }

    public int Quarry { get; private set; }
    public int QuarryLine { get; private set; }
    public int QuarryOutput { get; private set; }
    public int North { get; private set; }
    public int NorthStorage { get; private set; }
    public int South { get; private set; }
    public int SouthStorage { get; private set; }

    public TransportTestDb(bool productionEnabled = true)
    {
        E = new ProductionEngineTestDb(productionEnabled);
        E.SetSettings(s => s.Transport = new ProductionTransportSettingsDto { WalkingSpeedBlocksPerSecond = Speed });
        Transport = NewService();
        Lanes = new TransportLaneService(E.Db, new ProductionSettingsReader(E.Db), E.Clock);
    }

    /// <summary>A fresh service on the same database: what an API restart looks like.</summary>
    public TransportService NewService() =>
        new(E.Db, E.Stock, new ProductionSettingsReader(E.Db), E.Engine, E.Clock);

    public void Restart() => Transport = NewService();

    public void Dispose() => E.Dispose();

    public async Task<TransportTestDb> SetUpAsync(bool connected = true, bool lanes = true, long? northCap = 1000, bool producing = false)
    {
        var (sid, line, output) = await E.QuarryAsync(cap: 100);
        (Quarry, QuarryLine, QuarryOutput) = (sid, line, output);
        // Paused unless a test is about output while workers are away, so settles don't add stock under the test's feet.
        if (!producing) E.Update(db => db.ProductionLines.Single(l => l.Id == line), l => l.Enabled = false);
        if (connected) E.Update(db => db.ProductionStructures.Single(s => s.Id == sid), s => s.RoadConnection = RoadConnectionState.Connected);
        var warehouses = ProductionWorldTestDb.Warehouses(E.Db);
        var north = await warehouses.CreateAsync(new WarehouseDto { Name = "North", Description = "", WgRegionId = "tmp_n", TownId = ProductionWorldTestDb.Phoenix });
        var south = await warehouses.CreateAsync(new WarehouseDto { Name = "South", Description = "", WgRegionId = "tmp_s", TownId = ProductionWorldTestDb.Phoenix });
        North = north.Id!.Value;
        South = south.Id!.Value;
        NorthStorage = E.Db.Warehouses.AsNoTracking().Single(w => w.Id == North).DefaultStorageId!.Value;
        SouthStorage = E.Db.Warehouses.AsNoTracking().Single(w => w.Id == South).DefaultStorageId!.Value;
        if (northCap.HasValue) E.Update(db => db.Storages.Single(s => s.Id == NorthStorage), s => s.MaxUnits = northCap);
        if (lanes)
        {
            await Lane(Quarry, North, 432m);
            await Lane(Quarry, South, 864m);
            await Lane(North, South, 216m);
        }
        return this;
    }

    public Task<TransportLaneDto> Lane(int from, int to, decimal road) =>
        Lanes.CreateAsync(new TransportLaneDto { FromStructureId = from, ToStructureId = to, RoadDistance = road });

    public void Advance(TimeSpan by) => E.Advance(by);

    public void Advance(int seconds) => E.Advance(TimeSpan.FromSeconds(seconds));

    public long Amount(int storage, int blueprint) => E.Amount(storage, blueprint);

    public Task<StockResult> Put(int storage, long amount, int blueprint = Cobblestone) => E.AdjustAsync(storage, blueprint, amount);

    public TransportOrder Order(int id) => E.Db.TransportOrders.AsNoTracking()
        .Include(o => o.Lines).Include(o => o.Stops).ThenInclude(s => s.Lines).Include(o => o.Assignments).Single(o => o.Id == id);

    public List<TransportEvent> Events(int id) =>
        E.Db.TransportEvents.AsNoTracking().Where(e => e.TransportOrderId == id).OrderBy(e => e.Id).ToList();

    public ProductionStructure Structure(int id) => E.Db.ProductionStructures.AsNoTracking().Single(s => s.Id == id);

    public bool LedgerClean() => new StockReconciler(E.Db, E.Clock).RunAsync().Result.Clean;

    public Task<TransportSweepResultDto> Sweep() => Transport.SweepAsync();

    public TransportOrderCreateDto Manual(int to, long amount, int blueprint = Cobblestone, int? from = null) => new()
    {
        SourceStorageId = from ?? QuarryOutput,
        Stops = new List<TransportStopRequestDto>
        {
            new() { StorageId = to, Lines = new List<TransportLineRequestDto> { new() { ItemBlueprintId = blueprint, Amount = amount } } },
        },
    };
}
