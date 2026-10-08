using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
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
/// KNG-73: where /navigate &lt;domain&gt; leads without spawn/region - a default per domain type, a
/// per-domain override on the domain's own form, and the effective value on the search DTO the game
/// server's catalogue reads.
/// </summary>
public class DomainNavigationDefaultsTests
{
    private readonly string _dbName = $"nav-defaults-{Guid.NewGuid()}";

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    private static IMapper Mapper() => new MapperConfiguration(cfg =>
    {
        cfg.AddProfile<DomainMappingProfile>();
        cfg.AddProfile<PagedQueryMappingProfile>();
        cfg.AddProfile<TownMappingProfile>();
        cfg.AddProfile<GateStructureMappingProfile>();
    }).CreateMapper();

    // ==================== the override on a domain's form ====================

    [Fact]
    public void NullLeavesTheOverrideAsItIs()
    {
        var town = new Town { NavigationDefaultOverride = NavigationDestinationMode.Region };

        DomainNavigationDefaults.Apply(town, new TownDto { NavigationDefaultOverride = null });

        Assert.Equal(NavigationDestinationMode.Region, town.NavigationDefaultOverride);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("TypeDefault")]
    [InlineData("typedefault")]
    public void EmptyOrTypeDefaultClearsTheOverride(string value)
    {
        var town = new Town { NavigationDefaultOverride = NavigationDestinationMode.Region };

        DomainNavigationDefaults.Apply(town, new TownDto { NavigationDefaultOverride = value });

        Assert.Null(town.NavigationDefaultOverride);
    }

    [Theory]
    [InlineData("Region", NavigationDestinationMode.Region)]
    [InlineData("region", NavigationDestinationMode.Region)]
    [InlineData(" SPAWN ", NavigationDestinationMode.Spawn)]
    public void ANamedModeSetsTheOverride(string value, NavigationDestinationMode expected)
    {
        var structure = new Structure();

        DomainNavigationDefaults.Apply(structure, new StructureDto { NavigationDefaultOverride = value });

        Assert.Equal(expected, structure.NavigationDefaultOverride);
    }

    [Theory]
    [InlineData("Nearest")]
    [InlineData("1")]
    public void AnUnknownModeIsRefused(string value)
    {
        var district = new District();

        Assert.Throws<ArgumentException>(() =>
            DomainNavigationDefaults.Apply(district, new DistrictDto { NavigationDefaultOverride = value }));
    }

    // ==================== the effective default ====================

    [Fact]
    public void TheDomainsOverrideWinsOverItsType()
    {
        var defaults = new Dictionary<string, NavigationDestinationMode> { ["Town"] = NavigationDestinationMode.Region };

        Assert.Equal(NavigationDestinationMode.Spawn,
            DomainNavigationDefaults.Effective(new Town { NavigationDefaultOverride = NavigationDestinationMode.Spawn }, defaults));
        Assert.Equal(NavigationDestinationMode.Region, DomainNavigationDefaults.Effective(new Town(), defaults));
    }

    [Fact]
    public void ATypeWithoutARowGoesToTheSpawnLocation()
    {
        Assert.Equal(NavigationDestinationMode.Spawn,
            DomainNavigationDefaults.Effective(new GateStructure(), new Dictionary<string, NavigationDestinationMode>()));
        Assert.Equal(NavigationDestinationMode.Spawn, DomainNavigationDefaults.Effective(new District(), null));
    }

    [Fact]
    public void AGateFollowsTheGateStructureDefaultNotTheStructureOne()
    {
        var defaults = new Dictionary<string, NavigationDestinationMode>(StringComparer.OrdinalIgnoreCase)
        {
            ["Structure"] = NavigationDestinationMode.Spawn,
            ["GateStructure"] = NavigationDestinationMode.Region
        };

        Assert.Equal(NavigationDestinationMode.Region, DomainNavigationDefaults.Effective(new GateStructure(), defaults));
        Assert.Equal(NavigationDestinationMode.Spawn, DomainNavigationDefaults.Effective(new Structure(), defaults));
    }

    // ==================== the search DTO (the plugin's catalogue) ====================

