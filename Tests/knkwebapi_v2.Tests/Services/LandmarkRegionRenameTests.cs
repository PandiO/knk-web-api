using AutoMapper;
using FluentAssertions;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// PandiO/knk-workspace#945: a landmark's region drawn through the form's region task (tempregion_worldtask_&lt;n&gt;) is
/// renamed to landmark_&lt;id&gt; on save, so the plugin's 14-day temp-region cleanup keeps it; a re-run of the region task
/// replaces the landmark's previous region as KNG-103 does for domains and #34 for lootbox spawn areas. An existing
/// region id (typed in the form or given to /knk landmark create &lt;name&gt; region &lt;id&gt;) is kept as it is.
/// </summary>
public class LandmarkRegionRenameTests : IDisposable
{
    private readonly KnKDbContext _context;
    private readonly Mock<IRegionService> _regions = new();
    private readonly List<(string Old, string New, string? World, string? Replace)> _renames = new();
    private readonly DomainRegionNameFinalizer _finalizer;
    private bool _pluginRenames = true;

    public LandmarkRegionRenameTests()
    {
        _context = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _regions.Setup(r => r.RenameLandmarkRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, string, string?, string?>((o, n, w, x) => _renames.Add((o, n, w, x)))
            .ReturnsAsync(() => _pluginRenames);
        _regions.Setup(r => r.RenameLootboxAreaRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);
        _regions.Setup(r => r.RenameRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);
        _regions.Setup(r => r.RenameRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);
        _finalizer = new DomainRegionNameFinalizer(_context, _regions.Object, NullLogger<DomainRegionNameFinalizer>.Instance);

        _context.Locations.Add(new Location { Id = 10, Name = "Hall", X = 1, Y = 64, Z = 2, World = "world" });
        _context.Locations.Add(new Location { Id = 11, Name = "Gate", X = 5, Y = 70, Z = 6, World = "world" });
        _context.Locations.Add(new Location { Id = 12, Name = "Nether hall", X = 1, Y = 64, Z = 2, World = "world_nether" });
        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    private LandmarkService Service(IDomainRegionNameFinalizer? regionNames)
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddMaps(typeof(LandmarkMappingProfile).Assembly)).CreateMapper();
        var locations = new LocationRepository(_context);
        return new LandmarkService(new LandmarkRepository(_context), locations, new LocationService(locations, mapper), mapper, regionNames);
    }

    private static LandmarkCreateDto WithRegion(string region, int locationId = 10, string name = "Throne room") =>
        new() { Name = name, LocationId = locationId, ExtentKind = "Region", RegionId = region };

    private static LandmarkUpdateDto UpdateWithRegion(string region, int locationId = 10, string name = "Throne room") =>
        new() { Name = name, LocationId = locationId, ExtentKind = "Region", RegionId = region };

    private async Task<Landmark> AddLandmarkAsync(int id, string? region, int locationId = 10)
    {
        var landmark = new Landmark
        {
            Id = id, Name = $"Landmark {id}", LocationId = locationId,
            ExtentKind = region == null ? LandmarkExtentKind.None : LandmarkExtentKind.Region, RegionId = region
        };
        _context.Landmarks.Add(landmark);
        await _context.SaveChangesAsync();
        return landmark;
    }

    private async Task<string?> StoredRegionAsync(int landmarkId) =>
        await _context.Landmarks.AsNoTracking().Where(l => l.Id == landmarkId).Select(l => l.RegionId).SingleAsync();

    // ===== on submit =====

    [Fact]
    public async Task Create_WithADrawnRegion_RenamesItToTheLandmarkName_InThePinsWorld_AndStoresIt()
    {
        var created = await Service(_finalizer).CreateAsync(WithRegion("tempregion_worldtask_41", locationId: 12));

        created.RegionId.Should().Be($"landmark_{created.Id}");
        _renames.Should().Equal(("tempregion_worldtask_41", $"landmark_{created.Id}", "world_nether", (string?)null));
        (await StoredRegionAsync(created.Id)).Should().Be($"landmark_{created.Id}");
    }

