using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// StreetService: the existing create/update/get/search behaviour (never covered before road
/// navigation Phase 1 touched it) and the road edge counts on StreetDto (DESIGN §3.7, plan R32).
/// </summary>
public class StreetServiceTests : IDisposable
{
    private readonly KnKDbContext _db;
    private readonly StreetService _service;

    public StreetServiceTests()
    {
        _db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);
        _db.Towns.Add(new Town { Id = 1, Name = "Rivia", Description = "", WgRegionId = "town_rivia" });
        _db.Districts.Add(new District { Id = 2, Name = "Old Quarter", Description = "", WgRegionId = "d", TownId = 1 });
        _db.Districts.Add(new District { Id = 3, Name = "Harbour", Description = "", WgRegionId = "h", TownId = 1 });
        _db.SaveChanges();

        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<StreetMappingProfile>();
            cfg.AddProfile<PagedQueryMappingProfile>();
        }).CreateMapper();
        _service = new StreetService(new StreetRepository(_db), new DistrictRepository(_db), new RoadNetworkRepository(_db), mapper);
    }

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Create_SavesTheStreetWithItsDistricts()
    {
        var created = await _service.CreateAsync(new StreetDto { Name = "High Street", DistrictIds = new() { 2, 3 } });

        Assert.True(created.Id > 0);
        Assert.Equal("High Street", created.Name);
        var stored = await _service.GetByIdAsync(created.Id!.Value);
        Assert.Equal(new[] { "Harbour", "Old Quarter" }, stored!.Districts!.Select(d => d.Name).OrderBy(n => n));
        Assert.Equal("Rivia", stored.Districts!.First().Town!.Name);
    }

    [Fact]
    public async Task Create_RejectsAMissingNameOrUnknownDistrict()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new StreetDto { Name = " " }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new StreetDto { Name = "x", DistrictIds = new() { 99 } }));
        await Assert.ThrowsAsync<ArgumentNullException>(() => _service.CreateAsync(null!));
    }

    [Fact]
    public async Task Update_RenamesAndReplacesDistricts()
    {
        var created = await _service.CreateAsync(new StreetDto { Name = "High Street", DistrictIds = new() { 2 } });

        await _service.UpdateAsync(created.Id!.Value, new StreetDto { Name = "Low Street", DistrictIds = new() { 3 } });

        var stored = await _service.GetByIdAsync(created.Id.Value);
        Assert.Equal("Low Street", stored!.Name);
        Assert.Equal("Harbour", Assert.Single(stored.Districts!).Name);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpdateAsync(999, new StreetDto { Name = "x" }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(created.Id.Value, new StreetDto { Name = "" }));
    }

    [Fact]
    public async Task Delete_RemovesTheStreet()
    {
        var created = await _service.CreateAsync(new StreetDto { Name = "High Street" });

        await _service.DeleteAsync(created.Id!.Value);

        Assert.Null(await _service.GetByIdAsync(created.Id.Value));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.DeleteAsync(created.Id.Value));
    }

    [Fact]
    public async Task GetById_ReturnsNullForBadIds()
    {
        Assert.Null(await _service.GetByIdAsync(0));
        Assert.Null(await _service.GetByIdAsync(123));
    }

    [Fact]
    public async Task Search_FiltersByNameAndPages()
    {
        foreach (var name in new[] { "High Street", "Mill Lane", "High Road" })
        {
            await _service.CreateAsync(new StreetDto { Name = name });
        }

        var page = await _service.SearchAsync(new PagedQueryDto { SearchTerm = "high", PageSize = 1, PageNumber = 2, SortBy = "name" });

        Assert.Equal(2, page.TotalCount);
        Assert.Equal("High Street", Assert.Single(page.Items).name);
    }

    [Fact]
    public async Task RoadCounts_AreFilledFromTheLabelledEdges()
    {
        var high = await _service.CreateAsync(new StreetDto { Name = "High Street" });
        var mill = await _service.CreateAsync(new StreetDto { Name = "Mill Lane" });
        var tile = new RoadTile { World = "world" };
        var a = new RoadNode { World = "world", X = 0, Y = 64, Z = 0, Tile = tile };
        var b = new RoadNode { World = "world", X = 10, Y = 64, Z = 0, Tile = tile };
        var c = new RoadNode { World = "world", X = 20, Y = 64, Z = 0, Tile = tile };
        _db.RoadEdges.AddRange(
            new RoadEdge { World = "world", Tile = tile, FromNode = a, ToNode = b, Length = 10.5, StreetId = high.Id, StreetSource = RoadStreetSource.Inferred },
            new RoadEdge { World = "world", Tile = tile, FromNode = b, ToNode = c, Length = 4.5, StreetId = high.Id, StreetSource = RoadStreetSource.Manual },
            new RoadEdge { World = "world", Tile = tile, FromNode = a, ToNode = c, Length = 99, StreetId = null });
        _db.SaveChanges();

        var one = await _service.GetByIdAsync(high.Id!.Value);
        var all = (await _service.GetAllAsync()).ToDictionary(s => s.Name);

        Assert.Equal((2, 15.0), (one!.EdgeCount, one.TotalLength));
        Assert.Equal((2, 15.0), (all["High Street"].EdgeCount, all["High Street"].TotalLength));
        Assert.Equal((0, 0.0), (all["Mill Lane"].EdgeCount, all["Mill Lane"].TotalLength));
        Assert.Equal(mill.Id, all["Mill Lane"].Id);
    }
}
