using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Realtime.Contracts;
using knkwebapi_v2.Services.Map;
using knkwebapi_v2.Tests.Services.Email;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Map;

/// <summary>
/// Overview map L1 (PandiO/knk-workspace#735, OVERVIEW_MAP_DESIGN § 6.1.1, O28; acceptance A01, A02): what the game
/// server reports over the hub becomes one <c>map_region_outlines</c> row per world and region id.
/// </summary>
public sealed class MapRegionOutlineServiceTests : IDisposable
{
    private readonly KnKDbContext _db;
    private readonly ManualClock _clock = new(new DateTime(2026, 11, 18, 12, 0, 0, DateTimeKind.Utc));
    private readonly MapRegionOutlineService _service;

    public MapRegionOutlineServiceTests()
    {
        _db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        _db.Towns.Add(new Town { Id = 1, Name = "Rivia", Description = "", WgRegionId = "domain_1", WorldName = "world" });
        _db.Districts.Add(new District { Id = 2, Name = "Old Quarter", Description = "", WgRegionId = "domain_2", TownId = 1, WorldName = "world" });
        _db.Domains.Add(new Domain { Id = 5, Name = "Hub town", Description = "", WgRegionId = "domain_5", WorldName = "hub" });
        _db.Domains.Add(new Domain { Id = 6, Name = "No world yet", Description = "", WgRegionId = "domain_6" });
        _db.DomainSubRegions.Add(new DomainSubRegion { Id = 1, DomainId = 2, WgRegionId = "domain_2_sub_1" });
        _db.SaveChanges();
        _service = new MapRegionOutlineService(_db, NullLogger<MapRegionOutlineService>.Instance, _clock);
    }

    public void Dispose() => _db.Dispose();

    private static RegionOutlineDto Outline(string region, string world = "world", int size = 10, int minY = 60, int maxY = 90) => new()
    {
        WgRegionId = region,
        WorldName = world,
        Points = new List<OutlinePointDto> { new() { X = 0, Z = 0 }, new() { X = size, Z = 0 }, new() { X = size, Z = size }, new() { X = 0, Z = size } },
        MinY = minY,
        MaxY = maxY,
    };

    private static RegionOutlineReportDto Report(params RegionOutlineDto[] outlines) => new() { Outlines = outlines.ToList() };

    private List<MapRegionOutline> Rows() => _db.MapRegionOutlines.AsNoTracking().OrderBy(o => o.WgRegionId).ToList();

    [Fact]
    public async Task StartupReport_StoresOneRowPerRegionWithPointsYRangeAndDomainLink() // A01
    {
        var result = await _service.ApplyReportAsync(Report(Outline("domain_1", size: 100), Outline("domain_2", minY: 64, maxY: 80)));

        Assert.Equal((2, 0, 0), (result.Stored, result.Unchanged, result.Removed));
        Assert.Empty(result.Rejected);
        var rows = Rows();
        Assert.Equal(new[] { "domain_1", "domain_2" }, rows.Select(r => r.WgRegionId));
        Assert.Equal("[[0,0],[100,0],[100,100],[0,100]]", rows[0].PointsJson);
        Assert.Equal(new[] { (0, 0), (100, 0), (100, 100), (0, 100) }, MapOutlinePoints.Parse(rows[0].PointsJson));
        Assert.Equal((1, "world"), (rows[0].DomainId, rows[0].WorldName));
        Assert.Equal((2, 64, 80), (rows[1].DomainId, rows[1].MinY, rows[1].MaxY));
        Assert.All(rows, r => Assert.Equal(_clock.UtcNow, r.UpdatedAt));
    }

    [Fact]
    public async Task ReportingAgain_UpdatesInPlaceWithoutDuplicates_AndKeepsUnchangedRowsUntouched() // A01 restart, A02
    {
        await _service.ApplyReportAsync(Report(Outline("domain_1"), Outline("domain_2")));
        var firstWrite = _clock.UtcNow;
        _clock.Advance(TimeSpan.FromMinutes(5));

        var result = await _service.ApplyReportAsync(Report(Outline("domain_1", size: 20), Outline("domain_2")));

        Assert.Equal((1, 1), (result.Stored, result.Unchanged));
        var rows = Rows();
        Assert.Equal(2, rows.Count);
        Assert.Equal("[[0,0],[20,0],[20,20],[0,20]]", rows[0].PointsJson);
        Assert.Equal(_clock.UtcNow, rows[0].UpdatedAt);
        Assert.Equal(firstWrite, rows[1].UpdatedAt);
    }

