using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// A domain name is trimmed on every write. Two districts were stored as "Residential District\n" and
/// "Merchant's District\n", and the game server drew the newline as a missing-glyph box in its region entry
/// message. The entity trims, so the create path (AutoMapper from the DTO) and the update path
/// (<c>existing.Name = dto.Name</c>) are both covered without each service repeating it.
/// </summary>
public class DomainNameTrimmingTests
{
    [Fact]
    public void NameIsTrimmedOnEveryDomainType()
    {
        Assert.Equal("Cinix", new Town { Name = " Cinix\n" }.Name);
        Assert.Equal("Residential District", new District { Name = "Residential District\n" }.Name);
        Assert.Equal("Smithy", new Structure { Name = "\tSmithy \r\n" }.Name);
        Assert.Equal("North Gate", new GateStructure { Name = "North Gate\n" }.Name);
    }

    [Fact]
    public void InnerWhitespaceStays()
    {
        Assert.Equal("Merchant's  District", new District { Name = "Merchant's  District" }.Name);
    }

    [Fact]
    public void CreateMappingTrimsTheDtoName()
    {
        var mapper = new MapperConfiguration(cfg =>
        {
            cfg.AddProfile<TownMappingProfile>();
            cfg.AddProfile<DistrictMappingProfile>();
        }).CreateMapper();

        var district = mapper.Map<District>(new DistrictDto { Name = "Residential District\n", Description = "d", WgRegionId = "district_7", TownId = 1 });
        var town = mapper.Map<Town>(new TownDto { Name = "Cinix\n", Description = "t", WgRegionId = "town_1" });

        Assert.Equal("Residential District", district.Name);
        Assert.Equal("Cinix", town.Name);
    }

    [Fact]
    public async Task UpdateAndSaveStoreTheTrimmedName()
    {
        using var context = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        context.Towns.Add(new Town { Id = 1, Name = "Cinix", Description = "t", WgRegionId = "town_1", WorldName = "world" });
        await context.SaveChangesAsync();

        var existing = await context.Towns.SingleAsync(t => t.Id == 1);
        existing.Name = "Cinix Renamed\n";
        await context.SaveChangesAsync();

        var stored = await context.Towns.AsNoTracking().SingleAsync(t => t.Id == 1);
        Assert.Equal("Cinix Renamed", stored.Name);
    }
}
