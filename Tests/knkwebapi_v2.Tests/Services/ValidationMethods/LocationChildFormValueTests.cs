using System.Text.Json;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.ValidationMethods;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services.ValidationMethods;

/// <summary>
/// KNG-130: a Location edited in a child form comes back with the form's field names, string numbers and the entity id
/// as a string ("83"). The boundary check and the parent's save must read it like a Location loaded from the API.
/// </summary>
public class LocationChildFormValueTests
{
    // What the Town form holds after "Edit Location" in the child form: the Location form's field names, the numeric
    // inputs as text, and the nested FormWizard's entityId (a string).
    private const string ChildFormLocation =
        "{\"Name\":\"Kardenna spawn\",\"X\":\"120.5\",\"Y\":\"64\",\"Z\":\"-30\",\"Yaw\":\"90\",\"Pitch\":\"0\",\"World\":\"world\",\"id\":\"83\"}";

    [Fact]
    public async Task LocationInsideRegion_ReadsALocationEditedInAChildForm()
    {
        var regions = new Mock<IRegionService>();
        regions.Setup(r => r.IsLocationInsideRegionAsync("town_kardenna", 120.5, -30, false, "world")).ReturnsAsync(true);
        var validator = new LocationInsideRegionValidator(regions.Object, new Mock<ILocationService>().Object);

        // The Town form's own WgRegionId field is the dependency, so it arrives as a JSON string.
        var result = await validator.ValidateAsync(
            JsonDocument.Parse(ChildFormLocation).RootElement,
            JsonDocument.Parse("\"town_kardenna\"").RootElement,
            "{\"regionPropertyPath\":\"WgRegionId\",\"allowBoundary\":false}",
            null);

        Assert.True(result.IsValid, result.Message);
        regions.Verify(r => r.IsLocationInsideRegionAsync("town_kardenna", 120.5, -30, false, "world"), Times.Once);
    }

    [Fact]
    public async Task LocationInsideRegion_AnUnreadableValueSaysSoInsteadOfOutside()
    {
        var regions = new Mock<IRegionService>();
        var validator = new LocationInsideRegionValidator(regions.Object, new Mock<ILocationService>().Object);

        var result = await validator.ValidateAsync(
            JsonDocument.Parse("{\"x\":\"not a number\"}").RootElement,
            JsonDocument.Parse("\"town_kardenna\"").RootElement,
            "{\"regionPropertyPath\":\"WgRegionId\"}",
            null);

        Assert.False(result.IsValid);
        // ValidationService shows a message with a failureReason as is, not the rule's "outside" ErrorMessage.
        Assert.Equal("UnreadableLocation", result.Metadata!["failureReason"]);
        regions.Verify(r => r.IsLocationInsideRegionAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(),
            It.IsAny<bool>(), It.IsAny<string?>()), Times.Never);
    }

    [Fact]
    public void TownDto_AcceptsALocationWithAStringId()
    {
        var body = "{\"id\":19,\"name\":\"Kardenna\",\"wgRegionId\":\"town_kardenna\",\"Location\":" + ChildFormLocation + "}";

        var town = JsonSerializer.Deserialize<TownDto>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(town?.Location);
        Assert.Equal(83, town!.Location!.Id);
        Assert.Equal("Kardenna spawn", town.Location.Name);
    }

    [Fact]
    public void LocationDto_TreatsAnEmptyIdAsNew()
    {
        var location = JsonSerializer.Deserialize<LocationDto>("{\"id\":\"\",\"x\":1,\"y\":2,\"z\":3}");

        Assert.Null(location!.Id);
    }
}
