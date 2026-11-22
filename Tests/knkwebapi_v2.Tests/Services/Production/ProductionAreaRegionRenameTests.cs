using FluentAssertions;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Map;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Production;

/// <summary>
/// PandiO/knk-workspace#946: a Region-shaped production area's region drawn through the area form's region task
/// (tempregion_worldtask_&lt;n&gt;) is renamed to production_area_&lt;id&gt; on save, so the plugin's 14-day temp-region
/// cleanup keeps it and the survey keeps finding it; a re-run replaces the previous region as KNG-103 does for domains,
/// #34 for lootbox spawn areas and #945 for landmarks. An existing region id is kept as it is.
/// </summary>
public class ProductionAreaRegionRenameTests : IDisposable
{
    private readonly ProductionYieldTestDb _y = new();
    private readonly Mock<IRegionService> _regions = new();
    private readonly List<(string Old, string New, string? World, string? Replace)> _renames = new();
    private readonly DomainRegionNameFinalizer _finalizer;
    private readonly ProductionAreaService _areas;
    private bool _pluginRenames = true;

    public ProductionAreaRegionRenameTests()
    {
        _regions.Setup(r => r.RenameProductionAreaRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, string, string?, string?>((o, n, w, x) => _renames.Add((o, n, w, x)))
            .ReturnsAsync(() => _pluginRenames);
        _regions.Setup(r => r.RenameRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);
        _finalizer = new DomainRegionNameFinalizer(_y.T.Db, _regions.Object, NullLogger<DomainRegionNameFinalizer>.Instance);
        _areas = new ProductionAreaService(_y.T.Db, new ProductionSettingsReader(_y.T.Db), _y.Engine, _y.Yield, _y.T.Clock, null, _finalizer);
    }

    public void Dispose() => _y.Dispose();

    private async Task<int> FarmAsync(string? yieldSource = null) => (await _y.Structures.CreateAsync(new ProductionStructureDto
    {
        Name = "Farm", Description = "", WgRegionId = "tmp_farm", TownId = ProductionWorldTestDb.Phoenix, ProductionKindId = 1,
        YieldSource = yieldSource,
    })).Id!.Value;

    private static ProductionAreaDto RegionArea(int structureId, string region, int x = 0, string name = "South field") => new()
    {
        ProductionStructureId = structureId, Name = name, Shape = "Region", WgRegionId = region,
        MinX = x, MaxX = x + 9, MinY = 60, MaxY = 62, MinZ = 0, MaxZ = 9,
    };

    private async Task<string?> StoredRegionAsync(int areaId) =>
        await _y.T.Db.ProductionAreas.AsNoTracking().Where(a => a.Id == areaId).Select(a => a.WgRegionId).SingleAsync();

    // ===== on submit =====

    [Fact]
    public async Task Create_WithADrawnRegion_RenamesItToTheAreaName_InTheAreasWorld_AndStoresIt()
    {
        int farm = await FarmAsync();

        var created = await _areas.CreateAsync(RegionArea(farm, "tempregion_worldtask_41"));

        created.WgRegionId.Should().Be($"production_area_{created.Id}");
        _renames.Should().Equal(("tempregion_worldtask_41", $"production_area_{created.Id}", created.WorldName, (string?)null));
        (await StoredRegionAsync(created.Id)).Should().Be($"production_area_{created.Id}");
    }

    [Theory]
    [InlineData("farm_south")]
    [InlineData("domain_7")]
    [InlineData("production_area_99")]
    public async Task Create_OnAnExistingRegion_KeepsItsId(string region)
    {
        int farm = await FarmAsync();

        var created = await _areas.CreateAsync(RegionArea(farm, region));

        created.WgRegionId.Should().Be(region);
        _renames.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_WhenThePluginRefuses_StillSaves_OnTheTempName()
    {
        int farm = await FarmAsync();
        _pluginRenames = false;

        var created = await _areas.CreateAsync(RegionArea(farm, "tempregion_worldtask_42"));

        created.WgRegionId.Should().Be("tempregion_worldtask_42");
        (await StoredRegionAsync(created.Id)).Should().Be("tempregion_worldtask_42");
    }

    [Fact]
    public async Task Create_WhenThePluginIsUnreachable_StillSaves()
    {
        int farm = await FarmAsync();
        _regions.Setup(r => r.RenameProductionAreaRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var created = await _areas.CreateAsync(RegionArea(farm, "tempregion_worldtask_43"));

        created.WgRegionId.Should().Be("tempregion_worldtask_43");
    }

    [Fact]
    public async Task Service_WithoutAFinalizer_BehavesAsBefore()
    {
        int farm = await FarmAsync();

        var created = await _y.Areas.CreateAsync(RegionArea(farm, "tempregion_worldtask_44"));

        created.WgRegionId.Should().Be("tempregion_worldtask_44");
        _renames.Should().BeEmpty();
    }

    // ===== on a re-run =====

    [Fact]
    public async Task Update_AfterARerun_ReplacesTheAreasOwnRegionInPlace()
    {
        int farm = await FarmAsync();
        var created = await _areas.CreateAsync(RegionArea(farm, "tempregion_worldtask_50"));
        _renames.Clear();

        var updated = await _areas.UpdateAsync(created.Id, RegionArea(farm, "tempregion_worldtask_51"));

        string final = $"production_area_{created.Id}";
        _renames.Should().Equal(("tempregion_worldtask_51", final, created.WorldName, (string?)final));
        updated.WgRegionId.Should().Be(final);
        (await StoredRegionAsync(created.Id)).Should().Be(final);
    }

    [Fact]
    public async Task Update_AfterARerun_OfAnAreaStillOnATempName_RemovesThatTempRegion()
    {
        int farm = await FarmAsync();
        _pluginRenames = false;
        var created = await _areas.CreateAsync(RegionArea(farm, "tempregion_worldtask_60"));
        _pluginRenames = true;
        _renames.Clear();

        await _areas.UpdateAsync(created.Id, RegionArea(farm, "tempregion_worldtask_61"));

        _renames.Should().Equal(("tempregion_worldtask_61", $"production_area_{created.Id}", created.WorldName,
            (string?)"tempregion_worldtask_60"));
    }

    [Fact]
    public async Task Update_ThatLeavesTheShapeAlone_OrMovesToAnotherShape_RenamesNothing()
    {
        int farm = await FarmAsync();
        var created = await _areas.CreateAsync(RegionArea(farm, "tempregion_worldtask_62"));
        _renames.Clear();

        await _areas.UpdateAsync(created.Id, new ProductionAreaDto { Name = "South meadow" });
        (await StoredRegionAsync(created.Id)).Should().Be($"production_area_{created.Id}");
        await _areas.UpdateAsync(created.Id, new ProductionAreaDto
        {
            Shape = "Cuboid", MinX = 0, MaxX = 9, MinY = 60, MaxY = 62, MinZ = 0, MaxZ = 9,
        });

        _renames.Should().BeEmpty();
        (await StoredRegionAsync(created.Id)).Should().BeNull();
    }

    [Fact]
    public async Task Rerun_KeepsARegionTheFormDidNotDraw_AndOneSomeoneElseUses()
    {
        var area = new ProductionArea { Id = 5, Name = "a5", WorldName = "world", Shape = ProductionAreaShape.Region, WgRegionId = "tempregion_worldtask_70" };
        // A landmark is a domain (PandiO/knk-workspace#983): its own region counts like any domain's.
        _y.T.Db.Landmarks.Add(new Landmark
        {
            Name = "Mill", Description = "", AreaKind = LandmarkAreaKind.OwnRegion, WgRegionId = "tempregion_worldtask_71", WorldName = "world",
            Location = new Location { Name = "Mill", X = 1, Y = 64, Z = 2, World = "world" },
        });
        _y.T.Db.LootboxSpawnAreas.Add(new LootboxSpawnArea { Id = 2, Name = "a2", World = "world", WgRegionId = "tempregion_worldtask_72" });
        await _y.T.Db.SaveChangesAsync();

        (await _finalizer.ReplaceableProductionAreaRegionAsync(area, "farm_south", "production_area_5", "world")).Should().BeNull();
        (await _finalizer.ReplaceableProductionAreaRegionAsync(area, "tempregion_worldtask_71", "production_area_5", "world")).Should().BeNull();
        (await _finalizer.ReplaceableProductionAreaRegionAsync(area, "tempregion_worldtask_72", "production_area_5", "world")).Should().BeNull();
        (await _finalizer.ReplaceableProductionAreaRegionAsync(area, "tempregion_worldtask_73", "production_area_5", "world"))
            .Should().Be("tempregion_worldtask_73");
        (await _finalizer.ReplaceableProductionAreaRegionAsync(area, "PRODUCTION_AREA_5", "production_area_5", "world"))
            .Should().Be("PRODUCTION_AREA_5");
    }

    [Fact]
    public async Task ADomainLootboxOrLandmarkRerun_KeepsAPreviousTempRegionAProductionAreaStandsOn()
    {
        int farm = await FarmAsync();
        _pluginRenames = false;
        var created = await _areas.CreateAsync(RegionArea(farm, "tempregion_worldtask_80"));
        var town = new Town { Id = 901, Name = "Cinix", Description = "t", WgRegionId = "tempregion_worldtask_81", WorldName = created.WorldName };
        _y.T.Db.Towns.Add(town);
        var lootbox = new LootboxSpawnArea { Id = 2, Name = "a2", World = created.WorldName, WgRegionId = "tempregion_worldtask_82" };
        _y.T.Db.LootboxSpawnAreas.Add(lootbox);
        await _y.T.Db.SaveChangesAsync();

        (await _finalizer.ReplaceableRegionAsync(town, "tempregion_worldtask_80")).Should().BeNull();
        (await _finalizer.ReplaceableLootboxAreaRegionAsync(lootbox, "tempregion_worldtask_80", "lootbox_area_2", created.WorldName)).Should().BeNull();
        (await _finalizer.ReplaceableRegionAsync(town, "tempregion_worldtask_83")).Should().Be("tempregion_worldtask_83");
    }

    // ===== finalize-temp-names, the survey and the bounds =====

    [Fact]
    public async Task FinalizeAll_AlsoRenamesProductionAreasStillOnATempName_AndListsRefusals()
    {
        int farm = await FarmAsync();
        _pluginRenames = false;
        var first = await _areas.CreateAsync(RegionArea(farm, "tempregion_worldtask_90"));
        var second = await _areas.CreateAsync(RegionArea(farm, "tempregion_worldtask_91", x: 100, name: "East field"));
        _pluginRenames = true;
        _renames.Clear();
        _regions.Setup(r => r.RenameProductionAreaRegionAsync("tempregion_worldtask_91", It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(false);

        var result = await _finalizer.FinalizeAllAsync();

        result.Found.Should().Be(2);
        result.Renamed.Should().Be(1);
        result.Failed.Should().Equal($"ProductionArea {second.Id} (tempregion_worldtask_91)");
        (await StoredRegionAsync(first.Id)).Should().Be($"production_area_{first.Id}");
    }

    [Fact]
    public async Task TheSurveyQueue_NamesTheRenamedRegion()
    {
        int farm = await FarmAsync(yieldSource: "Derived");

        var created = await _areas.CreateAsync(RegionArea(farm, "tempregion_worldtask_92"));

        var due = await _areas.SurveyDueAsync(10);
        due.Select(d => (d.Area.Id, d.Area.WgRegionId)).Should().Contain((created.Id, $"production_area_{created.Id}"));
    }

    [Fact]
    public async Task ALaterEdit_TakesTheBoundsFromTheRenamedRegionsOutline()
    {
        // #877 after #946: the rename reports the region's outline under its final name (overview map, with
        // map.reporting.enabled), so an edit that names production_area_<id> and leaves the bounds empty still works.
        int farm = await FarmAsync();
        var created = await _areas.CreateAsync(RegionArea(farm, "tempregion_worldtask_93"));
        string final = $"production_area_{created.Id}";
        _y.T.Db.MapRegionOutlines.Add(new MapRegionOutline
        {
            WgRegionId = final, WorldName = created.WorldName!, MinY = 58, MaxY = 66, UpdatedAt = DateTime.UtcNow,
            PointsJson = MapOutlinePoints.Serialize(new[] { (2, 3), (14, 3), (14, 11), (2, 11) }),
        });
        await _y.T.Db.SaveChangesAsync();
        _y.T.Db.ChangeTracker.Clear();

        var updated = await _areas.UpdateAsync(created.Id, new ProductionAreaDto { Shape = "Region", WgRegionId = final });

        (updated.MinX, updated.MaxX, updated.MinY, updated.MaxY, updated.MinZ, updated.MaxZ).Should().Be(((int?)2, (int?)14, (int?)58, (int?)66, (int?)3, (int?)11));
        updated.WgRegionId.Should().Be(final);
    }
}