    [Fact]
    public async Task SearchCarriesEachDomainsEffectiveDefault()
    {
        var repo = new Mock<IDomainRepository>();
        repo.Setup(r => r.SearchAsync(It.IsAny<PagedQuery>())).ReturnsAsync(new PagedResult<Domain>
        {
            Items = new List<Domain>
            {
                new Town { Id = 1, Name = "Oakhaven", WgRegionId = "town_1" },
                new Town { Id = 2, Name = "Brink", WgRegionId = "town_2", NavigationDefaultOverride = NavigationDestinationMode.Spawn },
                new GateStructure { Id = 3, Name = "North Gate", WgRegionId = "gate_3" },
                new District { Id = 4, Name = "Old Quarter", WgRegionId = "district_4" },
            },
            TotalCount = 4,
            PageNumber = 1,
            PageSize = 50
        });
        repo.Setup(r => r.GetNavigationDefaultsAsync()).ReturnsAsync(
            new Dictionary<string, NavigationDestinationMode>(StringComparer.OrdinalIgnoreCase)
            {
                ["Town"] = NavigationDestinationMode.Region,
                ["GateStructure"] = NavigationDestinationMode.Region,
            });
        var service = new DomainService(repo.Object, Mapper(), new Mock<IDomainRegionNameFinalizer>().Object,
            NullLogger<DomainService>.Instance);

        var result = await service.SearchAsync(new PagedQueryDto { PageNumber = 1, PageSize = 50 });

        Assert.Equal(new[] { "Region", "Spawn", "Region", "Spawn" }, result.Items.Select(i => i.NavigationDefault));
        Assert.Equal(new[] { "Town", "Town", "GateStructure", "District" }, result.Items.Select(i => i.DomainType));
    }

    // ==================== the type defaults (road admin page) ====================

    [Fact]
    public async Task EveryTypeIsListedWithItsDefaultAndOverrideCount()
    {
        await using (var db = NewContext())
        {
            db.DomainNavigationDefaults.Add(new DomainNavigationDefault { DomainType = "Town", DefaultMode = NavigationDestinationMode.Region });
            db.Towns.Add(new Town { Name = "Oakhaven", Description = "", WgRegionId = "t1", NavigationDefaultOverride = NavigationDestinationMode.Spawn });
            db.Towns.Add(new Town { Name = "Brink", Description = "", WgRegionId = "t2" });
            await db.SaveChangesAsync();
        }

        await using var read = NewContext();
        var list = await new DomainNavigationSettingsService(read).GetTypeDefaultsAsync();

        Assert.Equal(new[] { "Town", "District", "Structure", "GateStructure" }, list.Select(d => d.DomainType));
        Assert.Equal(new[] { "Region", "Spawn", "Spawn", "Spawn" }, list.Select(d => d.DefaultMode));
        Assert.Equal(new[] { 1, 0, 0, 0 }, list.Select(d => d.OverrideCount));
    }

    [Fact]
    public async Task UpdatingATypeStoresItsDefaultForTheSearch()
    {
        await using (var db = NewContext())
        {
            var updated = await new DomainNavigationSettingsService(db)
                .UpdateTypeDefaultAsync("gatestructure", new UpdateDomainNavigationDefaultDto { DefaultMode = "region" });
            Assert.Equal(("GateStructure", "Region"), (updated.DomainType, updated.DefaultMode));
        }

        await using var read = NewContext();
        var defaults = await new DomainRepository(read).GetNavigationDefaultsAsync();
        Assert.Equal(NavigationDestinationMode.Region, defaults["GateStructure"]);
        Assert.Equal(NavigationDestinationMode.Region, defaults["gatestructure"]);
    }

    [Fact]
    public async Task AnUnknownTypeOrModeIsRefused()
    {
        await using var db = NewContext();
        var service = new DomainNavigationSettingsService(db);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            service.UpdateTypeDefaultAsync("Street", new UpdateDomainNavigationDefaultDto { DefaultMode = "Region" }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateTypeDefaultAsync("Town", new UpdateDomainNavigationDefaultDto { DefaultMode = "Nearest" }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.UpdateTypeDefaultAsync("Town", new UpdateDomainNavigationDefaultDto { DefaultMode = null }));
    }

    // ==================== mapping ====================

    [Fact]
    public void TheDomainFormReadsTheOverrideAsAString()
    {
        var mapper = Mapper();

        Assert.Equal("Region", mapper.Map<TownDto>(new Town { NavigationDefaultOverride = NavigationDestinationMode.Region }).NavigationDefaultOverride);
        Assert.Null(mapper.Map<TownDto>(new Town()).NavigationDefaultOverride);
        Assert.Equal("Spawn", mapper.Map<GateStructureDto>(
            new GateStructure { NavigationDefaultOverride = NavigationDestinationMode.Spawn }).NavigationDefaultOverride);
    }

    [Fact]
    public void AutoMapperNeverWritesTheOverride()
    {
        var mapper = Mapper();
        var gate = new GateStructure { Name = "North Gate", NavigationDefaultOverride = NavigationDestinationMode.Region };

        mapper.Map(new GateStructureDto { Name = "North Gate", WgRegionId = "g", NavigationDefaultOverride = "" }, gate);
        var town = mapper.Map<Town>(new TownDto { Name = "Oakhaven", WgRegionId = "t", NavigationDefaultOverride = "Region" });

        Assert.Equal(NavigationDestinationMode.Region, gate.NavigationDefaultOverride);
        Assert.Null(town.NavigationDefaultOverride);
    }
}
