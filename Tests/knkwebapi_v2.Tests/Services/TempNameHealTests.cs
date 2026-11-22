using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// PandiO/knk-workspace#952: a row left on its temp-named region after a failed re-run of its region step (the old
/// final-named region still in the world) is healed by the next save or <c>finalize-temp-names</c>: on such a retry the
/// finalizer names the row's own final name as the region to replace, so the plugin redefines it in place, unless another
/// row names that region. Covered for domain sub-regions, lootbox spawn areas, production areas and siege weapon spots;
/// a domain's <c>domain_&lt;id&gt;</c> is not (a v2 region can carry it, managed-worldguard-regions.md § 8 item 9). A
/// first save never names a region to replace.
/// </summary>
public sealed class TempNameHealTests : IDisposable
{
    private const string Temp = "tempregion_worldtask_51";
    private readonly KnKDbContext _context;
    private readonly Mock<IRegionService> _regions = new();
    private readonly List<(string Kind, string Old, string New, string? Replace)> _renames = new();
    private readonly DomainRegionNameFinalizer _finalizer;

    public TempNameHealTests()
    {
        _context = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _regions.Setup(r => r.RenameLootboxAreaRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, string, string?, string?>((o, n, _, x) => _renames.Add(("lootbox", o, n, x)))
            .ReturnsAsync(true);
        _regions.Setup(r => r.RenameProductionAreaRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, string, string?, string?>((o, n, _, x) => _renames.Add(("production", o, n, x)))
            .ReturnsAsync(true);
        _regions.Setup(r => r.RenameSiegeWeaponSpotRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, string, string?, string?>((o, n, _, x) => _renames.Add(("siege", o, n, x)))
            .ReturnsAsync(true);
        _regions.Setup(r => r.RenameSubRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, string, string?, string, string?, string?>((o, n, _, _, _, x) => _renames.Add(("sub", o, n, x)))
            .ReturnsAsync(true);
        _regions.Setup(r => r.RenameRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, string, string?, string?, string?>((o, n, _, _, _) => _renames.Add(("domain", o, n, null)))
            .ReturnsAsync(true);
        _regions.Setup(r => r.RenameRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, string, string?, string?, string?, string?>((o, n, _, _, _, x) => _renames.Add(("domain", o, n, x)))
            .ReturnsAsync(true);
        _finalizer = new DomainRegionNameFinalizer(_context, _regions.Object, NullLogger<DomainRegionNameFinalizer>.Instance);

        _context.Towns.Add(new Town { Id = 1, Name = "Cinix", Description = "t", WgRegionId = "domain_1", WorldName = "world" });
        _context.SiegeScenarios.Add(new SiegeScenario { Id = 100, Name = "Siege of Cinix", TownId = 1, HubLocationId = 1000 });
        _context.Locations.Add(new Location { Id = 1000, World = "hub_world" });
        _context.SaveChanges();
    }

    public void Dispose() => _context.Dispose();

    private async Task<LootboxSpawnArea> LootboxAreaAsync(int id, string region)
    {
        var area = new LootboxSpawnArea { Id = id, Name = $"a{id}", World = "world", WgRegionId = region };
        _context.LootboxSpawnAreas.Add(area);
        await _context.SaveChangesAsync();
        return area;
    }

    private async Task<ProductionArea> ProductionAreaAsync(int id, string region)
    {
        var area = new ProductionArea { Id = id, ProductionStructureId = 1, Name = $"p{id}", Shape = ProductionAreaShape.Region,
            WorldName = "world", WgRegionId = region };
        _context.ProductionAreas.Add(area);
        await _context.SaveChangesAsync();
        return area;
    }

    private async Task<SiegeWeaponSpot> SpotAsync(int id, string region)
    {
        var spot = new SiegeWeaponSpot { Id = id, SiegeScenarioId = 100, SiegeTeamId = 202, Name = $"s{id}", WgRegionId = region,
            Facing = SiegeWeaponFacing.North, SizeClass = SiegeWeaponSizeClass.Small };
        _context.SiegeWeaponSpots.Add(spot);
        await _context.SaveChangesAsync();
        return spot;
    }

    private async Task<DomainSubRegion> SubRegionAsync(int id, string region)
    {
        var sub = new DomainSubRegion { Id = id, DomainId = 1, WgRegionId = region };
        _context.DomainSubRegions.Add(sub);
        await _context.SaveChangesAsync();
        return sub;
    }

    private async Task NamedByATownAsync(string region)
    {
        _context.Towns.Add(new Town { Id = 2, Name = "Other", Description = "t", WgRegionId = region, WorldName = "world" });
        await _context.SaveChangesAsync();
    }

    // ===== lootbox spawn areas =====

    [Fact]
    public async Task LootboxArea_ASaveThatKeepsTheTempName_ReplacesItsOwnFinalName()
    {
        var area = await LootboxAreaAsync(5, Temp);

        Assert.True(await _finalizer.FinalizeLootboxAreaAsync(area, Temp));

        Assert.Equal(("lootbox", Temp, "lootbox_area_5", (string?)"lootbox_area_5"), Assert.Single(_renames));
        Assert.Equal("lootbox_area_5", (await _context.LootboxSpawnAreas.AsNoTracking().SingleAsync(a => a.Id == 5)).WgRegionId);
    }

    [Fact]
    public async Task LootboxArea_TheFirstSave_NamesNoRegionToReplace()
    {
        var area = await LootboxAreaAsync(5, Temp);

        Assert.True(await _finalizer.FinalizeLootboxAreaAsync(area, null));

        Assert.Equal(("lootbox", Temp, "lootbox_area_5", (string?)null), Assert.Single(_renames));
    }

    [Fact]
    public async Task LootboxArea_ARealRerun_StillReplacesThePreviousRegion()
    {
        var area = await LootboxAreaAsync(5, Temp);

        Assert.True(await _finalizer.FinalizeLootboxAreaAsync(area, "tempregion_worldtask_50"));

        Assert.Equal(("lootbox", Temp, "lootbox_area_5", (string?)"tempregion_worldtask_50"), Assert.Single(_renames));
    }

    [Fact]
    public async Task LootboxArea_FinalNameNamedByADomain_IsNotReplaced()
    {
        await NamedByATownAsync("lootbox_area_5");
        var area = await LootboxAreaAsync(5, Temp);

        await _finalizer.FinalizeLootboxAreaAsync(area, Temp);

        Assert.Null(Assert.Single(_renames).Replace);
    }

    [Fact]
    public async Task LootboxArea_FinalNameAProductionAreaStandsOn_IsNotReplaced()
    {
        await ProductionAreaAsync(9, "lootbox_area_5");
        var area = await LootboxAreaAsync(5, Temp);

        await _finalizer.FinalizeLootboxAreaAsync(area, Temp);

        Assert.Null(Assert.Single(_renames).Replace);
    }

    // ===== production areas =====

    [Fact]
    public async Task ProductionArea_ASaveThatKeepsTheTempName_ReplacesItsOwnFinalName()
    {
        var area = await ProductionAreaAsync(7, Temp);

        Assert.True(await _finalizer.FinalizeProductionAreaAsync(area, Temp));

        Assert.Equal(("production", Temp, "production_area_7", (string?)"production_area_7"), Assert.Single(_renames));
    }

    [Fact]
    public async Task ProductionArea_FinalNameASubRegionNames_IsNotReplaced()
    {
        await SubRegionAsync(3, "production_area_7");
        var area = await ProductionAreaAsync(7, Temp);

        await _finalizer.FinalizeProductionAreaAsync(area, Temp);

        Assert.Null(Assert.Single(_renames).Replace);
    }

    [Fact]
    public async Task ProductionArea_FinalNameAnotherProductionAreaStandsOn_IsNotReplaced()
    {
        await ProductionAreaAsync(8, "production_area_7");
        var area = await ProductionAreaAsync(7, Temp);

        await _finalizer.FinalizeProductionAreaAsync(area, Temp);

        Assert.Null(Assert.Single(_renames).Replace);
    }

    // ===== siege weapon spots =====

    [Fact]
    public async Task SiegeWeaponSpot_ASaveThatKeepsTheTempName_ReplacesItsOwnFinalName()
    {
        var spot = await SpotAsync(7, Temp);

        Assert.True(await _finalizer.FinalizeSiegeWeaponSpotAsync(spot, Temp));

        Assert.Equal(("siege", Temp, "siege_spot_7", (string?)"siege_spot_7"), Assert.Single(_renames));
    }

    [Fact]
    public async Task SiegeWeaponSpot_FinalNameALootboxAreaStandsOn_IsNotReplaced()
    {
        await LootboxAreaAsync(4, "siege_spot_7");
        var spot = await SpotAsync(7, Temp);

        await _finalizer.FinalizeSiegeWeaponSpotAsync(spot, Temp);

        Assert.Null(Assert.Single(_renames).Replace);
    }

    // ===== domain sub-regions =====

    [Fact]
    public async Task SubRegion_ASaveThatKeepsTheTempName_ReplacesItsOwnFinalName()
    {
        var sub = await SubRegionAsync(3, Temp);

        Assert.True(await _finalizer.FinalizeSubRegionAsync(sub, Temp));

        Assert.Equal(("sub", Temp, "domain_1_sub_3", (string?)"domain_1_sub_3"), Assert.Single(_renames));
    }

    [Fact]
    public async Task SubRegion_TheFirstSave_NamesNoRegionToReplace()
    {
        var sub = await SubRegionAsync(3, Temp);

        await _finalizer.FinalizeSubRegionAsync(sub, null);

        Assert.Null(Assert.Single(_renames).Replace);
    }

    [Fact]
    public async Task SubRegion_FinalNameNamedByADomain_IsNotReplaced()
    {
        await NamedByATownAsync("domain_1_sub_3");
        var sub = await SubRegionAsync(3, Temp);

        await _finalizer.FinalizeSubRegionAsync(sub, Temp);

        Assert.Null(Assert.Single(_renames).Replace);
    }

    [Fact]
    public async Task SubRegion_FinalNameALootboxAreaStandsOn_IsNotReplaced()
    {
        await LootboxAreaAsync(4, "domain_1_sub_3");
        var sub = await SubRegionAsync(3, Temp);

        await _finalizer.FinalizeSubRegionAsync(sub, Temp);

        Assert.Null(Assert.Single(_renames).Replace);
    }

    [Fact]
    public async Task SubRegion_AnAdoptedRegion_IsRenamedWithoutAReplace()
    {
        var sub = await SubRegionAsync(3, "town_1,2");

        await _finalizer.FinalizeSubRegionAsync(sub, "town_1,2");

        Assert.Equal(("sub", "town_1,2", "domain_1_sub_3", (string?)null), Assert.Single(_renames));
    }

    // ===== finalize-temp-names =====

    [Fact]
    public async Task FinalizeAll_HealsEveryOwnerKind_ButNotADomainsOwnName()
    {
        _context.Districts.Add(new District { Id = 10, Name = "Old Town", Description = "d", WgRegionId = "tempregion_worldtask_60", TownId = 1, WorldName = "world" });
        await _context.SaveChangesAsync();
        await SubRegionAsync(3, "tempregion_worldtask_61");
        await LootboxAreaAsync(5, "tempregion_worldtask_62");
        await ProductionAreaAsync(7, "tempregion_worldtask_63");
        await SpotAsync(8, "tempregion_worldtask_64");
        _context.ChangeTracker.Clear();

        var result = await _finalizer.FinalizeAllAsync();

        Assert.Equal((5, 5), (result.Found, result.Renamed));
        Assert.Equal(new (string, string, string, string?)[]
        {
            ("domain", "tempregion_worldtask_60", "domain_10", null),
            ("sub", "tempregion_worldtask_61", "domain_1_sub_3", "domain_1_sub_3"),
            ("lootbox", "tempregion_worldtask_62", "lootbox_area_5", "lootbox_area_5"),
            ("production", "tempregion_worldtask_63", "production_area_7", "production_area_7"),
            ("siege", "tempregion_worldtask_64", "siege_spot_8", "siege_spot_8"),
        }, _renames);
    }
}