    [Fact]
    public async Task Rename_StoresTheNewIdAndRemovesTheOldRow() // A02
    {
        await _service.ApplyReportAsync(Report(Outline("tempregion_worldtask_7")));

        var result = await _service.ApplyReportAsync(new RegionOutlineReportDto
        {
            Outlines = new() { Outline("domain_2") },
            Removed = new() { new RegionOutlineRefDto { WgRegionId = "tempregion_worldtask_7", WorldName = "world" } },
        });

        Assert.Equal((1, 1), (result.Stored, result.Removed));
        var row = Assert.Single(Rows());
        Assert.Equal(("domain_2", (int?)2), (row.WgRegionId, row.DomainId));
    }

    [Fact]
    public async Task RedefinedInPlace_RemovedNamesTheSameRegion_TheRowIsKept()
    {
        await _service.ApplyReportAsync(Report(Outline("domain_1")));

        await _service.ApplyReportAsync(new RegionOutlineReportDto
        {
            Outlines = new() { Outline("domain_1", size: 30) },
            Removed = new() { new RegionOutlineRefDto { WgRegionId = "DOMAIN_1", WorldName = "world" } },
        });

        Assert.Equal("[[0,0],[30,0],[30,30],[0,30]]", Assert.Single(Rows()).PointsJson);
    }

    [Fact]
    public async Task RegionIdsArePerWorld_AndLinkOnlyToADomainInThatWorld()
    {
        await _service.ApplyReportAsync(Report(Outline("domain_5", world: "hub"), Outline("domain_5", world: "world"),
            Outline("domain_6", world: "anywhere")));

        var rows = _db.MapRegionOutlines.AsNoTracking().OrderBy(o => o.WgRegionId).ThenBy(o => o.WorldName).ToList();
        Assert.Equal(new[] { ("domain_5", "hub", (int?)5), ("domain_5", "world", (int?)null), ("domain_6", "anywhere", (int?)6) },
            rows.Select(r => (r.WgRegionId, r.WorldName, r.DomainId)));
    }

    [Fact]
    public async Task SubRegion_LinksToItsDomain_AndRegionIdsAreStoredLowerCase()
    {
        await _service.ApplyReportAsync(Report(Outline(" Domain_2_Sub_1 ")));

        var row = Assert.Single(Rows());
        Assert.Equal(("domain_2_sub_1", (int?)2), (row.WgRegionId, row.DomainId));
    }

    [Fact]
    public async Task UnknownRegion_IsStoredWithoutADomain()
    {
        await _service.ApplyReportAsync(Report(Outline("arena_2")));

        Assert.Null(Assert.Single(Rows()).DomainId);
    }

    [Fact]
    public async Task InvalidOutlines_AreRejectedWithAReason_AndTheRestIsStored()
    {
        var tooFew = Outline("domain_1");
        tooFew.Points = tooFew.Points!.Take(2).ToList();
        var tooMany = Outline("domain_2");
        tooMany.Points = Enumerable.Range(0, MapRegionOutlineService.MaxPointsPerOutline + 1).Select(i => new OutlinePointDto { X = i, Z = i }).ToList();
        var noWorld = Outline("domain_5", world: " ");
        var longId = Outline(new string('a', MapRegionOutlineService.MaxRegionIdLength + 1));

        var result = await _service.ApplyReportAsync(Report(tooFew, tooMany, noWorld, longId, Outline("arena_2")));

        Assert.Equal(1, result.Stored);
        Assert.Equal(4, result.Rejected.Count);
        Assert.Contains(result.Rejected, r => r.Contains("fewer than three points"));
        Assert.Contains(result.Rejected, r => r.Contains("more than"));
        Assert.Contains(result.Rejected, r => r.Contains("no world"));
        Assert.Contains(result.Rejected, r => r.Contains("region id longer"));
        Assert.Equal("arena_2", Assert.Single(Rows()).WgRegionId);
    }

    [Fact]
    public async Task MinAndMaxY_AreOrdered()
    {
        await _service.ApplyReportAsync(Report(Outline("domain_1", minY: 120, maxY: -10)));

        var row = Assert.Single(Rows());
        Assert.Equal((-10, 120), (row.MinY, row.MaxY));
    }

    [Fact]
    public async Task EmptyReport_ChangesNothing()
    {
        var result = await _service.ApplyReportAsync(new RegionOutlineReportDto());

        Assert.Equal((0, 0, 0), (result.Stored, result.Unchanged, result.Removed));
        Assert.Empty(Rows());
    }

    [Fact]
    public void Parse_ReturnsEmptyForAnythingButPairs()
    {
        Assert.Empty(MapOutlinePoints.Parse(null));
        Assert.Empty(MapOutlinePoints.Parse("not json"));
        Assert.Empty(MapOutlinePoints.Parse("[[1,2,3]]"));
        Assert.Equal(new[] { (1, 2) }, MapOutlinePoints.Parse("[[1,2]]"));
    }
}
