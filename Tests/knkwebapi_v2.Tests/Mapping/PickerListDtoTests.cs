using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Mapping;

/// <summary>
/// KNG-166: the paged search endpoints' list rows carry what an admin needs to tell two rows apart
/// in a relationship picker (a street's districts and towns, a structure's or gate's town, a
/// town's world). Each test goes through the repository's search, so a missing Include shows up
/// (the change tracker is cleared, nothing is loaded by accident).
/// </summary>
public class PickerListDtoTests : IDisposable
{
    private readonly KnKDbContext _ctx =
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private readonly IMapper _mapper = new MapperConfiguration(cfg =>
    {
        cfg.AddProfile<StreetMappingProfile>();
        cfg.AddProfile<StructureMappingProfile>();
        cfg.AddProfile<GateStructureMappingProfile>();
        cfg.AddProfile<TownMappingProfile>();
        cfg.AddProfile<DistrictMappingProfile>();
    }).CreateMapper();

    public PickerListDtoTests()
    {
        var rivia = new Town { Id = 1, Name = "Rivia", Description = "", WgRegionId = "town_rivia", WorldName = "knk_hub" };
        var cinix = new Town { Id = 2, Name = "Cinix", Description = "", WgRegionId = "town_cinix", WorldName = "knk_hub" };
        var oldQuarter = new District { Id = 3, Name = "Old Quarter", Description = "", WgRegionId = "d_oq", Town = rivia };
        var harbour = new District { Id = 4, Name = "Harbour", Description = "", WgRegionId = "d_h", Town = rivia };
        var cinixMarket = new District { Id = 5, Name = "Market", Description = "", WgRegionId = "d_m", Town = cinix };
        _ctx.Towns.AddRange(rivia, cinix);
        _ctx.Districts.AddRange(oldQuarter, harbour, cinixMarket);
        _ctx.Streets.Add(new Street { Id = 7, Name = "Main", Districts = { oldQuarter, harbour, cinixMarket } });
        _ctx.Streets.Add(new Street { Id = 8, Name = "Lonely Lane" });
        _ctx.Structures.Add(new Structure { Id = 10, Name = "Smithy", Description = "", WgRegionId = "s_smithy", StreetId = 7, DistrictId = 3, HouseNumber = 12 });
        _ctx.Set<GateStructure>().Add(new GateStructure { Id = 11, Name = "North Gate", Description = "", WgRegionId = "g_north", StreetId = 7, DistrictId = 5, HouseNumber = 1 });
        _ctx.SaveChanges();
        _ctx.ChangeTracker.Clear();
    }

    public void Dispose() => _ctx.Dispose();

    private static PagedQuery Query() => new() { PageNumber = 1, PageSize = 25 };

    [Fact]
    public async Task StreetRows_NameTheirDistrictsAndTowns()
    {
        var page = await new StreetRepository(_ctx).SearchAsync(Query());
        var rows = _mapper.Map<List<StreetListDto>>(page.Items);

        var main = rows.Single(r => r.id == 7);
        Assert.Equal("Harbour, Market, Old Quarter", main.districtNames);
        Assert.Equal("Cinix, Rivia", main.townNames);

        var lonely = rows.Single(r => r.id == 8);
        Assert.Null(lonely.districtNames);
        Assert.Null(lonely.townNames);
    }

    [Fact]
    public async Task StructureRows_NameStreetDistrictAndTown()
    {
        var page = await new StructureRepository(_ctx).SearchAsync(Query());
        var smithy = _mapper.Map<List<StructureListDto>>(page.Items).Single(r => r.id == 10);

        Assert.Equal(7, smithy.streetId);
        Assert.Equal("Main", smithy.streetName);
        Assert.Equal(12, smithy.houseNumber);
        Assert.Equal(3, smithy.districtId);
        Assert.Equal("Old Quarter", smithy.districtName);
        Assert.Equal("Rivia", smithy.townName);
    }

    [Fact]
    public async Task GateRows_NameStreetDistrictAndTown()
    {
        var page = await new GateStructureRepository(_ctx).SearchAsync(Query());
        var gate = _mapper.Map<List<GateStructureListDto>>(page.Items).Single();

        Assert.Equal("Main", gate.streetName);
        Assert.Equal(1, gate.houseNumber);
        Assert.Equal(5, gate.districtId);
        Assert.Equal("Market", gate.districtName);
        Assert.Equal("Cinix", gate.townName);
    }

    [Fact]
    public async Task TownRows_NameTheirWorld()
    {
        var page = await new TownRepository(_ctx).SearchAsync(Query());
        var rows = _mapper.Map<List<TownListDto>>(page.Items);

        Assert.All(rows, r => Assert.Equal("knk_hub", r.WorldName));
    }

    [Fact]
    public async Task DistrictRows_NameTheirTown()
    {
        var page = await new DistrictRepository(_ctx).SearchAsync(Query());
        var market = _mapper.Map<List<DistrictListDto>>(page.Items).Single(r => r.id == 5);

        Assert.Equal(2, market.townId);
        Assert.Equal("Cinix", market.townName);
    }

    [Fact]
    public async Task StructureAndGateReads_NameTheirDistrictsTown()
    {
        // A structure or gate created in a form is pinned in the picker from its full read DTO; its
        // Town column needs the district's town there too, like the search row.
        var structure = await new StructureRepository(_ctx).GetByIdAsync(10);
        Assert.Equal("Rivia", _mapper.Map<StructureDto>(structure).District!.TownName);

        _ctx.ChangeTracker.Clear();
        var gate = await new GateStructureRepository(_ctx).GetByIdAsync(11);
        Assert.Equal("Cinix", _mapper.Map<GateStructureDto>(gate).District!.TownName);
    }
}
