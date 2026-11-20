using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Json;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Services.Territory;
using knkwebapi_v2.Tests.Services.Stock;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// Transport core P5 (PandiO/knk-workspace#79) on MySQL: four transport sweeps racing make one automatic order per
/// source (the structure row lock) and apply each step once (the order row lock): the cargo is loaded and delivered
/// once, the worker leaves and comes back once, and the reconciler stays clean. A storage an order names can't be
/// deleted (FK Restrict).
/// </summary>
[Trait("Category", "requires-mysql")]
public class TransportMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public TransportMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private static TransportService Transport(KnKDbContext ctx, TimeProvider clock)
    {
        var weather = new ProductionWeatherService(ctx, clock);
        var stock = new StockService(ctx, clock);
        var engine = new ProductionEngine(ctx, stock, new ProductionSettingsReader(ctx), weather,
            new IProductionModifierSource[] { new LevelModifierSource(), new StoredModifierSource(ctx) },
            new IProductionBaseRateProvider[] { new ConfiguredBaseRateProvider() }, clock: clock);
        return new TransportService(ctx, stock, new ProductionSettingsReader(ctx), engine, clock);
    }

    private async Task RaceAsync(TimeProvider clock)
    {
        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var ctx = _db.NewContext();
            await Transport(ctx, clock).SweepAsync();
        }));
    }

    [MySqlFact]
    public async Task RacingSweeps_MakeOneAutomaticOrder_AndApplyEachStepOnce()
    {
        var clock = new TestClock { Now = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.Zero) };
        var suffix = Guid.NewGuid().ToString("N")[..10];
        int structureId, output, delivery, blueprint;
        await using (var ctx = _db.NewContext())
        {
            var settings = await ctx.GameSettings.FirstOrDefaultAsync();
            if (settings == null) { settings = new GameSettings(); ctx.GameSettings.Add(settings); }
            settings.ProductionSettingsJson = JsonColumn.Serialize(new ProductionSettingsDto { Enabled = true });
            var town = new Town { Name = "T " + suffix, Description = "t", WgRegionId = "tt_" + suffix, WorldName = "world" };
            ctx.Towns.Add(town);
            var bp = new ItemBlueprint { Name = "Log " + suffix, DefaultDisplayName = "Log" };
            ctx.ItemBlueprints.Add(bp);
            await ctx.SaveChangesAsync();
            blueprint = bp.Id;
            var structure = new ProductionStructure
            {
                Name = "L " + suffix, Description = "", WgRegionId = "l_" + suffix, WorldName = "world", TownId = town.Id,
                RoadConnection = RoadConnectionState.Connected, TransportCapacity = 40,
            };
            var warehouse = new Warehouse { Name = "W " + suffix, Description = "", WgRegionId = "w_" + suffix, WorldName = "world", TownId = town.Id };
            ctx.ProductionStructures.Add(structure);
            ctx.Warehouses.Add(warehouse);
            await ctx.SaveChangesAsync();
            structureId = structure.Id;
            var o = new Storage { Name = "out " + suffix, Kind = StorageKind.Domain, DomainId = structure.Id, Purpose = StoragePurpose.Output, MaxUnits = 100 };
            var d = new Storage { Name = "in " + suffix, Kind = StorageKind.Domain, DomainId = warehouse.Id, Purpose = StoragePurpose.Delivery };
            ctx.Storages.AddRange(o, d);
            await ctx.SaveChangesAsync();
            (output, delivery) = (o.Id, d.Id);
            warehouse.DefaultStorageId = d.Id;
            ctx.TransportLanes.Add(new TransportLane { FromStructureId = structure.Id, ToStructureId = warehouse.Id, RoadDistance = 431.7m, MeasuredAt = clock.Now.UtcDateTime });
            await ctx.SaveChangesAsync();
            await new StockService(ctx, clock).AdjustAsync(o.Id, bp.Id, null, 90,
                new StockContext { Reason = StockReason.AdminAdjust, ActorComponent = "test", Note = "test" });
        }

        await RaceAsync(clock);
        await using (var ctx = _db.NewContext())
        {
            var order = await ctx.TransportOrders.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.SourceStructureId == structureId);
            Assert.Equal((TransportStatus.InTransit, 40L), (order.Status, order.Lines.Single().Loaded));
            Assert.Equal(50, await ctx.StockLines.AsNoTracking().Where(l => l.StorageId == output).SumAsync(l => l.Amount));
            Assert.Equal(1, (await ctx.ProductionStructures.AsNoTracking().SingleAsync(s => s.Id == structureId)).WorkersPresent);
        }

        clock.Now += TimeSpan.FromSeconds(100); // 431.7 blocks at 4.317 b/s
        await RaceAsync(clock);
        await using (var ctx = _db.NewContext())
        {
            var orders = await ctx.TransportOrders.AsNoTracking().Where(x => x.SourceStructureId == structureId).OrderBy(x => x.Id).ToListAsync();
            Assert.Equal(TransportStatus.Delivered, orders[0].Status);
            Assert.Equal(40, await ctx.StockLines.AsNoTracking().Where(l => l.StorageId == delivery && l.ItemBlueprintId == blueprint).SumAsync(l => l.Amount));
            Assert.Equal(2, await ctx.StockTransactions.CountAsync(t => t.SourceType == nameof(TransportOrder) && t.SourceRef == orders[0].Id.ToString()));
            Assert.Single(orders); // 50 left is under the threshold of 75
            Assert.Equal(2, (await ctx.ProductionStructures.AsNoTracking().SingleAsync(s => s.Id == structureId)).WorkersPresent);
            Assert.True((await new StockReconciler(ctx, clock).RunAsync()).Clean);

            await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync($"DELETE FROM storages WHERE Id = {orders[0].CargoStorageId}"));
        }
    }
}
