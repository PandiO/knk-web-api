using knkwebapi_v2.Configuration;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services.WorldAnalytics;
using knkwebapi_v2.Tests.Services.WorldAnalytics;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// The MySQL-only paths of world analytics (KNG-34 link 7): the AddWorldAnalytics migration applies,
/// the multi-row upserts add samples/counts and keep the maximum of unique players, a batch id
/// posted concurrently applies once, the grouped reads and ExecuteDelete retention translate.
/// </summary>
[Trait("Category", "requires-mysql")]
public class WorldAnalyticsMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;
    private readonly WorldAnalyticsOptions _options = new();
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Amsterdam");
    private readonly DateTime _now = DateTime.UtcNow;

    public WorldAnalyticsMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private WorldAnalyticsIngestionService Ingestion(Properties.KnKDbContext ctx) =>
        new(new WorldAnalyticsRepository(ctx), _options, _zone, () => _now);

    [MySqlFact]
    public async Task Upserts_AddUp_AndKeepTheHighestUniquePlayers()
    {
        var world = "w" + Guid.NewGuid().ToString("N")[..10];
        var menu = "m." + Guid.NewGuid().ToString("N")[..10];
        int domainId;
        await using (var ctx = _db.NewContext())
        {
            var domain = new Domain { Name = "Analytics " + world, Description = "", WgRegionId = "r" + world };
            ctx.Domains.Add(domain);
            await ctx.SaveChangesAsync();
            domainId = domain.Id;
        }

        for (var i = 0; i < 2; i++)
        {
            var batch = WorldAnalyticsTestDb.Batch(_now.AddMinutes(-1));
            batch.MovementCells!.Add(WorldAnalyticsTestDb.Cell(-3, 4, 2, world));
            batch.MovementCells.Add(WorldAnalyticsTestDb.Cell(-3, 4, 1, world));
            batch.MenuSteps!.Add(WorldAnalyticsTestDb.Step(menu, "action:menu.open", 2, "denied"));
            batch.DomainInteractions!.Add(WorldAnalyticsTestDb.Domain("enter", 3, i == 0 ? 5 : 2, regionId: "r" + world));
            await using var ctx = _db.NewContext();
            var result = await Ingestion(ctx).IngestAsync(batch);
            Assert.Equal(4, result.Accepted);
        }

        await using (var ctx = _db.NewContext())
        {
            var cell = await ctx.WorldMovementCellDailies.SingleAsync(c => c.World == world);
            Assert.Equal((-3, 4, 6), (cell.CellX, cell.CellZ, cell.Samples));
            Assert.Equal(4, (await ctx.MenuFunnelDailies.SingleAsync(s => s.MenuKey == menu)).Count);
            var domain = await ctx.DomainInteractionDailies.SingleAsync(d => d.DomainId == domainId);
            Assert.Equal((6, 5), (domain.Count, domain.UniquePlayers));

            var query = new WorldAnalyticsQueryService(new WorldAnalyticsRepository(ctx), _options, _zone, () => _now);
            var (from, to) = query.ResolveRange(null, null);
            var map = await query.GetHeatmapAsync(world, from, to, 32);
            Assert.Equal((-2, 2, 6L), (map.Cells.Single().X, map.Cells.Single().Z, map.Cells.Single().Samples));
            Assert.Contains(await query.GetWorldsAsync(from, to), w => w.World == world && w.Samples == 6);
            Assert.Equal(4, (await query.GetMenuFunnelsAsync(menu, from, to)).Menus.Single().Steps.Single().Count);
            Assert.Equal("Analytics " + world, (await query.GetDomainsAsync("enter", from, to)).Domains.Single(d => d.DomainId == domainId).Name);
        }
    }

    [MySqlFact]
    public async Task SameBatchIdInParallel_AppliesOnce()
    {
        var world = "w" + Guid.NewGuid().ToString("N")[..10];
        var id = Guid.NewGuid();

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(async _ =>
        {
            var batch = WorldAnalyticsTestDb.Batch(_now.AddMinutes(-1), id);
            batch.MovementCells!.Add(WorldAnalyticsTestDb.Cell(0, 0, 1, world));
            await using var ctx = _db.NewContext();
            return await Ingestion(ctx).IngestAsync(batch);
        }));

        Assert.Single(results, r => !r.Duplicate);
        await using var check = _db.NewContext();
        Assert.Equal(1, (await check.WorldMovementCellDailies.SingleAsync(c => c.World == world)).Samples);
    }

    [MySqlFact]
    public async Task Retention_DeletesOldRows()
    {
        var world = "w" + Guid.NewGuid().ToString("N")[..10];
        await using (var ctx = _db.NewContext())
        {
            ctx.WorldMovementCellDailies.Add(new WorldMovementCellDaily { Day = new DateOnly(2020, 1, 1), World = world, CellSize = 16, Samples = 1 });
            ctx.WorldMovementCellDailies.Add(new WorldMovementCellDaily { Day = DateOnly.FromDateTime(_now), World = world, CellSize = 16, Samples = 1 });
            await ctx.SaveChangesAsync();
        }

        await using (var ctx = _db.NewContext())
        {
            Assert.True(await new WorldAnalyticsRepository(ctx).PurgeDailyBeforeAsync(new DateOnly(2021, 1, 1)) >= 1);
        }

        await using var check = _db.NewContext();
        Assert.Equal(DateOnly.FromDateTime(_now), (await check.WorldMovementCellDailies.SingleAsync(c => c.World == world)).Day);
    }
}
