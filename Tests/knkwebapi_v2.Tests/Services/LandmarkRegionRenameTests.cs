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
/// Landmark-domain A04 (LD13, LD14, PandiO/knk-workspace#983): a landmark is a domain, so a region drawn through its form's
/// region task (tempregion_worldtask_&lt;n&gt;) is renamed on save to <c>domain_&lt;id&gt;</c> like every domain's, with the
/// domain type <c>Landmark</c> and its parent's main region as WorldGuard parent, and its sub-regions to
/// <c>domain_&lt;id&gt;_sub_&lt;n&gt;</c>. The #945 path (<c>landmark_&lt;id&gt;</c>) is gone. An existing region id is kept as
/// it is; a re-run of the region task replaces the previous <c>domain_&lt;id&gt;</c> (KNG-103).
/// </summary>
public class LandmarkRegionRenameTests : IDisposable
{
    private readonly KnKDbContext _context;
    private readonly Mock<IRegionService> _regions = new();
    private readonly List<(string Old, string New, string? Type, string? Parent, string? World, string? Replace)> _renames = new();
    private readonly List<(string Old, string New, string? Type, string SubRegionOf, string? World)> _subRenames = new();
    private readonly DomainRegionNameFinalizer _finalizer;
    private bool _pluginRenames = true;

