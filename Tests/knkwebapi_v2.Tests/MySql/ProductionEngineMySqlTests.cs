using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Json;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Services.Territory;
using knkwebapi_v2.Tests.Services.Stock;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// Production engine P3 (PandiO/knk-workspace#86) on MySQL: four sweeps racing on the same due lines settle each period
/// once (the line row lock), the yield moves keep the reconciler clean, and a kind used by a structure can't be deleted
/// (FK Restrict).
/// </summary>
[Trait("Category", "requires-mysql")]
public class ProductionEngineMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public ProductionEngineMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private static ProductionEngine Engine(knkwebapi_v2.Properties.KnKDbContext ctx, TimeProvider clock)
    {
        var weather = new ProductionWeatherService(ctx, clock);
        return new ProductionEngine(ctx, new StockService(ctx, clock), new ProductionSettingsReader(ctx), weather,
            new IProductionModifierSource[] { new LevelModifierSource(), new WeatherModifierSource(ctx, weather), new StoredModifierSource(ctx) },
            new IProductionBaseRateProvider[] { new ConfiguredBaseRateProvider() }, clock: clock);
    }

    [MySqlFact]
    public async Task ConcurrentSweeps_SettleEachPeriodOnce_AndTheLedgerStaysBalanced()
    {
        var clock = new TestClock { Now = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero) };
        var suffix = Guid.NewGuid().ToString("N")[..10];
        int lineCount = 5, output;
        var lineIds = new List<int>();
        await using (var ctx = _db.NewContext())
        {
            var settings = await ctx.GameSettings.FirstOrDefaultAsync();
            if (settings == null) { settings = new GameSettings(); ctx.GameSettings.Add(settings); }
            settings.ProductionSettingsJson = JsonColumn.Serialize(new ProductionSettingsDto { Enabled = true });
            var town = new Town { Name = "P " + suffix, Description = "t", WgRegionId = "t_" + suffix, WorldName = "world" };
            ctx.Towns.Add(town);
            var bp = new ItemBlueprint { Name = "Cobble " + suffix, DefaultDisplayName = "Cobble" };
            ctx.ItemBlueprints.Add(bp);
            await ctx.SaveChangesAsync();
            var structure = new ProductionStructure
            {
                Name = "Q " + suffix, Description = "", WgRegionId = "q_" + suffix, WorldName = "world", TownId = town.Id,
                ProductionKindId = 3,
            };
            ctx.ProductionStructures.Add(structure);
            await ctx.SaveChangesAsync();
            var storage = new Storage { Name = "out " + suffix, Kind = StorageKind.Domain, DomainId = structure.Id, Purpose = StoragePurpose.Output };
            ctx.Storages.Add(storage);
            await ctx.SaveChangesAsync();
            output = storage.Id;
            var start = clock.Now.UtcDateTime;
            for (int i = 0; i < lineCount; i++)
            {
                var other = new ItemBlueprint { Name = $"Out {i} {suffix}", DefaultDisplayName = "x" };
                ctx.ItemBlueprints.Add(other);
                await ctx.SaveChangesAsync();
                var line = new ProductionLine
                {
                    ProductionStructureId = structure.Id, ItemBlueprintId = other.Id, ConfiguredRatePerHour = 60m,
                    AnchorAt = start, LastSettledAt = start, NextSettleAt = start.AddMinutes(10), CreatedAt = start,
                };
                ctx.ProductionLines.Add(line);
                await ctx.SaveChangesAsync();
                lineIds.Add(line.Id);
            }
        }

        clock.Now += TimeSpan.FromMinutes(30);
        var sweeps = Enumerable.Range(0, 4).Select(async _ =>
        {
            await using var ctx = _db.NewContext();
            return await Engine(ctx, clock).SweepAsync();
        }).ToList();
        await Task.WhenAll(sweeps);

        await using (var ctx = _db.NewContext())
        {
            var total = await ctx.StockLines.AsNoTracking().Where(l => l.StorageId == output).SumAsync(l => l.Amount);
            Assert.Equal(30L * lineCount, total); // 30 minutes at 60/h per line, once, whatever sweep got there first
            Assert.Equal(lineCount, await ctx.ProductionCycleRecords.CountAsync(r => lineIds.Contains(r.ProductionLineId)));
            Assert.True((await new StockReconciler(ctx, clock).RunAsync()).Clean);
        }
    }

    [MySqlFact]
    public async Task AKindInUse_CantBeDeleted()
    {
        var suffix = Guid.NewGuid().ToString("N")[..10];
        await using var ctx = _db.NewContext();
        var kind = new ProductionKind { Name = "K " + suffix, AreaModel = ProductionAreaModel.Pasture, ResourceCategory = ResourceCategory.Animals };
        ctx.ProductionKinds.Add(kind);
        var town = new Town { Name = "T " + suffix, Description = "t", WgRegionId = "tk_" + suffix, WorldName = "world" };
        ctx.Towns.Add(town);
        await ctx.SaveChangesAsync();
        ctx.ProductionStructures.Add(new ProductionStructure
        {
            Name = "S " + suffix, Description = "", WgRegionId = "sk_" + suffix, WorldName = "world", TownId = town.Id, ProductionKindId = kind.Id,
        });
        await ctx.SaveChangesAsync();
        await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
            ctx.Database.ExecuteSqlRawAsync($"DELETE FROM production_kinds WHERE Id = {kind.Id}").ContinueWith(t =>
            {
                if (t.Exception != null) throw new DbUpdateException("refused", t.Exception.InnerException);
                return t.Result;
            }));
    }
}
