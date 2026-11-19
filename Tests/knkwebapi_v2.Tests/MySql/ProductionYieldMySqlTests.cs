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
/// Yield areas and surveys P4 (PandiO/knk-workspace#138) on MySQL: the survey queue, the exclusive-overlap and overlap
/// queries translate; a reported survey materializes a Derived line that produces what the area holds, with the
/// reconciler clean.
/// </summary>
[Trait("Category", "requires-mysql")]
public class ProductionYieldMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public ProductionYieldMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private sealed record Services(ProductionEngine Engine, ProductionAreaService Areas, ProductionYieldService Yield);

    private static Services Build(knkwebapi_v2.Properties.KnKDbContext ctx, TimeProvider clock)
    {
        var settings = new ProductionSettingsReader(ctx);
        var weather = new ProductionWeatherService(ctx, clock);
        var calculator = new ProductionYieldCalculator(ctx);
        var engine = new ProductionEngine(ctx, new StockService(ctx, clock), settings, weather,
            new IProductionModifierSource[] { new LevelModifierSource(), new StoredModifierSource(ctx), new PrimaryOutputModifierSource(ctx) },
            new IProductionBaseRateProvider[] { new ConfiguredBaseRateProvider(), new DerivedBaseRateProvider(calculator), new HybridBaseRateProvider(calculator) },
            clock: clock);
        var yield = new ProductionYieldService(ctx, calculator, engine, settings, clock);
        return new Services(engine, new ProductionAreaService(ctx, settings, engine, yield, clock), yield);
    }

    [MySqlFact]
    public async Task ASurveyedDerivedLogger_ProducesWhatItsAreaHolds_AndExclusiveAreasDontOverlap()
    {
        var clock = new TestClock { Now = new DateTimeOffset(2026, 11, 19, 12, 0, 0, TimeSpan.Zero) };
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var world = "w_" + suffix;
        int structureId, quarryId, output, oak;
        await using (var ctx = _db.NewContext())
        {
            var settings = await ctx.GameSettings.FirstOrDefaultAsync();
            if (settings == null) { settings = new GameSettings(); ctx.GameSettings.Add(settings); }
            settings.ProductionSettingsJson = JsonColumn.Serialize(new ProductionSettingsDto { Enabled = true });
            var town = new Town { Name = "Y " + suffix, Description = "t", WgRegionId = "ty_" + suffix, WorldName = world };
            ctx.Towns.Add(town);
            var bp = new ItemBlueprint { Name = "Oak " + suffix, DefaultDisplayName = "Oak" };
            ctx.ItemBlueprints.Add(bp);
            await ctx.SaveChangesAsync();
            oak = bp.Id;
            var kind = new ProductionKind
            {
                Name = "Logger " + suffix, AreaModel = ProductionAreaModel.SharedRadius, ResourceCategory = ResourceCategory.Wood,
            };
            kind.YieldTable.Add(new ProductionYieldRow { UnitKey = "TREE:OAK:*", ItemBlueprintId = oak, RatePerUnitPerHour = 2m });
            for (int l = 1; l <= 10; l++) kind.Levels.Add(ProductionCatalogSeed.DefaultLevel(0, l, 32, 4));
            ctx.ProductionKinds.Add(kind);
            await ctx.SaveChangesAsync();
            var structure = new ProductionStructure
            {
                Name = "L " + suffix, Description = "", WgRegionId = "l_" + suffix, WorldName = world, TownId = town.Id,
                ProductionKindId = kind.Id, YieldSource = ProductionRateSource.Derived,
            };
            var quarry = new ProductionStructure
            {
                Name = "Q " + suffix, Description = "", WgRegionId = "q_" + suffix, WorldName = world, TownId = town.Id,
                ProductionKindId = 3, YieldSource = ProductionRateSource.Configured,
            };
            ctx.ProductionStructures.AddRange(structure, quarry);
            await ctx.SaveChangesAsync();
            structureId = structure.Id;
            quarryId = quarry.Id;
            var storage = new Storage { Name = "out " + suffix, Kind = StorageKind.Domain, DomainId = structure.Id, Purpose = StoragePurpose.Output };
            ctx.Storages.Add(storage);
            await ctx.SaveChangesAsync();
            output = storage.Id;
        }

        await using (var ctx = _db.NewContext())
        {
            var s = Build(ctx, clock);
            var area = await s.Areas.CreateAsync(new ProductionAreaDto
            {
                ProductionStructureId = structureId, Name = "Forest", Shape = "Radius", Radius = 20, CenterX = 0, CenterY = 64, CenterZ = 0,
                WorldName = world,
            });
            var due = await s.Areas.SurveyDueAsync(ProductionAreaService.MaxSurveyDue);
            Assert.Equal("Creation", due.Single(d => d.Area.Id == area.Id).Trigger);
            var report = await s.Areas.ReportSurveyAsync(area.Id, new AreaSurveyDto
            {
                Units = new() { new() { UnitKey = "TREE:OAK:LARGE", Count = 10 }, new() { UnitKey = "TREE:OAK:SMALL", Count = 4, SharedBy = 2 } },
            });
            Assert.Single(report.Materialization!.LinesCreated);
            Assert.DoesNotContain(await s.Areas.SurveyDueAsync(ProductionAreaService.MaxSurveyDue), d => d.Area.Id == area.Id);
            Assert.Empty(await s.Areas.OverlapsAsync(area.Id));

            await s.Areas.CreateAsync(new ProductionAreaDto
            {
                ProductionStructureId = quarryId, Name = "Pit", Shape = "Cuboid", WorldName = world,
                MinX = 100, MaxX = 110, MinY = 0, MaxY = 10, MinZ = 100, MaxZ = 110,
            });
            await Assert.ThrowsAsync<InvalidOperationException>(() => s.Areas.CreateAsync(new ProductionAreaDto
            {
                ProductionStructureId = quarryId, Name = "Pit 2", Shape = "Cuboid", WorldName = world,
                MinX = 110, MaxX = 120, MinY = 0, MaxY = 10, MinZ = 100, MaxZ = 110,
            }));
        }

        clock.Now += TimeSpan.FromHours(1);
        await using (var ctx = _db.NewContext())
        {
            await Build(ctx, clock).Engine.SettleStructureAsync(structureId);
            // 10 trees × 2 + 4 shared trees × 2 ÷ 2 = 24 per hour.
            Assert.Equal(24L, await ctx.StockLines.AsNoTracking().Where(l => l.StorageId == output && l.ItemBlueprintId == oak).SumAsync(l => l.Amount));
            Assert.Equal(ProductionRateSource.Derived, (await ctx.ProductionLines.AsNoTracking().SingleAsync(l => l.ProductionStructureId == structureId)).RateSource);
            Assert.True((await new StockReconciler(ctx, clock).RunAsync()).Clean);
        }
    }
}
