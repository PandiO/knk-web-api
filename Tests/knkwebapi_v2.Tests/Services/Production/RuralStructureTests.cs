using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;
using static knkwebapi_v2.Tests.Services.Production.ProductionWorldTestDb;

namespace knkwebapi_v2.Tests.Services.Production;

/// <summary>
/// Rural structures through the existing structure paths (P2, PandiO/knk-workspace#91; DESIGN § 3.4 audit): the plain
/// Structure service accepts District-xor-Town, the region decisions the game server reads give a rural structure no
/// parent (no town flags inherited, PvP stays allowed), the search lists its town for the territory report (L2 note), and
/// a rural structure on contested land keeps its town and is flagged (R3, A07).
/// </summary>
public class RuralStructureTests : IDisposable
{
    private readonly KnKDbContext _db = Create();

    public void Dispose() => _db.Dispose();

    private StructureService Structures()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<StructureMappingProfile>();
            cfg.AddProfile<LocationMappingProfile>();
            cfg.AddProfile<DomainMappingProfile>();
            cfg.AddProfile<PagedQueryMappingProfile>();
        }).CreateMapper();
        return new StructureService(new StructureRepository(_db), new StreetRepository(_db), new DistrictRepository(_db),
            new LocationRepository(_db), new Mock<ILocationService>().Object, mapper, new Mock<ITeleportDestinationService>().Object,
            new Mock<IDomainRegionNameFinalizer>().Object, FixedWorldResolver.Instance, new TownRepository(_db));
    }

    private static StructureDto RuralDto(int? townId = Phoenix) =>
        new() { Name = "Shepherd's hut", Description = "", WgRegionId = "tmp_hut", TownId = townId };

    [Fact]
    public async Task StructureService_CreatesARuralStructure_WithItsTownAndNoStreet()
    {
        var created = await Structures().CreateAsync(RuralDto());

        var row = _db.Structures.AsNoTracking().Single();
        Assert.Equal(((int?)null, (int?)Phoenix, (int?)null, 0), (row.DistrictId, row.TownId, row.StreetId, row.HouseNumber));
        Assert.Equal((Phoenix, true), (created.EffectiveTownId!.Value, created.IsRural!.Value));
    }

    [Fact]
    public async Task StructureService_RefusesBothAndNeither_AndAnUnknownTown()
    {
        var both = RuralDto();
        both.DistrictId = Market;
        both.StreetId = Main;
        both.HouseNumber = 1;
        await Assert.ThrowsAsync<ArgumentException>(() => Structures().CreateAsync(both));
        await Assert.ThrowsAsync<ArgumentException>(() => Structures().CreateAsync(RuralDto(townId: null)));
        await Assert.ThrowsAsync<ArgumentException>(() => Structures().CreateAsync(RuralDto(townId: 999)));
        Assert.Empty(_db.Structures);
    }

    [Fact]
    public async Task StructureService_Search_ListsTheEffectiveTown_AndFiltersRuralByTown()
    {
        await Structures().CreateAsync(RuralDto());
        await Structures().CreateAsync(new StructureDto { Name = "Bakery", Description = "", WgRegionId = "b", DistrictId = Market, StreetId = Main, HouseNumber = 2 });

        var all = await Structures().SearchAsync(new PagedQueryDto { PageSize = 10, Filters = new() { ["townId"] = Phoenix.ToString() } });
        var rural = await Structures().SearchAsync(new PagedQueryDto { PageSize = 10, Filters = new() { ["isRural"] = "true" } });

        Assert.Equal(2, all.Items.Count);
        Assert.All(all.Items, i => Assert.Equal(Phoenix, i.townId));
        var hut = Assert.Single(rural.Items);
        Assert.Equal(("Shepherd's hut", "Phoenix", (int?)null), (hut.name, hut.townName, hut.districtId));
    }

    [Fact]
    public void RegionDecisions_ForARuralStructure_HaveNoParent()
    {
        var mapper = new MapperConfiguration(cfg => cfg.AddProfile<DomainMappingProfile>()).CreateMapper();
        var rural = new Structure { Id = 5, Name = "Hut", WgRegionId = "domain_5", TownId = Phoenix, Town = new Town { Id = Phoenix, Name = "Phoenix" } };
        var district = new District { Id = Market, Name = "Market", WgRegionId = "domain_21", TownId = Phoenix, Town = new Town { Id = Phoenix, Name = "Phoenix", WgRegionId = "domain_10" } };
        var inDistrict = new Structure { Id = 6, Name = "Bakery", WgRegionId = "domain_6", DistrictId = Market, District = district };

        var ruralDecision = mapper.Map<DomainRegionDecisionDto>(rural);
        var districtDecision = mapper.Map<DomainRegionDecisionDto>(inDistrict);

        Assert.Empty(ruralDecision.ParentDomainDecisions);
        Assert.Equal(new[] { "domain_21", "domain_10" }, districtDecision.ParentDomainDecisions.Select(d => d.WgRegionId));
    }

    [Fact]
    public async Task ContestedLand_TheRuralStructureKeepsItsTown_AndIsFlagged()
    {
        var quarry = await Producers(_db).CreateAsync(Rural());
        var territory = new TerritoryService(_db, new ProductionSettingsReader(_db), NullLogger<TerritoryService>.Instance);

        await territory.ApplyReportAsync(new TerritoryReportDto
        {
            World = "world",
            Overlaps = new() { new TerritoryReportOverlapDto
            {
                HolderA = new TerritoryHolderDto { Kind = "Town", Id = Phoenix },
                HolderB = new TerritoryHolderDto { Kind = "Town", Id = Rivertown },
                Chunks = new() { new[] { 3, 3 } },
            } },
            Structures = new() { new TerritoryReportStructureDto
            {
                StructureId = quarry.Id!.Value, Flag = "Contested",
                HolderA = new TerritoryHolderDto { Kind = "Town", Id = Phoenix },
                HolderB = new TerritoryHolderDto { Kind = "Town", Id = Rivertown },
            } },
        });
        _db.ChangeTracker.Clear();

        var read = (await Producers(_db).GetByIdAsync(quarry.Id.Value))!;
        Assert.Equal((Phoenix, "Contested"), (read.TownId!.Value, read.TerritoryFlag));
        Assert.NotNull(read.TerritoryConflictId);
        Assert.True(read.ProductionEnabled); // keeps producing (R3)
        Assert.Contains(read.Warnings!, w => w.Contains("contested"));
        Assert.Equal(Phoenix, (await territory.GetStructureTerritoryAsync(quarry.Id.Value))!.TownId);
    }
}