    [Fact]
    public async Task Create_WithAnEmbeddedPin_RenamesInThatPinsWorld()
    {
        var created = await Service(_finalizer).CreateAsync(new LandmarkCreateDto
        {
            Name = "Well", ExtentKind = "Region", RegionId = "tempregion_worldtask_40",
            Location = new LocationDto { X = 100, Y = 65, Z = -20, World = "world_the_end" },
        });

        _renames.Should().Equal(("tempregion_worldtask_40", $"landmark_{created.Id}", "world_the_end", (string?)null));
    }

    [Theory]
    [InlineData("domain_7")]
    [InlineData("keep_throne")]
    [InlineData("lootbox_area_3")]
    [InlineData("landmark_99")]
    public async Task Create_OnAnExistingRegion_KeepsItsId(string region)
    {
        var created = await Service(_finalizer).CreateAsync(WithRegion(region));

        created.RegionId.Should().Be(region);
        _renames.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_WithARadiusOrNoExtent_RenamesNothing()
    {
        await Service(_finalizer).CreateAsync(new LandmarkCreateDto { Name = "Well", LocationId = 10, ExtentKind = "Radius", Radius = 5 });
        await Service(_finalizer).CreateAsync(new LandmarkCreateDto { Name = "Gate", LocationId = 11 });

        _renames.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_WhenThePluginRefuses_StillSaves_OnTheTempName()
    {
        _pluginRenames = false;

        var created = await Service(_finalizer).CreateAsync(WithRegion("tempregion_worldtask_42"));

        created.RegionId.Should().Be("tempregion_worldtask_42");
        (await StoredRegionAsync(created.Id)).Should().Be("tempregion_worldtask_42");
    }

    [Fact]
    public async Task Create_WhenThePluginIsUnreachable_StillSaves()
    {
        _regions.Setup(r => r.RenameLandmarkRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var created = await Service(_finalizer).CreateAsync(WithRegion("tempregion_worldtask_43"));

        created.RegionId.Should().Be("tempregion_worldtask_43");
    }

    [Fact]
    public async Task Service_WithoutAFinalizer_BehavesAsBefore()
    {
        var created = await Service(null).CreateAsync(WithRegion("tempregion_worldtask_44"));

        created.RegionId.Should().Be("tempregion_worldtask_44");
        _renames.Should().BeEmpty();
    }

    // ===== on a re-run =====

    [Fact]
    public async Task Update_AfterARerun_ReplacesTheLandmarksOwnRegionInPlace()
    {
        var service = Service(_finalizer);
        var created = await service.CreateAsync(WithRegion("tempregion_worldtask_50"));
        _renames.Clear();

        var updated = await service.UpdateAsync(created.Id, UpdateWithRegion("tempregion_worldtask_51"));

        string final = $"landmark_{created.Id}";
        _renames.Should().Equal(("tempregion_worldtask_51", final, "world", (string?)final));
        updated.RegionId.Should().Be(final);
        (await StoredRegionAsync(created.Id)).Should().Be(final);
    }

    [Fact]
    public async Task Update_AfterARerun_OfALandmarkStillOnATempName_RemovesThatTempRegion()
    {
        _pluginRenames = false;
        var service = Service(_finalizer);
        var created = await service.CreateAsync(WithRegion("tempregion_worldtask_60"));
        _pluginRenames = true;
        _renames.Clear();

        await service.UpdateAsync(created.Id, UpdateWithRegion("tempregion_worldtask_61"));

        _renames.Should().Equal(("tempregion_worldtask_61", $"landmark_{created.Id}", "world", (string?)"tempregion_worldtask_60"));
    }

    [Fact]
    public async Task Update_FromAnExistingRegionToADrawnOne_KeepsTheExistingRegion()
    {
        var service = Service(_finalizer);
        var created = await service.CreateAsync(WithRegion("keep_throne"));

        await service.UpdateAsync(created.Id, UpdateWithRegion("tempregion_worldtask_62"));

        _renames.Should().Equal(("tempregion_worldtask_62", $"landmark_{created.Id}", "world", (string?)null));
    }

    [Fact]
    public async Task Update_WithTheSameTempRegion_RetriesTheRename_WithoutReplacing()
    {
        _pluginRenames = false;
        var service = Service(_finalizer);
        var created = await service.CreateAsync(WithRegion("tempregion_worldtask_63"));
        _pluginRenames = true;
        _renames.Clear();

        await service.UpdateAsync(created.Id, UpdateWithRegion("tempregion_worldtask_63"));

        _renames.Should().Equal(("tempregion_worldtask_63", $"landmark_{created.Id}", "world", (string?)null));
        (await StoredRegionAsync(created.Id)).Should().Be($"landmark_{created.Id}");
    }

    [Fact]
    public async Task Update_ThatKeepsTheExtent_RenamesNothing()
    {
        var service = Service(_finalizer);
        var created = await service.CreateAsync(WithRegion("tempregion_worldtask_64"));
        _renames.Clear();

        await service.UpdateAsync(created.Id, new LandmarkUpdateDto { Name = "Throne hall", LocationId = 10, Preference = 0.5 });

        _renames.Should().BeEmpty();
        (await StoredRegionAsync(created.Id)).Should().Be($"landmark_{created.Id}");
    }

    [Fact]
    public async Task Rerun_KeepsARegionTheFormDidNotDraw()
    {
        var landmark = await AddLandmarkAsync(5, "tempregion_worldtask_70");

        (await _finalizer.ReplaceableLandmarkRegionAsync(landmark, "domain_7", "landmark_5", "world")).Should().BeNull();
        (await _finalizer.ReplaceableLandmarkRegionAsync(landmark, "keep_throne", "landmark_5", "world")).Should().BeNull();
        (await _finalizer.ReplaceableLandmarkRegionAsync(landmark, "landmark_6", "landmark_5", "world")).Should().BeNull();
        (await _finalizer.ReplaceableLandmarkRegionAsync(landmark, " ", "landmark_5", "world")).Should().BeNull();
        (await _finalizer.ReplaceableLandmarkRegionAsync(landmark, "LANDMARK_5", "landmark_5", "world")).Should().Be("LANDMARK_5");
    }

    [Fact]
    public async Task Rerun_KeepsAPreviousRegionSomeoneElseUses()
    {
        var landmark = await AddLandmarkAsync(5, "tempregion_worldtask_71");
        await AddLandmarkAsync(6, "tempregion_worldtask_72");
        _context.Towns.Add(new Town { Id = 1, Name = "Cinix", Description = "t", WgRegionId = "tempregion_worldtask_73", WorldName = "world" });
        _context.LootboxSpawnAreas.Add(new LootboxSpawnArea { Id = 2, Name = "a2", World = "world", WgRegionId = "tempregion_worldtask_74" });
        await AddLandmarkAsync(7, "landmark_5", locationId: 11);
        await _context.SaveChangesAsync();

        (await _finalizer.ReplaceableLandmarkRegionAsync(landmark, "tempregion_worldtask_72", "landmark_5", "world")).Should().BeNull();
        (await _finalizer.ReplaceableLandmarkRegionAsync(landmark, "tempregion_worldtask_73", "landmark_5", "world")).Should().BeNull();
        (await _finalizer.ReplaceableLandmarkRegionAsync(landmark, "tempregion_worldtask_74", "landmark_5", "world")).Should().BeNull();
        // Landmark 7 only points at landmark 5's region: it is redefined in place, and landmark 7 follows the new shape.
        (await _finalizer.ReplaceableLandmarkRegionAsync(landmark, "landmark_5", "landmark_5", "world")).Should().Be("landmark_5");
        // Another world's region of the same id is not this one.
        (await _finalizer.ReplaceableLandmarkRegionAsync(landmark, "tempregion_worldtask_72", "landmark_5", "world_nether")).Should().Be("tempregion_worldtask_72");
    }

    [Fact]
    public async Task ATempRegionSomeoneElseStandsOn_IsNotRenamedAway()
    {
        var landmark = await AddLandmarkAsync(5, "tempregion_worldtask_80");
        await AddLandmarkAsync(6, "tempregion_worldtask_80", locationId: 11);

        (await _finalizer.FinalizeLandmarkAsync(landmark, null)).Should().BeFalse();

        _renames.Should().BeEmpty();
        (await StoredRegionAsync(5)).Should().Be("tempregion_worldtask_80");
    }

    // ===== finalize-temp-names, domains and lootbox areas =====

    [Fact]
    public async Task FinalizeAll_AlsoRenamesLandmarksStillOnATempName()
    {
        await AddLandmarkAsync(5, "tempregion_worldtask_90", locationId: 12);
        await AddLandmarkAsync(6, "keep_throne");
        await AddLandmarkAsync(7, null);

        var result = await _finalizer.FinalizeAllAsync();

        result.Found.Should().Be(1);
        result.Renamed.Should().Be(1);
        _renames.Should().Equal(("tempregion_worldtask_90", "landmark_5", "world_nether", (string?)null));
        (await StoredRegionAsync(5)).Should().Be("landmark_5");
    }

    [Fact]
    public async Task FinalizeAll_ListsALandmarkThePluginRefused()
    {
        _pluginRenames = false;
        await AddLandmarkAsync(5, "tempregion_worldtask_91");

        var result = await _finalizer.FinalizeAllAsync();

        result.Failed.Should().Equal("Landmark 5 (tempregion_worldtask_91)");
    }

    [Fact]
    public async Task ADomainOrLootboxRerun_KeepsAPreviousTempRegionALandmarkStandsOn()
    {
        await AddLandmarkAsync(5, "tempregion_worldtask_95");
        var town = new Town { Id = 1, Name = "Cinix", Description = "t", WgRegionId = "tempregion_worldtask_96", WorldName = "world" };
        _context.Towns.Add(town);
        var area = new LootboxSpawnArea { Id = 2, Name = "a2", World = "world", WgRegionId = "tempregion_worldtask_98" };
        _context.LootboxSpawnAreas.Add(area);
        await _context.SaveChangesAsync();

        (await _finalizer.ReplaceableRegionAsync(town, "tempregion_worldtask_95")).Should().BeNull();
        (await _finalizer.ReplaceableRegionAsync(town, "tempregion_worldtask_97")).Should().Be("tempregion_worldtask_97");
        (await _finalizer.ReplaceableLootboxAreaRegionAsync(area, "tempregion_worldtask_95", "lootbox_area_2", "world")).Should().BeNull();
    }

    [Fact]
    public async Task ALandmarkOnAnotherRowsFinalName_DoesNotBlockItsRedefinition()
    {
        // Review of #945: a landmark (or production area) that names lootbox_area_2 or domain_1 as an existing region only
        // reads its shape; the owner's re-run still redefines it in place instead of leaving the owner on a temp name.
        await AddLandmarkAsync(5, "lootbox_area_2");
        await AddLandmarkAsync(6, "domain_1", locationId: 11);
        var area = new LootboxSpawnArea { Id = 2, Name = "a2", World = "world", WgRegionId = "tempregion_worldtask_100" };
        _context.LootboxSpawnAreas.Add(area);
        var town = new Town { Id = 1, Name = "Cinix", Description = "t", WgRegionId = "tempregion_worldtask_101", WorldName = "world" };
        _context.Towns.Add(town);
        await _context.SaveChangesAsync();

        (await _finalizer.ReplaceableLootboxAreaRegionAsync(area, "lootbox_area_2", "lootbox_area_2", "world")).Should().Be("lootbox_area_2");
        (await _finalizer.ReplaceableRegionAsync(town, "domain_1")).Should().Be("domain_1");
        // A lootbox area holding a landmark's final name still keeps it (it owns the region it stands on).
        _context.LootboxSpawnAreas.Add(new LootboxSpawnArea { Id = 3, Name = "a3", World = "world", WgRegionId = "landmark_7" });
        await _context.SaveChangesAsync();
        var landmark = await AddLandmarkAsync(7, "tempregion_worldtask_102");
        (await _finalizer.ReplaceableLandmarkRegionAsync(landmark, "landmark_7", "landmark_7", "world")).Should().BeNull();
    }

    [Fact]
    public async Task ALootboxAreaOnATempRegionALandmarkStandsOn_KeepsTheTempName()
    {
        await AddLandmarkAsync(5, "tempregion_worldtask_99");
        var area = new LootboxSpawnArea { Id = 2, Name = "a2", World = "world", WgRegionId = "tempregion_worldtask_99" };
        _context.LootboxSpawnAreas.Add(area);
        await _context.SaveChangesAsync();

        (await _finalizer.FinalizeLootboxAreaAsync(area, null)).Should().BeFalse();
    }
}
