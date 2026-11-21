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
/// Player harvesting P8a (PandiO/knk-workspace#685) guarantees only a real MySQL can prove: concurrent first harvests in
/// one area make one pool and draw every cost from it (the area row lock), one key books once under concurrency, the
/// players' fractions add up (the user row lock), and the reconciler stays clean.
/// </summary>
[Trait("Category", "requires-mysql")]
public class PlayerHarvestMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public PlayerHarvestMySqlTests(MySqlTestDatabase db) => _db = db;

    private static HarvestService Build(knkwebapi_v2.Properties.KnKDbContext ctx, TimeProvider clock)
    {
        var settings = new ProductionSettingsReader(ctx);
        var weather = new ProductionWeatherService(ctx, clock);
        var engine = new ProductionEngine(ctx, new StockService(ctx, clock), settings, weather,
            new IProductionModifierSource[] { new LevelModifierSource(), new StoredModifierSource(ctx) },
            new IProductionBaseRateProvider[] { new ConfiguredBaseRateProvider() }, clock: clock);
        return new HarvestService(ctx, new StockService(ctx, clock), settings, engine, clock);
    }

    private sealed record Seeded(int User, int Rule, int Area, int Output, int Blueprint);

    private async Task<Seeded> SeedAsync()
    {
        await using var ctx = _db.NewContext();
        var suffix = Guid.NewGuid().ToString("N")[..10];
        var settings = await ctx.GameSettings.FirstOrDefaultAsync();
        if (settings == null) { settings = new GameSettings(); ctx.GameSettings.Add(settings); }
        settings.ProductionSettingsJson = JsonColumn.Serialize(new ProductionSettingsDto
        {
            Enabled = true, Harvest = new ProductionHarvestSettingsDto { DefaultPoolCapacity = 1000m },
        });
        var town = new Town { Name = "H " + suffix, Description = "t", WgRegionId = "th_" + suffix, WorldName = "world" };
        var user = new User { Username = "p8_" + suffix };
        var bp = new ItemBlueprint { Name = "Coal " + suffix, DefaultDisplayName = "Coal" };
        ctx.AddRange(town, user, bp);
        await ctx.SaveChangesAsync();
        var structure = new ProductionStructure
        {
            Name = "M " + suffix, Description = "", WgRegionId = "m_" + suffix, WorldName = "world", TownId = town.Id,
            ProductionKindId = 4, YieldSource = ProductionRateSource.Configured,
        };
        ctx.ProductionStructures.Add(structure);
        await ctx.SaveChangesAsync();
        var output = new Storage { Name = "out " + suffix, Kind = StorageKind.Domain, DomainId = structure.Id, Purpose = StoragePurpose.Output };
        var area = new ProductionArea
        {
            ProductionStructureId = structure.Id, Name = "Pit", Model = ProductionAreaModel.ExclusiveVolume, Shape = ProductionAreaShape.Cuboid,
            MaxX = 9, MaxY = 9, MaxZ = 9, Size = 1000,
        };
        var rule = new HarvestRule
        {
            Name = "Coal " + suffix, ProductionKindId = 4, TargetsJson = "[\"COAL_ORE\"]", YieldItemBlueprintId = bp.Id, PoolCost = 10m,
        };
        ctx.AddRange(output, area, rule);
        await ctx.SaveChangesAsync();
        return new Seeded(user.Id, rule.Id, area.Id, output.Id, bp.Id);
    }

    private async Task<List<(HarvestEntryResultDto? Result, Exception? Error)>> InParallelAsync(int count, Func<int, HarvestReportEntryDto> entry)
    {
        var clock = new TestClock { Now = new DateTimeOffset(2026, 11, 21, 12, 0, 0, TimeSpan.Zero) };
        var start = new TaskCompletionSource();
        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            await start.Task;
            try
            {
                await using var ctx = _db.NewContext();
                var r = await Build(ctx, clock).ReportAsync(new HarvestReportDto { Entries = new() { entry(i) } });
                return ((HarvestEntryResultDto?)r.Entries.Single(), (Exception?)null);
            }
            catch (Exception ex)
            {
                return ((HarvestEntryResultDto?)null, ex);
            }
        }).ToList();
        start.SetResult();
        return (await Task.WhenAll(tasks)).ToList();
    }

    [MySqlFact]
    public async Task ConcurrentFirstHarvests_MakeOnePool_AndDrawEveryCost_AndTheFractionsAddUp()
    {
        var s = await SeedAsync();
        var results = await InParallelAsync(10, i => new HarvestReportEntryDto
        {
            IdempotencyKey = $"race-{s.Rule}-{i}", UserId = s.User, HarvestRuleId = s.Rule, ProductionAreaId = s.Area, Breaks = 2,
        });

        Assert.All(results, r => Assert.Null(r.Error));
        Assert.All(results, r => Assert.Equal("Applied", r.Result!.Status));
        await using var ctx = _db.NewContext();
        var pool = Assert.Single(ctx.Set<ResourcePool>().Where(p => p.ProductionAreaId == s.Area));
        Assert.Equal(1000m - 10 * 2 * 10m, pool.Current);
        // 20 breaks × 1 unit: 5 to the player, 15 to the town, whatever the order (fractions carried under the user lock).
        Assert.Equal(5, results.Sum(r => r.Result!.WorkerUnits));
        Assert.Equal(15, ctx.StockLines.Where(l => l.StorageId == s.Output && l.ItemBlueprintId == s.Blueprint).Sum(l => l.Amount));
        Assert.True((await new StockReconciler(ctx).RunAsync()).Clean);
    }

    [MySqlFact]
    public async Task ConcurrentHarvestsOfDifferentPlayers_DrawEveryCostFromOnePool()
    {
        var s = await SeedAsync();
        var users = new List<int>();
        await using (var ctx = _db.NewContext())
        {
            var made = Enumerable.Range(0, 10).Select(i => new User { Username = $"p8_{s.Rule}_{i}" }).ToList();
            ctx.Users.AddRange(made);
            await ctx.SaveChangesAsync();
            users.AddRange(made.Select(u => u.Id));
        }
        var results = await InParallelAsync(10, i => new HarvestReportEntryDto
        {
            IdempotencyKey = $"many-{s.Rule}-{i}", UserId = users[i], HarvestRuleId = s.Rule, ProductionAreaId = s.Area, Breaks = 4,
        });

        Assert.All(results, r => Assert.Null(r.Error));
        await using var check = _db.NewContext();
        var pool = Assert.Single(check.Set<ResourcePool>().Where(p => p.ProductionAreaId == s.Area));
        Assert.Equal(1000m - 10 * 4 * 10m, pool.Current); // every draw counted: the area row lock
        Assert.Equal(10, results.Sum(r => r.Result!.WorkerUnits));
        Assert.Equal(30, check.StockLines.Where(l => l.StorageId == s.Output && l.ItemBlueprintId == s.Blueprint).Sum(l => l.Amount));
    }

    [MySqlFact]
    public async Task OneKey_BooksOnce_UnderConcurrency()
    {
        var s = await SeedAsync();
        var key = $"same-{s.Rule}";
        var results = await InParallelAsync(8, _ => new HarvestReportEntryDto
        {
            IdempotencyKey = key, UserId = s.User, HarvestRuleId = s.Rule, ProductionAreaId = s.Area, Breaks = 4,
        });

        Assert.All(results, r => Assert.Null(r.Error));
        Assert.Single(results.Where(r => r.Result!.Status == "Applied"));
        Assert.Single(results.Select(r => r.Result!.PlayerHarvestId).Distinct());
        await using var ctx = _db.NewContext();
        Assert.Single(ctx.Set<PlayerHarvest>().Where(h => h.IdempotencyKey == key));
        Assert.Equal(3, ctx.StockLines.Where(l => l.StorageId == s.Output && l.ItemBlueprintId == s.Blueprint).Sum(l => l.Amount));
        Assert.Equal(960m, ctx.Set<ResourcePool>().Single(p => p.ProductionAreaId == s.Area).Current);
    }
}
