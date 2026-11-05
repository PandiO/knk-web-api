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

namespace knkwebapi_v2.Tests.Services.Lootbox;

/// <summary>
/// PandiO/knk-workspace#34: a lootbox spawn area's region drawn through the form's region task
/// (tempregion_worldtask_&lt;n&gt;) is renamed to lootbox_area_&lt;id&gt; on save, so the plugin's 14-day temp-region cleanup
/// keeps it; a re-run of the region task replaces the area's previous region as KNG-103 does for domains.
/// </summary>
public class LootboxAreaRegionRenameTests : IDisposable
{
    private readonly KnKDbContext _context;
    private readonly Mock<IRegionService> _regions = new();
    private readonly List<(string Old, string New, string? World, string? Replace)> _renames = new();
    private readonly DomainRegionNameFinalizer _finalizer;
    private bool _pluginRenames = true;

    public LootboxAreaRegionRenameTests()
    {
        _context = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _regions.Setup(r => r.RenameLootboxAreaRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .Callback<string, string, string?, string?>((o, n, w, x) => _renames.Add((o, n, w, x)))
            .ReturnsAsync(() => _pluginRenames);
        _regions.Setup(r => r.RenameRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);
        _regions.Setup(r => r.RenameRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ReturnsAsync(true);
        _finalizer = new DomainRegionNameFinalizer(_context, _regions.Object, NullLogger<DomainRegionNameFinalizer>.Instance);
    }

    public void Dispose() => _context.Dispose();

    private static IMapper Mapper() => new MapperConfiguration(cfg =>
    {
        cfg.AddProfile<LootboxMappingProfile>();
        cfg.AddProfile<PagedQueryMappingProfile>();
    }).CreateMapper();

    private LootboxSpawnAreaService Service(IDomainRegionNameFinalizer? regionNames) => new(
        new LootboxSpawnAreaRepository(_context),
        Mapper(),
        new AuditLogService(new AuditLogRepository(_context), new UserRepository(_context)),
        new UserRepository(_context),
        NullLogger<LootboxSpawnAreaService>.Instance,
        null,
        regionNames);

    private static LootboxSpawnAreaDto Area(string region, string name = "market") =>
        new() { Name = name, World = "world", WgRegionId = region };

    private async Task<LootboxSpawnArea> AddAreaAsync(int id, string region, string world = "world", string? name = null)
    {
        var area = new LootboxSpawnArea { Id = id, Name = name ?? $"a{id}", World = world, WgRegionId = region };
        _context.LootboxSpawnAreas.Add(area);
        await _context.SaveChangesAsync();
        return area;
    }

    private async Task<string> StoredRegionAsync(int areaId) =>
        await _context.LootboxSpawnAreas.AsNoTracking().Where(a => a.Id == areaId).Select(a => a.WgRegionId).SingleAsync();

    // ===== on submit =====

    [Fact]
    public async Task Create_WithADrawnRegion_RenamesItToTheAreaName_AndStoresIt()
    {
        var created = await Service(_finalizer).CreateAsync(Area("tempregion_worldtask_41"));

        created.WgRegionId.Should().Be($"lootbox_area_{created.Id}");
        _renames.Should().Equal(("tempregion_worldtask_41", $"lootbox_area_{created.Id}", "world", (string?)null));
        (await StoredRegionAsync(created.Id)).Should().Be($"lootbox_area_{created.Id}");
    }

    [Theory]
    [InlineData("domain_7")]
    [InlineData("lootbox_market")]
    [InlineData("spawn_plaza")]
    public async Task Create_OnAnExistingRegion_RenamesNothing(string region)
    {
        var created = await Service(_finalizer).CreateAsync(Area(region));

        created.WgRegionId.Should().Be(region);
        _renames.Should().BeEmpty();
    }

    [Fact]
    public async Task Create_WhenThePluginRefuses_StillSaves_OnTheTempName()
    {
        _pluginRenames = false;

        var created = await Service(_finalizer).CreateAsync(Area("tempregion_worldtask_42"));

        created.WgRegionId.Should().Be("tempregion_worldtask_42");
        (await StoredRegionAsync(created.Id)).Should().Be("tempregion_worldtask_42");
    }

    [Fact]
    public async Task Create_WhenThePluginIsUnreachable_StillSaves()
    {
        _regions.Setup(r => r.RenameLootboxAreaRegionAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string?>(), It.IsAny<string?>()))
            .ThrowsAsync(new HttpRequestException("connection refused"));

        var created = await Service(_finalizer).CreateAsync(Area("tempregion_worldtask_43"));

        created.WgRegionId.Should().Be("tempregion_worldtask_43");
    }

    [Fact]
    public async Task Service_WithoutAFinalizer_BehavesAsBefore()
    {
        var created = await Service(null).CreateAsync(Area("tempregion_worldtask_44"));

        created.WgRegionId.Should().Be("tempregion_worldtask_44");
        _renames.Should().BeEmpty();
    }

    // ===== on a re-run =====

    [Fact]
    public async Task Update_AfterARerun_ReplacesTheAreasOwnRegionInPlace()
    {
        var service = Service(_finalizer);
        var created = await service.CreateAsync(Area("tempregion_worldtask_50"));
        _renames.Clear();

        await service.UpdateAsync(created.Id, Area("tempregion_worldtask_51"));

        string final = $"lootbox_area_{created.Id}";
        _renames.Should().Equal(("tempregion_worldtask_51", final, "world", (string?)final));
        (await StoredRegionAsync(created.Id)).Should().Be(final);
    }

    [Fact]
    public async Task Update_AfterARerun_OfAnAreaStillOnATempName_RemovesThatTempRegion()
    {
        _pluginRenames = false;
        var service = Service(_finalizer);
        var created = await service.CreateAsync(Area("tempregion_worldtask_60"));
        _pluginRenames = true;
        _renames.Clear();

        await service.UpdateAsync(created.Id, Area("tempregion_worldtask_61"));

        _renames.Should().Equal(("tempregion_worldtask_61", $"lootbox_area_{created.Id}", "world", (string?)"tempregion_worldtask_60"));
    }

    [Fact]
    public async Task Update_WithTheSameTempRegion_RetriesTheRename_WithoutReplacing()
    {
        _pluginRenames = false;
        var service = Service(_finalizer);
        var created = await service.CreateAsync(Area("tempregion_worldtask_62"));
        _pluginRenames = true;
        _renames.Clear();

        await service.UpdateAsync(created.Id, Area("tempregion_worldtask_62"));

        _renames.Should().Equal(("tempregion_worldtask_62", $"lootbox_area_{created.Id}", "world", (string?)null));
        (await StoredRegionAsync(created.Id)).Should().Be($"lootbox_area_{created.Id}");
    }

    [Fact]
    public async Task Update_ThatOnlyTunesLimits_RenamesNothing()
    {
        var service = Service(_finalizer);
        var created = await service.CreateAsync(Area("tempregion_worldtask_63"));
        _renames.Clear();

        var dto = Area(created.WgRegionId);
        dto.MaxActive = 5;
        await service.UpdateAsync(created.Id, dto);

        _renames.Should().BeEmpty();
    }

    [Fact]
    public async Task Rerun_KeepsABorrowedOrInGameRegion()
    {
        var area = await AddAreaAsync(5, "tempregion_worldtask_70");

        (await _finalizer.ReplaceableLootboxAreaRegionAsync(area, "domain_7", "lootbox_area_5", "world")).Should().BeNull();
        (await _finalizer.ReplaceableLootboxAreaRegionAsync(area, "lootbox_market", "lootbox_area_5", "world")).Should().BeNull();
        (await _finalizer.ReplaceableLootboxAreaRegionAsync(area, " ", "lootbox_area_5", "world")).Should().BeNull();
        (await _finalizer.ReplaceableLootboxAreaRegionAsync(area, "LOOTBOX_AREA_5", "lootbox_area_5", "world")).Should().Be("LOOTBOX_AREA_5");
    }

    [Fact]
    public async Task Rerun_KeepsAPreviousTempRegionSomeoneElseUses()
    {
        var area = await AddAreaAsync(5, "tempregion_worldtask_71");
        await AddAreaAsync(6, "tempregion_worldtask_72");
        _context.Towns.Add(new Town { Id = 1, Name = "Cinix", Description = "t", WgRegionId = "tempregion_worldtask_73", WorldName = "world" });
        await _context.SaveChangesAsync();

        (await _finalizer.ReplaceableLootboxAreaRegionAsync(area, "tempregion_worldtask_72", "lootbox_area_5", "world")).Should().BeNull();
        (await _finalizer.ReplaceableLootboxAreaRegionAsync(area, "tempregion_worldtask_73", "lootbox_area_5", "world")).Should().BeNull();
        // Another world's region of the same id is not this one.
        (await _finalizer.ReplaceableLootboxAreaRegionAsync(area, "tempregion_worldtask_72", "lootbox_area_5", "world_nether")).Should().Be("tempregion_worldtask_72");
    }

    [Fact]
    public async Task ATempRegionSomeoneElseStandsOn_IsNotRenamedAway()
    {
        var area = await AddAreaAsync(5, "tempregion_worldtask_80");
        await AddAreaAsync(6, "tempregion_worldtask_80");

        (await _finalizer.FinalizeLootboxAreaAsync(area, null)).Should().BeFalse();

        _renames.Should().BeEmpty();
        (await StoredRegionAsync(5)).Should().Be("tempregion_worldtask_80");
    }

    // ===== finalize-temp-names and domains =====

    [Fact]
    public async Task FinalizeAll_AlsoRenamesAreasStillOnATempName()
    {
        await AddAreaAsync(5, "tempregion_worldtask_90");
        await AddAreaAsync(6, "lootbox_spawn");

        var result = await _finalizer.FinalizeAllAsync();

        result.Found.Should().Be(1);
        result.Renamed.Should().Be(1);
        _renames.Should().Equal(("tempregion_worldtask_90", "lootbox_area_5", "world", (string?)null));
        (await StoredRegionAsync(5)).Should().Be("lootbox_area_5");
    }

    [Fact]
    public async Task FinalizeAll_ListsAnAreaThePluginRefused()
    {
        _pluginRenames = false;
        await AddAreaAsync(5, "tempregion_worldtask_91");

        var result = await _finalizer.FinalizeAllAsync();

        result.Failed.Should().Equal("LootboxSpawnArea 5 (tempregion_worldtask_91)");
    }

    [Fact]
    public async Task ADomainRerun_KeepsAPreviousTempRegionALootboxAreaStandsOn()
    {
        await AddAreaAsync(5, "tempregion_worldtask_95");
        var town = new Town { Id = 1, Name = "Cinix", Description = "t", WgRegionId = "tempregion_worldtask_96", WorldName = "world" };
        _context.Towns.Add(town);
        await _context.SaveChangesAsync();

        (await _finalizer.ReplaceableRegionAsync(town, "tempregion_worldtask_95")).Should().BeNull();
        (await _finalizer.ReplaceableRegionAsync(town, "tempregion_worldtask_97")).Should().Be("tempregion_worldtask_97");
    }

    // ===== reserved names =====

    [Theory]
    [InlineData("area_5")]
    [InlineData("AREA_12")]
    public async Task AreaNumberNames_AreReserved(string name)
    {
        await Service(_finalizer).Invoking(s => s.CreateAsync(Area("domain_7", name))).Should().ThrowAsync<ArgumentException>();
    }

    [Theory]
    [InlineData("area_north")]
    [InlineData("area_5b")]
    [InlineData("my_area_5")]
    public async Task OtherAreaNames_AreFine(string name)
    {
        var created = await Service(_finalizer).CreateAsync(Area("domain_7", name));

        created.Name.Should().Be(name);
    }

    [Fact]
    public async Task AnExistingAreaWithAReservedName_MayKeepIt()
    {
        await AddAreaAsync(5, "lootbox_area_5", name: "area_5");

        await Service(_finalizer).UpdateAsync(5, Area("lootbox_area_5", "area_5"));
        await Service(_finalizer).Invoking(s => s.UpdateAsync(5, Area("lootbox_area_5", "area_6"))).Should().ThrowAsync<ArgumentException>();
    }
}
