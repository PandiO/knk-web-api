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
/// Landmarks as a Domain subtype (landmark-domain DESIGN § 5, PandiO/knk-workspace#983; first off-road #708): the rules of a
/// landmark against the real repository and Location service on EF InMemory. A landmark is saved as a <see cref="Domain"/>
/// of type Landmark (A01), with no region, its own region or the parent's area (A03), a parent that is never itself or a
/// descendant (LD23), its own pin (LD17), a name unique per world (LD18), the domain defaults (LD24) and the #708 field
/// names kept for knk-plugin until L3.
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

        _context.Locations.Add(new Location { Id = 10, Name = "Hall", X = 1, Y = 64, Z = 2, World = "world" });
        _context.Locations.Add(new Location { Id = 11, Name = "Gate", X = 5, Y = 70, Z = 6, World = "world" });
        _context.Locations.Add(new Location { Id = 12, Name = "Nether hall", X = 1, Y = 64, Z = 2, World = "world_nether" });
        _context.Locations.Add(new Location { Id = 13, Name = "Cinix spawn", X = 9, Y = 66, Z = 9, Yaw = 90, World = "world" });
        _context.Towns.Add(new Town { Id = 1, Name = "Cinix", Description = "t", WgRegionId = "domain_1", WorldName = "world", LocationId = 13 });
        _context.Towns.Add(new Town { Id = 4, Name = "Rivia", Description = "t", WgRegionId = "domain_4", WorldName = "world" });
        _context.Districts.Add(new District { Id = 3, Name = "Old town", Description = "d", WgRegionId = "domain_3", TownId = 4, WorldName = "world" });
        _context.Structures.Add(new Structure { Id = 2, Name = "Keep", Description = "s", WgRegionId = "domain_2", DistrictId = 3, StreetId = 4, WorldName = "world" });
        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    private static LandmarkCreateDto At(int locationId, string name = "Throne room") => new() { Name = name, LocationId = locationId };

    // ===== A01: a Domain of type Landmark, a point without a region =====

    [Fact]
    public async Task Create_APoint_IsADomainOfTypeLandmark_WithoutARegion_AndTheDomainDefaults()
    {
        var created = await _service.CreateAsync(new LandmarkCreateDto { Name = "  Throne room ", LocationId = 10 });

        Assert.Equal(("Throne room", "Landmark", "None"), (created.Name, created.DomainType, created.AreaKind));
        Assert.Null(created.WgRegionId);
        Assert.Empty(created.SubRegionIds);
        Assert.Null(created.ParentDomainId);
        Assert.Equal((true, true, false, false), (created.AllowEntry, created.AllowExit, created.ShowEnterLeaveMessages, created.TeleportEnabled));
        Assert.Equal((1.0, true, "General"), (created.Preference, created.NavigationEnabled, created.Kind));
        Assert.Equal(("world", "world", 10), (created.World, created.WorldName, created.LocationId));

        var domain = await _context.Domains.AsNoTracking().SingleAsync(d => d.Id == created.Id);
        Assert.IsType<Landmark>(domain);
        Assert.Null(domain.WgRegionId);
        Assert.Equal(("world", ""), (domain.WorldName, domain.Description));
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
    }

    // ===== LD17: a Location belongs to one domain =====

    [Fact]
    public async Task Create_OnATownsSpawn_CopiesTheLocation_AndLeavesTheTownsAlone()
    {
        var created = await _service.CreateAsync(At(13, "Fountain"));

        Assert.NotEqual(13, created.LocationId);
        var copy = await _context.Locations.AsNoTracking().SingleAsync(l => l.Id == created.LocationId);
        Assert.Equal(("Cinix spawn", "world", 9d, 66d, 9d, 90f), (copy.Name, copy.World, copy.X, copy.Y, copy.Z, copy.Yaw));
        Assert.Equal(13, (await _context.Towns.AsNoTracking().SingleAsync(t => t.Id == 1)).LocationId);
    }

    [Fact]
    public async Task Update_KeepingItsOwnPin_DoesNotCopyIt()
    {
        var created = await _service.CreateAsync(At(10));
        var updated = await _service.UpdateAsync(created.Id, new LandmarkUpdateDto { Name = "Throne room", LocationId = 10 });

        Assert.Equal(10, updated.LocationId);
    }

    // ===== LD18: the name =====

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
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(At(10, new string('a', 101))));
        Assert.Contains("100", ex.Message);
        Assert.NotNull(await _service.CreateAsync(At(10, new string('a', 100))));
    }

    [Fact]
    public async Task Name_IsUniquePerWorld_IgnoringCase()
    {
        await _service.CreateAsync(At(10, "Throne room"));

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(At(11, "THRONE ROOM")));
        Assert.Contains("already exists", ex.Message);
        Assert.NotNull(await _service.CreateAsync(At(12, "Throne room")));
    }

    [Fact]
    public async Task Name_OfATown_DoesNotBlockALandmark()
    {
        Assert.Equal("Cinix", (await _service.CreateAsync(At(10, "Cinix"))).Name);
    }

    [Fact]
    public async Task Update_KeepsItsOwnName_ButCantTakeAnother()
    {
        var first = await _service.CreateAsync(At(10, "Hall"));
        await _service.CreateAsync(At(11, "Gate"));

        Assert.Equal("Hall", (await _service.UpdateAsync(first.Id, new LandmarkUpdateDto { Name = "hall" })).Name, ignoreCase: true);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(first.Id, new LandmarkUpdateDto { Name = "Gate" }));
    }

    [Fact]
    public async Task Update_MovingThePinToAnotherWorld_ChecksTheNameThere_AndMovesTheDomainsWorld()
    {
        var hall = await _service.CreateAsync(At(10, "Hall"));
        await _service.CreateAsync(At(12, "Hall"));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(hall.Id, new LandmarkUpdateDto { Name = "Hall", LocationId = 12 }));
        var moved = await _service.UpdateAsync(hall.Id, new LandmarkUpdateDto { Name = "Lower hall", LocationId = 12 });
        Assert.Equal("world_nether", moved.WorldName);
    }

    // ===== LD09, LD23: the parent =====

    [Fact]
    public async Task Parent_MustExist_AndUpdateCanUnlinkIt()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new LandmarkCreateDto { Name = "Hall", LocationId = 10, ParentDomainId = 99 }));

        var created = await _service.CreateAsync(new LandmarkCreateDto { Name = "Hall", LocationId = 10, ParentDomainId = 2 });
        Assert.Equal((2, "Keep", "Structure"), (created.ParentDomainId!.Value, created.ParentDomainName, created.ParentDomain!.DomainType));

        var unlinked = await _service.UpdateAsync(created.Id, new LandmarkUpdateDto { Name = "Hall", ParentDomainId = null });
        Assert.Null(unlinked.ParentDomainId);
    }

    [Fact]
    public async Task Parent_CanBeALandmark_ButNeverItselfOrADescendant()
    {
        var palace = await _service.CreateAsync(At(10, "Palace"));
        var throne = await _service.CreateAsync(new LandmarkCreateDto { Name = "Throne room", LocationId = 11, ParentDomainId = palace.Id });
        var dais = await _service.CreateAsync(new LandmarkCreateDto { Name = "Dais", LocationId = 12, ParentDomainId = throne.Id });
        Assert.Equal(("Palace", "Landmark"), (throne.ParentDomainName, throne.ParentDomain!.DomainType));

        var self = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateAsync(palace.Id, new LandmarkUpdateDto { Name = "Palace", ParentDomainId = palace.Id }));
        Assert.Contains("own parent", self.Message);
        var cycle = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.UpdateAsync(palace.Id, new LandmarkUpdateDto { Name = "Palace", ParentDomainId = dais.Id }));
        Assert.Contains("own ancestor", cycle.Message);
        Assert.Null((await _service.GetByIdAsync(palace.Id))!.ParentDomainId);
    }

    [Fact]
    public async Task DeletingTheParent_MakesTheChildrenTopLevel_AndALinkedAreaAPoint()
    {
        var palace = await _service.CreateAsync(new LandmarkCreateDto { Name = "Palace", LocationId = 10, AreaKind = "OwnRegion", WgRegionId = "palace" });
        var throne = await _service.CreateAsync(new LandmarkCreateDto
        {
            Name = "Throne room", LocationId = 11, ParentDomainId = palace.Id, AreaKind = "LinkedDomain"
        });

        await _service.DeleteAsync(palace.Id);

        var orphan = (await _service.GetByIdAsync(throne.Id))!;
        Assert.Equal((null, "None"), (orphan.ParentDomainId, orphan.AreaKind));
        Assert.False(await _context.Domains.AnyAsync(d => d.Id == palace.Id));
    }

    // ===== A03 and LD16: the area kinds =====

    [Fact]
    public async Task OwnRegion_NeedsARegion_AndOwnsIt()
    {
        var none = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(new LandmarkCreateDto { Name = "Hall", LocationId = 10, AreaKind = "OwnRegion" }));
        Assert.Contains("region", none.Message);

        var created = await _service.CreateAsync(new LandmarkCreateDto { Name = "Hall", LocationId = 10, AreaKind = "ownregion", WgRegionId = " keep_hall " });
        Assert.Equal(("OwnRegion", "keep_hall"), (created.AreaKind, created.WgRegionId));
        Assert.Equal("keep_hall", (await _context.Domains.AsNoTracking().SingleAsync(d => d.Id == created.Id)).WgRegionId);
    }

    [Fact]
    public async Task OwnRegion_ARegionAnotherDomainHolds_IsRefused_InThatWorld()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(new LandmarkCreateDto { Name = "Hall", LocationId = 10, AreaKind = "OwnRegion", WgRegionId = "DOMAIN_1" }));
        Assert.Contains("Cinix", ex.Message);

        // Region ids are unique per world: the same id in the Nether is free.
        Assert.Equal("domain_1", (await _service.CreateAsync(new LandmarkCreateDto
        {
            Name = "Nether hall", LocationId = 12, AreaKind = "OwnRegion", WgRegionId = "domain_1"
        })).WgRegionId);
    }

    [Fact]
    public async Task LinkedDomain_SavesOnlyWithAParentThatHasARegion_AndOwnsNoRegion()
    {
        var noParent = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(new LandmarkCreateDto { Name = "Square", LocationId = 10, AreaKind = "LinkedDomain" }));
        Assert.Contains("parent with a region", noParent.Message);

        var point = await _service.CreateAsync(At(11, "Well"));
        var pointParent = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(new LandmarkCreateDto { Name = "Square", LocationId = 10, AreaKind = "LinkedDomain", ParentDomainId = point.Id }));
        Assert.Contains("parent with a region", pointParent.Message);
        Assert.Equal(1, await _context.Landmarks.CountAsync());

        var linked = await _service.CreateAsync(new LandmarkCreateDto
        {
            Name = "Square", LocationId = 10, AreaKind = "LinkedDomain", ParentDomainId = 1, WgRegionId = "ignored"
        });
        Assert.Equal(("LinkedDomain", 1), (linked.AreaKind, linked.ParentDomainId!.Value));
        Assert.Null(linked.WgRegionId);
        Assert.Null((await _context.Domains.AsNoTracking().SingleAsync(d => d.Id == linked.Id)).WgRegionId);
    }

    [Fact]
    public async Task UnknownAreaKind_IsRefused()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(new LandmarkCreateDto { Name = "Hall", LocationId = 10, AreaKind = "Cylinder" }));
        Assert.Contains("None, OwnRegion or LinkedDomain", ex.Message);
    }

    [Fact]
    public async Task LeavingOwnRegion_ClearsTheRegion_AndDropsTheSubRegionRows()
    {
        var created = await _service.CreateAsync(new LandmarkCreateDto { Name = "Hall", LocationId = 10, AreaKind = "OwnRegion", WgRegionId = $"hall" });
        _context.DomainSubRegions.Add(new DomainSubRegion { DomainId = created.Id, Name = "Tower", WgRegionId = $"domain_{created.Id}_sub_1" });
        await _context.SaveChangesAsync();
        Assert.Equal(new[] { $"domain_{created.Id}_sub_1" }, (await _service.GetByIdAsync(created.Id))!.SubRegionIds);

        var point = await _service.UpdateAsync(created.Id, new LandmarkUpdateDto { Name = "Hall", AreaKind = "None" });

        Assert.Equal("None", point.AreaKind);
        Assert.Null(point.WgRegionId);
        Assert.False(await _context.DomainSubRegions.AnyAsync(s => s.DomainId == created.Id));
    }

    // ===== Rules kept from #708 =====

    [Theory]
    [InlineData(0.24)]
    [InlineData(4.01)]
    [InlineData(double.NaN)]
    public async Task Preference_OutsideTheRange_IsRefused(double preference)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new LandmarkCreateDto { Name = "Hall", LocationId = 10, Preference = preference }));
    }

    [Theory]
    [InlineData(0.25)]
    [InlineData(4.0)]
    public async Task Preference_TheBounds_AreAllowed(double preference)
    {
        Assert.Equal(preference, (await _service.CreateAsync(new LandmarkCreateDto { Name = "Hall", LocationId = 10, Preference = preference })).Preference);
    }

    [Fact]
    public async Task Kind_OnlyGeneral()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new LandmarkCreateDto { Name = "Hall", LocationId = 10, Kind = "Plaza" }));
        Assert.Equal("General", (await _service.CreateAsync(new LandmarkCreateDto { Name = "Hall", LocationId = 10, Kind = "general" })).Kind);
    }

    [Fact]
    public async Task Update_NullFields_KeepTheStoredValues()
    {
        var created = await _service.CreateAsync(new LandmarkCreateDto
        {
            Name = "Hall", Description = "The great hall", LocationId = 10, ParentDomainId = 1, AreaKind = "OwnRegion", WgRegionId = "hall",
            Preference = 0.5, NavigationEnabled = false, AllowEntry = false, ShowEnterLeaveMessages = true,
        });

        var updated = await _service.UpdateAsync(created.Id, new LandmarkUpdateDto { Name = "Hall", ParentDomainId = 1 });

        Assert.Equal(("OwnRegion", "hall", 0.5, false), (updated.AreaKind, updated.WgRegionId, updated.Preference, updated.NavigationEnabled));
        Assert.Equal((false, true, "The great hall", 10), (updated.AllowEntry, updated.ShowEnterLeaveMessages, updated.Description, updated.LocationId));
    }

    [Fact]
    public async Task Update_AnEmbeddedLocationWithItsId_MovesThePin()
    {
        var created = await _service.CreateAsync(At(10));
        await _service.UpdateAsync(created.Id, new LandmarkUpdateDto
        {
            Name = "Throne room",
            Location = new LocationDto { Id = 10, X = 50, Y = 70, Z = 60, World = "world" },
        });

        Assert.Equal(50d, (await _context.Locations.AsNoTracking().SingleAsync(l => l.Id == 10)).X);
    }

    [Fact]
    public async Task UpdateAndDelete_OfAMissingLandmark_AreNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.UpdateAsync(99, new LandmarkUpdateDto { Name = "x" }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.DeleteAsync(99));
        // A town is not a landmark.
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.DeleteAsync(1));
    }

    [Fact]
    public async Task Delete_RemovesTheLandmark_AndLeavesThePinToTheRetentionCheck()
    {
        var created = await _service.CreateAsync(At(10));
        await _service.DeleteAsync(created.Id);

        Assert.Null(await _service.GetByIdAsync(created.Id));
        Assert.True(await _context.Locations.AnyAsync(l => l.Id == 10));
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
    public async Task Search_FiltersByParentWorldNavigationAndArea_AndMatchesTheParentsName()
    {
        await _service.CreateAsync(new LandmarkCreateDto { Name = "Throne room", LocationId = 10, ParentDomainId = 2 });
        await _service.CreateAsync(new LandmarkCreateDto { Name = "Gatehouse", LocationId = 11, ParentDomainId = 1, NavigationEnabled = false, AreaKind = "LinkedDomain" });
        await _service.CreateAsync(new LandmarkCreateDto { Name = "Portal", LocationId = 12 });

        async Task<string[]> Names(PagedQueryDto q) => (await _service.SearchAsync(q)).Items.Select(i => i.Name).ToArray();

        Assert.Equal(new[] { "Throne room" }, await Names(new PagedQueryDto { Filters = new() { ["parentDomainId"] = "2" } }));
        // #708's filter name, used by the web app's domain panel until L4.
        Assert.Equal(new[] { "Throne room" }, await Names(new PagedQueryDto { Filters = new() { ["domainId"] = "2" } }));
        Assert.Equal(new[] { "Portal" }, await Names(new PagedQueryDto { Filters = new() { ["World"] = "world_nether" } }));
        Assert.Equal(new[] { "Gatehouse" }, await Names(new PagedQueryDto { Filters = new() { ["navigationEnabled"] = "false" } }));
        Assert.Equal(new[] { "Gatehouse" }, await Names(new PagedQueryDto { Filters = new() { ["areaKind"] = "linkeddomain" } }));
        Assert.Equal(new[] { "Throne room" }, await Names(new PagedQueryDto { SearchTerm = "keep" }));
        Assert.Equal(new[] { "Gatehouse", "Portal", "Throne room" }, await Names(new PagedQueryDto { SortBy = "name", PageSize = 10 }));

        var row = (await _service.SearchAsync(new PagedQueryDto { Filters = new() { ["parentDomainId"] = "2" } })).Items.Single();
        Assert.Equal(("world", 1d, "Keep", "Structure"), (row.World, row.X!.Value, row.ParentDomainName, row.ParentDomainType));
        Assert.Equal((2, "Keep", "Structure", "None"), (row.DomainId!.Value, row.DomainName, row.DomainType, row.ExtentKind));
    }

    [Fact]
    public async Task DeletingAParentTown_ClearsTheLink()
    {
        // The FK is SET NULL in the database (LandmarkAsDomainTests); the InMemory provider applies the same rule on a tracked delete.
        var created = await _service.CreateAsync(new LandmarkCreateDto { Name = "Hall", LocationId = 10, ParentDomainId = 1 });
        var town = await _context.Towns.Include(t => t.Location).SingleAsync(t => t.Id == 1);
        await _context.Entry(town).Collection(t => t.Districts).LoadAsync();
        await _context.Landmarks.LoadAsync();
        _context.Towns.Remove(town);
        await _context.SaveChangesAsync();

        Assert.Null((await _service.GetByIdAsync(created.Id))!.ParentDomainId);
    }

    // ===== LD19: the domain dashboard's edit =====

    [Fact]
    public async Task UpdateFromDomain_SavesTheDomainFields_ThroughTheLandmarkRules()
    {
        var created = await _service.CreateAsync(new LandmarkCreateDto { Name = "Hall", LocationId = 10, ParentDomainId = 1, Preference = 2 });
        await _service.CreateAsync(At(11, "Gate"));

        var renamed = await _service.UpdateFromDomainAsync(created.Id, new Domain
        {
            Name = "Great hall", Description = "d", AllowEntry = false, AllowExit = true, WgRegionId = "great_hall"
        });
        Assert.Equal(("Great hall", "d", false, "OwnRegion", "great_hall"), (renamed.Name, renamed.Description, renamed.AllowEntry, renamed.AreaKind, renamed.WgRegionId));
        Assert.Equal((1, 2.0, 10), (renamed.ParentDomainId!.Value, renamed.Preference, renamed.LocationId));

        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateFromDomainAsync(created.Id, new Domain { Name = new string('x', 101), Description = "" }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateFromDomainAsync(created.Id, new Domain { Name = "gate", Description = "" }));
        Assert.Equal("Great hall", (await _service.GetByIdAsync(created.Id))!.Name);
    }

    [Fact]
    public async Task UpdateFromDomain_WithoutARegion_KeepsALinkedArea_AndMakesAnOwnRegionAPoint()
    {
        var linked = await _service.CreateAsync(new LandmarkCreateDto { Name = "Square", LocationId = 10, ParentDomainId = 1, AreaKind = "LinkedDomain" });
        var own = await _service.CreateAsync(new LandmarkCreateDto { Name = "Hall", LocationId = 11, AreaKind = "OwnRegion", WgRegionId = "hall" });

        Assert.Equal("LinkedDomain", (await _service.UpdateFromDomainAsync(linked.Id, new Domain { Name = "Square", Description = "" })).AreaKind);
        Assert.Equal("None", (await _service.UpdateFromDomainAsync(own.Id, new Domain { Name = "Hall", Description = "" })).AreaKind);
    }

    // ===== Compatibility with knk-plugin before L3: the #708 field names =====

    [Fact]
    public async Task Read_KeepsThe708Names_AsAProjectionOfTheArea()
    {
        var point = await _service.CreateAsync(new LandmarkCreateDto { Name = "Well", LocationId = 10, ParentDomainId = 2 });
        var own = await _service.CreateAsync(new LandmarkCreateDto { Name = "Hall", LocationId = 11, AreaKind = "OwnRegion", WgRegionId = "hall" });
        var linked = await _service.CreateAsync(new LandmarkCreateDto { Name = "Square", LocationId = 12, ParentDomainId = 1, AreaKind = "LinkedDomain" });

        Assert.Equal((2, "Keep", "Structure", "None", (string?)null, (double?)null),
            (point.DomainId!.Value, point.DomainName, point.Domain!.DomainType, point.ExtentKind, point.RegionId, point.Radius));
        Assert.Equal(("Region", "hall"), (own.ExtentKind, own.RegionId));
        // LinkedDomain: the parent's main region, so the plugin's arrival check uses the parent's area (LD07).
        Assert.Equal(("Region", "domain_1"), (linked.ExtentKind, linked.RegionId));
    }

    [Fact]
    public async Task Write_ReadsThe708Names_WhenTheNewOnesAreNotSent()
    {
        var created = await _service.CreateAsync(new LandmarkCreateDto { Name = "Hall", LocationId = 10, DomainId = 2, ExtentKind = "Region", RegionId = "hall" });
        Assert.Equal((2, "OwnRegion", "hall"), (created.ParentDomainId!.Value, created.AreaKind, created.WgRegionId));

        var point = await _service.UpdateAsync(created.Id, new LandmarkUpdateDto { Name = "Hall", DomainId = 2, ExtentKind = "None" });
        Assert.Equal(("None", (string?)null), (point.AreaKind, point.WgRegionId));

        // The new names win over the old ones.
        var both = await _service.UpdateAsync(created.Id, new LandmarkUpdateDto { Name = "Hall", ParentDomainId = 1, DomainId = 2 });
        Assert.Equal(1, both.ParentDomainId!.Value);
    }

    [Fact]
    public async Task Write_ThePluginsReadModifyWrite_OfALinkedLandmark_KeepsTheLinkedArea()
    {
        var linked = await _service.CreateAsync(new LandmarkCreateDto { Name = "Square", LocationId = 10, ParentDomainId = 1, AreaKind = "LinkedDomain" });

        // /knk landmark move: the plugin sends back what it read (extentKind Region, regionId = the parent's region).
        var moved = await _service.UpdateAsync(linked.Id, new LandmarkUpdateDto
        {
            Name = "Square", LocationId = 11, DomainId = linked.DomainId, ExtentKind = linked.ExtentKind, RegionId = linked.RegionId,
        });

        Assert.Equal(("LinkedDomain", (string?)null, 11), (moved.AreaKind, moved.WgRegionId, moved.LocationId));
    }

    [Fact]
    public async Task Write_ARadius_IsRefused()
    {
        var ex = await Assert.ThrowsAsync<ArgumentException>(() =>
            _service.CreateAsync(new LandmarkCreateDto { Name = "Well", LocationId = 10, ExtentKind = "Radius", Radius = 5 }));
        Assert.Contains("radius", ex.Message, StringComparison.OrdinalIgnoreCase);
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new LandmarkCreateDto { Name = "Well", LocationId = 10, Radius = 5 }));
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(new LandmarkCreateDto { Name = "Well", LocationId = 10, ExtentKind = "Circle" }));
        Assert.False(await _context.Landmarks.AnyAsync());
    }
}