    public LandmarkRegionRenameTests()
    {
        _context = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _regions.Setup(r => r.RenameRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, string, string?, string?, string?>((o, n, t, p, w) => _renames.Add((o, n, t, p, w, null)))
            .ReturnsAsync(() => _pluginRenames);
        _regions.Setup(r => r.RenameRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, string, string?, string?, string?, string?>((o, n, t, p, w, x) => _renames.Add((o, n, t, p, w, x)))
            .ReturnsAsync(() => _pluginRenames);
        _regions.Setup(r => r.RenameSubRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, string, string?, string, string?, string?>((o, n, t, s, w, _) => _subRenames.Add((o, n, t, s, w)))
            .ReturnsAsync(true);
        _finalizer = new DomainRegionNameFinalizer(_context, _regions.Object, NullLogger<DomainRegionNameFinalizer>.Instance);

        _context.Locations.Add(new Location { Id = 10, Name = "Hall", X = 1, Y = 64, Z = 2, World = "world" });
        _context.Locations.Add(new Location { Id = 11, Name = "Gate", X = 5, Y = 70, Z = 6, World = "world" });
        _context.Locations.Add(new Location { Id = 12, Name = "Nether hall", X = 1, Y = 64, Z = 2, World = "world_nether" });
        _context.Towns.Add(new Town { Id = 1, Name = "Cinix", Description = "t", WgRegionId = "domain_1", WorldName = "world" });
        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    private LandmarkService Service(IDomainRegionNameFinalizer? regionNames)
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddMaps(typeof(LandmarkMappingProfile).Assembly)).CreateMapper();
        var locations = new LocationRepository(_context);
        return new LandmarkService(new LandmarkRepository(_context), locations, new LocationService(locations, mapper), mapper, regionNames);
    }

    private static LandmarkCreateDto Own(string region, int locationId = 10, string name = "Throne room", int? parent = null) =>
        new() { Name = name, LocationId = locationId, AreaKind = "OwnRegion", WgRegionId = region, ParentDomainId = parent };

    private static LandmarkUpdateDto UpdateOwn(string region, int locationId = 10, string name = "Throne room") =>
        new() { Name = name, LocationId = locationId, AreaKind = "OwnRegion", WgRegionId = region };

    private async Task<string?> StoredRegionAsync(int landmarkId) =>
        await _context.Domains.AsNoTracking().Where(d => d.Id == landmarkId).Select(d => d.WgRegionId).SingleAsync();

    [Fact]
    public async Task Create_WithADrawnRegion_RenamesItToDomainId_AsALandmark_InThePinsWorld()
    {
        var created = await Service(_finalizer).CreateAsync(Own("tempregion_worldtask_41", locationId: 12));

        created.WgRegionId.Should().Be($"domain_{created.Id}");
        _renames.Should().Equal(("tempregion_worldtask_41", $"domain_{created.Id}", "Landmark", (string?)null, "world_nether", (string?)null));
        (await StoredRegionAsync(created.Id)).Should().Be($"domain_{created.Id}");
    }

    [Fact]
    public async Task Create_UnderAParent_GivesTheParentsMainRegionAsWorldGuardParent()
    {
        var created = await Service(_finalizer).CreateAsync(Own("tempregion_worldtask_42", parent: 1));

        _renames.Should().Equal(("tempregion_worldtask_42", $"domain_{created.Id}", "Landmark", "domain_1", "world", (string?)null));
    }

    [Fact]
    public async Task Create_UnderAPointLandmark_HasNoWorldGuardParent()
    {
        var palace = await Service(_finalizer).CreateAsync(new LandmarkCreateDto { Name = "Palace", LocationId = 11 });
        var created = await Service(_finalizer).CreateAsync(Own("tempregion_worldtask_43", parent: palace.Id));

        _renames.Should().Equal(("tempregion_worldtask_43", $"domain_{created.Id}", "Landmark", (string?)null, "world", (string?)null));
    }

    [Fact]
    public async Task Create_ThroughThe708Names_RenamesToo()
    {
        var created = await Service(_finalizer).CreateAsync(new LandmarkCreateDto
        {
            Name = "Well", ExtentKind = "Region", RegionId = "tempregion_worldtask_40",
            Location = new LocationDto { X = 100, Y = 65, Z = -20, World = "world_the_end" },
        });

        created.WgRegionId.Should().Be($"domain_{created.Id}");
        created.RegionId.Should().Be($"domain_{created.Id}");
        _renames.Should().Equal(("tempregion_worldtask_40", $"domain_{created.Id}", "Landmark", (string?)null, "world_the_end", (string?)null));
    }

    [Theory]
    [InlineData("keep_throne")]
    [InlineData("landmark_99")]
    [InlineData("domain_77")]
    public async Task Create_OnAnExistingRegion_KeepsItsId(string region)
    {
        var created = await Service(_finalizer).CreateAsync(Own(region));

        created.WgRegionId.Should().Be(region);
        _renames.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_AsAPointOrLinked_RenamesNothing()
    {
        await Service(_finalizer).CreateAsync(new LandmarkCreateDto { Name = "Well", LocationId = 10 });
        await Service(_finalizer).CreateAsync(new LandmarkCreateDto
        {
            Name = "Square", LocationId = 11, AreaKind = "LinkedDomain", ParentDomainId = 1, WgRegionId = "tempregion_worldtask_44"
        });

        _renames.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_WhenThePluginRefuses_StillSaves_OnTheTempName()
    {
        _pluginRenames = false;

        var created = await Service(_finalizer).CreateAsync(Own("tempregion_worldtask_45"));

        created.WgRegionId.Should().Be("tempregion_worldtask_45");
        (await StoredRegionAsync(created.Id)).Should().Be("tempregion_worldtask_45");
    }

    [Fact]
    public async Task Create_WhenThePluginIsUnreachable_StillSaves()
    {
        _regions.Setup(r => r.RenameRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ThrowsAsync(new HttpRequestException("down"));

        var created = await Service(_finalizer).CreateAsync(Own("tempregion_worldtask_46"));

        (await StoredRegionAsync(created.Id)).Should().Be("tempregion_worldtask_46");
    }

    [Fact]
    public async Task Service_WithoutAFinalizer_KeepsTheTempName()
    {
        var created = await Service(null).CreateAsync(Own("tempregion_worldtask_47"));

        created.WgRegionId.Should().Be("tempregion_worldtask_47");
    }

    [Fact]
    public async Task Update_AfterARerun_ReplacesTheLandmarksOwnDomainRegionInPlace()
    {
        var created = await Service(_finalizer).CreateAsync(Own("tempregion_worldtask_50"));
        _renames.Clear();

        var updated = await Service(_finalizer).UpdateAsync(created.Id, UpdateOwn("tempregion_worldtask_51"));

        updated.WgRegionId.Should().Be($"domain_{created.Id}");
        _renames.Should().Equal(("tempregion_worldtask_51", $"domain_{created.Id}", "Landmark", (string?)null, "world", $"domain_{created.Id}"));
    }

    [Fact]
    public async Task Update_FromAnExistingRegionToADrawnOne_KeepsTheExistingRegion()
    {
        var created = await Service(_finalizer).CreateAsync(Own("keep_throne"));

        await Service(_finalizer).UpdateAsync(created.Id, UpdateOwn("tempregion_worldtask_52"));

        _renames.Should().Equal(("tempregion_worldtask_52", $"domain_{created.Id}", "Landmark", (string?)null, "world", (string?)null));
    }

    [Fact]
    public async Task SubRegion_OfALandmark_BecomesDomainIdSubN_AsACopyOfItsMainRegion()
    {
        var created = await Service(_finalizer).CreateAsync(Own("tempregion_worldtask_53"));
        var sub = new DomainSubRegion { DomainId = created.Id, Name = "Tower", WgRegionId = "tempregion_worldtask_54" };
        _context.DomainSubRegions.Add(sub);
        await _context.SaveChangesAsync();

        (await _finalizer.FinalizeSubRegionAsync(sub, null)).Should().BeTrue();

        _subRenames.Should().Equal(("tempregion_worldtask_54", $"domain_{created.Id}_sub_{sub.Id}", "Landmark", $"domain_{created.Id}", "world"));
        (await _context.DomainSubRegions.AsNoTracking().SingleAsync(s => s.Id == sub.Id)).WgRegionId.Should().Be($"domain_{created.Id}_sub_{sub.Id}");
    }

    [Fact]
    public async Task SubRegion_OfAPointLandmark_IsNotRenamed()
    {
        var point = await Service(_finalizer).CreateAsync(new LandmarkCreateDto { Name = "Well", LocationId = 10 });
        var sub = new DomainSubRegion { DomainId = point.Id, Name = "x", WgRegionId = "tempregion_worldtask_55" };
        _context.DomainSubRegions.Add(sub);
        await _context.SaveChangesAsync();

        (await _finalizer.FinalizeSubRegionAsync(sub, null)).Should().BeFalse();
        _subRenames.Should().BeEmpty();
    }

    [Fact]
    public async Task FinalizeAll_RenamesLandmarksStillOnATempName_AfterTheirParents()
    {
        _pluginRenames = false;
        var palace = await Service(_finalizer).CreateAsync(Own("tempregion_worldtask_60", name: "Palace"));
        var throne = await Service(_finalizer).CreateAsync(Own("tempregion_worldtask_61", locationId: 11, parent: palace.Id));
        var town = new Town { Id = 5, Name = "Rivia", Description = "", WgRegionId = "tempregion_worldtask_62", WorldName = "world" };
        _context.Towns.Add(town);
        await _context.SaveChangesAsync();
        _pluginRenames = true;
        _renames.Clear();

        var result = await _finalizer.FinalizeAllAsync();

        result.Renamed.Should().Be(3);
        _renames.Select(r => r.New).Should().Equal("domain_5", $"domain_{palace.Id}", $"domain_{throne.Id}");
        _renames[2].Parent.Should().Be($"domain_{palace.Id}");
    }

    [Fact]
    public async Task ALandmarksTempRegion_IsADomainsRegion_ForTheOtherRows()
    {
        _pluginRenames = false;
        await Service(_finalizer).CreateAsync(Own("tempregion_worldtask_70"));
        var lootbox = new LootboxSpawnArea { Id = 2, Name = "a2", World = "world", WgRegionId = "tempregion_worldtask_71" };
        _context.LootboxSpawnAreas.Add(lootbox);
        await _context.SaveChangesAsync();

        // A lootbox area's re-run may not remove a temp region a landmark (a domain) still names.
        (await _finalizer.ReplaceableLootboxAreaRegionAsync(lootbox, "tempregion_worldtask_70", "lootbox_area_2", "world")).Should().BeNull();
    }
}
