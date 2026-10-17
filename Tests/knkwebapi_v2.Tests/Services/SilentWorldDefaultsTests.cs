using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-118 (D8, D9): no silent world defaults. A District's new inline Location without a world is
/// refused instead of stored with an empty world, and a Location task output without a world gives
/// no Location instead of one in a world named "world".
/// </summary>
public class SilentWorldDefaultsTests
{
    private readonly Mock<IDistrictRepository> _districts = new();
    private readonly Mock<ITownRepository> _towns = new();
    private readonly Mock<ILocationRepository> _locations = new();
    private readonly Mock<IDomainWorldResolver> _worlds = new();
    private readonly DistrictService _service;

    public SilentWorldDefaultsTests()
    {
        _towns.Setup(r => r.GetByIdAsync(1)).ReturnsAsync(new Town { Id = 1, Name = "Rivia", Description = "" });
        _districts.Setup(r => r.GetByIdAsync(5)).ReturnsAsync(new District { Id = 5, Name = "Old", Description = "", TownId = 1 });
        _worlds.Setup(w => w.ResolveAsync(It.IsAny<DomainWorldRequest>())).ReturnsAsync("world");
        _service = new DistrictService(_districts.Object, _towns.Object, _locations.Object, new Mock<IStreetRepository>().Object,
            new Mock<IMapper>().Object, new Mock<IDomainRegionNameFinalizer>().Object, NullLogger<DistrictService>.Instance,
            new Mock<ITeleportDestinationService>().Object, _worlds.Object);
    }

    private static DistrictDto District(string? locationWorld, int? locationId = null) => new()
    {
        Name = "Old Quarter",
        WgRegionId = "district_old",
        TownId = 1,
        Location = new LocationDto { Id = locationId, X = 1, Y = 2, Z = 3, World = locationWorld }
    };

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task CreateWithANewInlineLocationWithoutAWorld_IsRefusedAndSavesNothing(string? world)
    {
        var error = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(District(world)));

        Assert.Contains("needs a world", error.Message);
        _locations.Verify(r => r.AddLocationAsync(It.IsAny<Location>()), Times.Never);
    }

    [Fact]
    public async Task UpdateWithANewInlineLocationWithoutAWorld_IsRefusedAndSavesNothing()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.UpdateAsync(5, District("")));

        _locations.Verify(r => r.AddLocationAsync(It.IsAny<Location>()), Times.Never);
        _districts.Verify(r => r.UpdateDistrictAsync(It.IsAny<District>()), Times.Never);
    }

    [Fact]
    public async Task AnExistingInlineLocationKeepsItsWorldWhenNoneIsSent()
    {
        var stored = new Location { Id = 9, World = "hub" };
        _locations.Setup(r => r.GetByIdAsync(9)).ReturnsAsync(stored);

        try
        {
            await _service.UpdateAsync(5, District("  ", locationId: 9));
        }
        catch (Exception)
        {
            // The steps after the Location need a real mapper; only the Location matters here.
        }

        Assert.Equal("hub", stored.World);
        _locations.Verify(r => r.UpdateLocationAsync(stored), Times.Once);
    }

    [Theory]
    [InlineData("{\"x\":1,\"y\":2,\"z\":3,\"World\":\"hub\"}", "hub")]
    [InlineData("{\"x\":1,\"y\":2,\"z\":3,\"worldName\":\" gameplay \"}", "gameplay")]
    [InlineData("{\"x\":1,\"y\":2,\"z\":3,\"world\":\"hub\"}", "hub")]
    public void LocationTaskOutput_KeepsTheReportedWorld(string json, string expected)
    {
        Assert.Equal(expected, WorkflowService.ExtractLocationDataFromTaskOutput(json)!.World);
    }

    [Theory]
    [InlineData("{\"x\":1,\"y\":2,\"z\":3}")]
    [InlineData("{\"x\":1,\"y\":2,\"z\":3,\"worldName\":\"\"}")]
    public void LocationTaskOutputWithoutAWorld_GivesNoLocation(string json)
    {
        Assert.Null(WorkflowService.ExtractLocationDataFromTaskOutput(json));
    }
}
