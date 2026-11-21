using knkwebapi_v2.Enums;
using knkwebapi_v2.Json;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Logistics;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Services.Territory;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Tests.Services.Stock;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// Town logistics P9 (PandiO/knk-workspace#127) on MySQL: four planner runs of one town racing (two API instances, or
/// a run now during the schedule) make one reservation and one collection order (the town row lock and the open key),
/// and the stock is never reserved twice. A reservation keeps its order (FK Restrict).
/// </summary>
[Trait("Category", "requires-mysql")]
public class LogisticsMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public LogisticsMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private static LogisticsPlanner Planner(KnKDbContext ctx, TimeProvider clock)
    {
        var weather = new ProductionWeatherService(ctx, clock);
        var stock = new StockService(ctx, clock);
        var engine = new ProductionEngine(ctx, stock, new ProductionSettingsReader(ctx), weather,
            new IProductionModifierSource[] { new LevelModifierSource(), new StoredModifierSource(ctx) },
            new IProductionBaseRateProvider[] { new ConfiguredBaseRateProvider() }, clock: clock);
        var transport = new TransportService(ctx, stock, new ProductionSettingsReader(ctx), engine, clock);
        return new LogisticsPlanner(ctx, transport, new ProductionSettingsReader(ctx), clock);
    }

    [MySqlFact]
    public async Task RacingRuns_ReserveOnce_AndMakeOneCollectionOrder()
    {
        var clock = new TestClock { Now = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero) };
        var suffix = Guid.NewGuid().ToString("N")[..10];
        int townId, input, delivery;
        await using (var ctx = _db.NewContext())
        {
            var settings = await ctx.GameSettings.FirstOrDefaultAsync();
            if (settings == null) { settings = new GameSettings(); ctx.GameSettings.Add(settings); }
            settings.ProductionSettingsJson = JsonColumn.Serialize(new ProductionSettingsDto { Enabled = true });
            var town = new Town { Name = "T " + suffix, Description = "t", WgRegionId = "tt_" + suffix, WorldName = "world" };
            ctx.Towns.Add(town);
            var bp = new ItemBlueprint { Name = "Flour " + suffix, DefaultDisplayName = "Flour" };
            ctx.ItemBlueprints.Add(bp);
            await ctx.SaveChangesAsync();
            townId = town.Id;
            var bakery = new ProductionStructure
            {
                Name = "B " + suffix, Description = "", WgRegionId = "b_" + suffix, WorldName = "world", TownId = town.Id,
                RoadConnection = RoadConnectionState.Connected,
            };
            var warehouse = new Warehouse { Name = "W " + suffix, Description = "", WgRegionId = "w_" + suffix, WorldName = "world", TownId = town.Id };
            ctx.ProductionStructures.Add(bakery);
            ctx.Warehouses.Add(warehouse);
            await ctx.SaveChangesAsync();
            var i = new Storage { Name = "in " + suffix, Kind = StorageKind.Domain, DomainId = bakery.Id, Purpose = StoragePurpose.Input, MaxUnits = 500 };
            var d = new Storage { Name = "wh " + suffix, Kind = StorageKind.Domain, DomainId = warehouse.Id, Purpose = StoragePurpose.Delivery };
            ctx.Storages.AddRange(i, d);
            await ctx.SaveChangesAsync();
            (input, delivery) = (i.Id, d.Id);
            warehouse.DefaultStorageId = d.Id;
            ctx.Set<ConsumerReplenishment>().Add(new ConsumerReplenishment
            {
                StorageId = i.Id, StructureId = bakery.Id, ItemBlueprintId = bp.Id, ReorderPoint = 20, TargetLevel = 60,
                CreatedAt = clock.Now.UtcDateTime, UpdatedAt = clock.Now.UtcDateTime,
            });
            await ctx.SaveChangesAsync();
            await new StockService(ctx, clock).AdjustAsync(d.Id, bp.Id, null, 100,
                new StockContext { Reason = StockReason.AdminAdjust, ActorComponent = "test", Note = "test" });
        }

        await Task.WhenAll(Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var ctx = _db.NewContext();
            await Planner(ctx, clock).RunAsync(townId, null);
        }));

        await using (var ctx = _db.NewContext())
        {
            var reservations = await ctx.Set<LogisticsReservation>().AsNoTracking().Where(r => r.TownId == townId).ToListAsync();
            var r = Assert.Single(reservations);
            Assert.Equal((60L, LogisticsReservationStatus.Open), (r.Amount, r.Status));
            var orders = await ctx.TransportOrders.AsNoTracking().Where(o => o.Origin == TransportOrigin.Distribution && o.SourceStorageId == delivery).ToListAsync();
            Assert.Equal(r.TransportOrderId, Assert.Single(orders).Id);
            Assert.Equal(4, await ctx.Set<LogisticsPlannerRun>().CountAsync(x => x.TownId == townId));
            Assert.Equal(1, await ctx.Set<LogisticsPlanEntry>().CountAsync(e => e.ConsumerStorageId == input && e.Outcome == LogisticsPlanOutcome.Reserved));
            await Assert.ThrowsAnyAsync<Exception>(() => ctx.Database.ExecuteSqlRawAsync($"DELETE FROM transport_orders WHERE Id = {orders[0].Id}"));
        }
    }
}
