using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.ValidationMethods;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services.ValidationMethods;

/// <summary>
/// KNG-111: a region id is unique per world only, so the containment checks name the parent domain's world, and a
/// location in another world than its parent domain is refused without asking the plugin.
/// </summary>
public class RegionWorldValidatorTests
{
    private readonly Mock<IRegionService> _regions = new();
    private readonly Mock<ILocationService> _locations = new();

    private static TownDto Town(string? world) => new() { Id = 1, Name = "Hubtown", WgRegionId = "town_1", WorldName = world };

    [Fact]
    public async Task LocationInsideRegion_AsksAboutTheRegionInItsDomainsWorld()
    {
        _regions.Setup(r => r.IsLocationInsideRegionAsync("town_1", 10, 20, false, "hub")).ReturnsAsync(true);
        var validator = new LocationInsideRegionValidator(_regions.Object, _locations.Object);

        var result = await validator.ValidateAsync(new LocationDto { X = 10, Z = 20, World = "hub" }, Town("hub"),
            "{\"regionPropertyPath\":\"WgRegionId\"}", null);

        Assert.True(result.IsValid, result.Message);
        _regions.Verify(r => r.IsLocationInsideRegionAsync("town_1", 10, 20, false, "hub"), Times.Once);
    }

    [Fact]
    public async Task LocationInsideRegion_ALocationInAnotherWorldIsOutside()
    {
        var validator = new LocationInsideRegionValidator(_regions.Object, _locations.Object);

        var result = await validator.ValidateAsync(new LocationDto { X = 10, Z = 20, World = "gameplay" }, Town("hub"),
            "{\"regionPropertyPath\":\"WgRegionId\"}", null);

        Assert.False(result.IsValid);
        Assert.Contains("world 'gameplay'", result.Message);
        _regions.Verify(r => r.IsLocationInsideRegionAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(),
            It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public async Task LocationInsideRegion_ADomainWithoutAWorldUsesTheLocationsWorld()
    {
        _regions.Setup(r => r.IsLocationInsideRegionAsync("town_1", 10, 20, false, "gameplay")).ReturnsAsync(true);
        var validator = new LocationInsideRegionValidator(_regions.Object, _locations.Object);

        var result = await validator.ValidateAsync(new LocationDto { X = 10, Z = 20, World = "gameplay" }, Town(null),
            "{\"regionPropertyPath\":\"WgRegionId\"}", null);

        Assert.True(result.IsValid, result.Message);
    }

    [Fact]
    public async Task RegionContainment_AsksAboutTheParentsWorld()
    {
        _regions.Setup(r => r.IsRegionContainedAsync("town_1", "tempregion_worldtask_7", true, "hub")).ReturnsAsync(true);
        var validator = new RegionContainmentValidator(_regions.Object);

        var result = await validator.ValidateAsync("tempregion_worldtask_7", Town("hub"),
            "{\"parentRegionPath\":\"WgRegionId\"}", null);

        Assert.True(result.IsValid, result.Message);
        _regions.Verify(r => r.IsRegionContainedAsync("town_1", "tempregion_worldtask_7", true, "hub"), Times.Once);
    }
}
