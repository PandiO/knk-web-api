using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Off-road navigation phase 5.1 (PandiO/knk-workspace#708, DESIGN § 6.3, A12): the rules of a landmark, against the real
/// repository and Location service on EF InMemory. Name unique per world, the pin (an id or an embedded Location), the
/// optional domain link, the extent, the preference range, the kind, and the read DTO the plugin's provider uses.
/// </summary>
public class LandmarkServiceTests : IDisposable
{
    private readonly KnKDbContext _context;
    private readonly LandmarkService _service;

    public LandmarkServiceTests()
    {
        _context = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        var mapper = new MapperConfiguration(cfg => cfg.AddMaps(typeof(LandmarkMappingProfile).Assembly)).CreateMapper();
        var locations = new LocationRepository(_context);
        _service = new LandmarkService(new LandmarkRepository(_context), locations, new LocationService(locations, mapper), mapper);

        _context.Towns.Add(new Town { Id = 1, Name = "Cinix", Description = "t", WgRegionId = "domain_1", WorldName = "world" });
        _context.Structures.Add(new Structure { Id = 2, Name = "Keep", Description = "s", WgRegionId = "domain_2", DistrictId = 3, StreetId = 4, WorldName = "world" });
        _context.Locations.Add(new Location { Id = 10, Name = "Hall", X = 1, Y = 64, Z = 2, World = "world" });
        _context.Locations.Add(new Location { Id = 11, Name = "Gate", X = 5, Y = 70, Z = 6, World = "world" });
        _context.Locations.Add(new Location { Id = 12, Name = "Nether hall", X = 1, Y = 64, Z = 2, World = "world_nether" });
        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    private static LandmarkCreateDto At(int locationId, string name = "Throne room") => new() { Name = name, LocationId = locationId };

    [Fact]
    public async Task Create_WithDefaults_ReturnsWhatThePluginNeeds()
    {
        var created = await _service.CreateAsync(new LandmarkCreateDto { Name = "  Throne room ", LocationId = 10, DomainId = 2 });

        Assert.Equal("Throne room", created.Name);
        Assert.Equal(10, created.LocationId);
        Assert.Equal("world", created.World);
        Assert.Equal((1d, 64d, 2d), (created.Location!.X!.Value, created.Location.Y!.Value, created.Location.Z!.Value));
        Assert.Equal(2, created.DomainId);
        Assert.Equal("Keep", created.DomainName);
        Assert.Equal(("Keep", "Structure"), (created.Domain!.Name, created.Domain.DomainType));
        Assert.Equal("None", created.ExtentKind);
        Assert.Null(created.RegionId);
        Assert.Null(created.Radius);
        Assert.Equal(1.0, created.Preference);
        Assert.True(created.NavigationEnabled);
        Assert.Equal("General", created.Kind);
    }

    [Fact]
    public async Task Create_WithAnEmbeddedLocation_CreatesThePin()
    {
        var created = await _service.CreateAsync(new LandmarkCreateDto
        {
            Name = "Well",
            Location = new LocationDto { X = 100, Y = 65, Z = -20, World = "world" },
        });

        var pin = await _context.Locations.AsNoTracking().SingleAsync(l => l.Id == created.LocationId);
        Assert.Equal(("Well", "world", 100d), (pin.Name, pin.World, pin.X));
    }

    [Fact]
    public async Task Create_WithoutAPin_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new LandmarkCreateDto { Name = "Nowhere" }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(At(999)));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new LandmarkCreateDto
        {
            Name = "No world",
            Location = new LocationDto { X = 1, Y = 2, Z = 3 },
        }));
        Assert.Empty(_context.Landmarks);
        Assert.Equal(3, _context.Locations.Count());
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task Create_WithoutAName_IsRefused(string name)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(At(10, name)));
    }

    [Fact]
    public async Task Create_NameLongerThan100_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(At(10, new string('a', 101))));
        Assert.NotNull(await _service.CreateAsync(At(10, new string('a', 100))));
    }

    [Fact]
    public async Task Name_IsUniquePerWorld_IgnoringCase()
    {
        await _service.CreateAsync(At(10));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(At(11, "THRONE ROOM")));
        Assert.Contains("already exists in world world", ex.Message);

        // The same name in another world is fine.
        var nether = await _service.CreateAsync(At(12));
        Assert.Equal("world_nether", nether.World);
    }

    [Fact]
    public async Task Update_KeepsItsOwnName_ButCantTakeAnother()
    {
        var hall = await _service.CreateAsync(At(10, "Hall"));
        var gate = await _service.CreateAsync(At(11, "Gate"));

        var renamed = await _service.UpdateAsync(hall.Id, new LandmarkUpdateDto { Name = "hall" });
        Assert.Equal("hall", renamed.Name);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(gate.Id, new LandmarkUpdateDto { Name = "Hall" }));
    }

    [Fact]
    public async Task Update_MovingThePinToAnotherWorld_ChecksTheNameThere()
    {
        await _service.CreateAsync(At(12, "Hall"));
        var hall = await _service.CreateAsync(At(10, "Hall"));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(hall.Id, new LandmarkUpdateDto { Name = "Hall", LocationId = 12 }));
    }

    [Fact]
    public async Task Domain_MustExist_AndUpdateCanUnlinkIt()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new LandmarkCreateDto { Name = "x", LocationId = 10, DomainId = 77 }));

        var linked = await _service.CreateAsync(new LandmarkCreateDto { Name = "Hall", LocationId = 10, DomainId = 1 });
        Assert.Equal("Cinix", linked.DomainName);

        var relinked = await _service.UpdateAsync(linked.Id, new LandmarkUpdateDto { Name = "Hall", DomainId = 2 });
        Assert.Equal(("Keep", 2), (relinked.DomainName, relinked.DomainId));

        var unlinked = await _service.UpdateAsync(linked.Id, new LandmarkUpdateDto { Name = "Hall", DomainId = null });
        Assert.Null(unlinked.DomainId);
        Assert.Null(unlinked.DomainName);
        Assert.Null(unlinked.Domain);
    }

    [Fact]
    public async Task Extent_Region_NeedsARegionId_AndDropsTheRadius()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new LandmarkCreateDto { Name = "x", LocationId = 10, ExtentKind = "Region" }));

        var created = await _service.CreateAsync(new LandmarkCreateDto
        {
            Name = "Great hall", LocationId = 10, ExtentKind = "region", RegionId = " great_hall ", Radius = 8,
        });
        Assert.Equal(("Region", "great_hall", (double?)null), (created.ExtentKind, created.RegionId, created.Radius));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(512.5)]
    [InlineData(double.NaN)]
    public async Task Extent_Radius_OutOfRange_IsRefused(double radius)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new LandmarkCreateDto
        {
            Name = "x", LocationId = 10, ExtentKind = "Radius", Radius = radius,
        }));
    }

    [Fact]
    public async Task Extent_Radius_DropsTheRegion_AndNoneDropsBoth()
    {
        var created = await _service.CreateAsync(new LandmarkCreateDto
        {
            Name = "Square", LocationId = 10, ExtentKind = "Radius", Radius = 12.5, RegionId = "square",
        });
        Assert.Equal(("Radius", (string?)null, (double?)12.5), (created.ExtentKind, created.RegionId, created.Radius));

        var kept = await _service.UpdateAsync(created.Id, new LandmarkUpdateDto { Name = "Square" });
        Assert.Equal(("Radius", 12.5), (kept.ExtentKind, kept.Radius!.Value));

        var none = await _service.UpdateAsync(created.Id, new LandmarkUpdateDto { Name = "Square", ExtentKind = "None", Radius = 3 });
        Assert.Equal(("None", (string?)null, (double?)null), (none.ExtentKind, none.RegionId, none.Radius));
    }

    [Fact]
    public async Task Extent_UnknownWord_IsRefused()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new LandmarkCreateDto { Name = "x", LocationId = 10, ExtentKind = "Sphere" }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new LandmarkCreateDto { Name = "x", LocationId = 10, ExtentKind = "5" }));
    }

    [Theory]
    [InlineData(0.24)]
    [InlineData(4.01)]
    [InlineData(double.NaN)]
    public async Task Preference_OutsideTheRange_IsRefused(double preference)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new LandmarkCreateDto { Name = "x", LocationId = 10, Preference = preference }));
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(4.0)]
    public async Task Preference_TheBounds_AreAllowed(double preference)
    {
        var created = await _service.CreateAsync(new LandmarkCreateDto { Name = "x", LocationId = 10, Preference = preference });
        Assert.Equal(preference, created.Preference);
    }

    [Fact]
    public async Task Kind_OnlyGeneral()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new LandmarkCreateDto { Name = "x", LocationId = 10, Kind = "Plaza" }));
        Assert.Equal("General", (await _service.CreateAsync(new LandmarkCreateDto { Name = "x", LocationId = 10, Kind = "general" })).Kind);
    }

    [Fact]
    public async Task Update_NullFields_KeepTheStoredValues()
    {
        var created = await _service.CreateAsync(new LandmarkCreateDto
        {
            Name = "Hall", LocationId = 10, Preference = 2.0, NavigationEnabled = false,
        });

        var updated = await _service.UpdateAsync(created.Id, new LandmarkUpdateDto { Name = "Hall" });

        Assert.Equal((10, 2.0, false), (updated.LocationId, updated.Preference, updated.NavigationEnabled));
    }

    [Fact]
    public async Task Update_AnEmbeddedLocationWithItsId_MovesThePin()
    {
        var created = await _service.CreateAsync(At(10, "Hall"));

        var moved = await _service.UpdateAsync(created.Id, new LandmarkUpdateDto
        {
            Name = "Hall",
            Location = new LocationDto { Id = 10, Name = "Hall", X = 50, Y = 64, Z = 50, World = "world" },
        });

        Assert.Equal((10, 50d), (moved.LocationId, moved.Location!.X!.Value));
    }

    [Fact]
    public async Task UpdateAndDelete_OfAMissingLandmark_AreNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpdateAsync(5, new LandmarkUpdateDto { Name = "x" }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.DeleteAsync(5));
        Assert.Null(await _service.GetByIdAsync(5));
    }

    [Fact]
    public async Task Delete_RemovesTheLandmark_AndLeavesThePinToTheRetentionCheck()
    {
        var created = await _service.CreateAsync(At(10));

        await _service.DeleteAsync(created.Id);

        Assert.Empty(_context.Landmarks);
        Assert.True(_context.Locations.Any(l => l.Id == 10));
    }

    [Fact]
    public async Task GetAll_ByWorld_IgnoresCase_AndSortsByName()
    {
        await _service.CreateAsync(At(10, "b hall"));
        await _service.CreateAsync(At(11, "a gate"));
        await _service.CreateAsync(At(12, "nether hall"));

        Assert.Equal(new[] { "a gate", "b hall" }, (await _service.GetAllAsync("WORLD")).Select(l => l.Name));
        Assert.Equal(3, (await _service.GetAllAsync()).Count);
        Assert.Empty(await _service.GetAllAsync("world_the_end"));
    }

    [Fact]
    public async Task Search_FiltersByDomainWorldAndNavigation_AndMatchesTheDomainName()
    {
        await _service.CreateAsync(new LandmarkCreateDto { Name = "Throne room", LocationId = 10, DomainId = 2 });
        await _service.CreateAsync(new LandmarkCreateDto { Name = "Gatehouse", LocationId = 11, DomainId = 1, NavigationEnabled = false });
        await _service.CreateAsync(new LandmarkCreateDto { Name = "Portal", LocationId = 12 });

        async Task<string[]> Names(PagedQueryDto q) => (await _service.SearchAsync(q)).Items.Select(i => i.Name).ToArray();

        Assert.Equal(new[] { "Throne room" }, await Names(new PagedQueryDto { Filters = new() { ["domainId"] = "2" } }));
        Assert.Equal(new[] { "Portal" }, await Names(new PagedQueryDto { Filters = new() { ["World"] = "world_nether" } }));
        Assert.Equal(new[] { "Gatehouse" }, await Names(new PagedQueryDto { Filters = new() { ["navigationEnabled"] = "false" } }));
        Assert.Equal(new[] { "Throne room" }, await Names(new PagedQueryDto { SearchTerm = "keep" }));
        Assert.Equal(new[] { "Gatehouse", "Portal", "Throne room" }, await Names(new PagedQueryDto { SortBy = "name", PageSize = 10 }));

        var row = (await _service.SearchAsync(new PagedQueryDto { Filters = new() { ["domainId"] = "2" } })).Items.Single();
        Assert.Equal(("world", 1d, "Keep", "Structure"), (row.World, row.X!.Value, row.DomainName, row.DomainType));
    }

    [Fact]
    public async Task DeletingTheDomain_ClearsTheLink()
    {
        // The FK is SET NULL in the database (AddLandmarksTests); the InMemory provider applies the same rule on a tracked delete.
        var created = await _service.CreateAsync(new LandmarkCreateDto { Name = "Hall", LocationId = 10, DomainId = 1 });
        var town = await _context.Towns.Include(t => t.Location).SingleAsync(t => t.Id == 1);
        await _context.Entry(town).Collection(t => t.Districts).LoadAsync();
        await _context.Landmarks.LoadAsync();
        _context.Towns.Remove(town);
        await _context.SaveChangesAsync();

        Assert.Null((await _service.GetByIdAsync(created.Id))!.DomainId);
    }
}
