using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.ValidationMethods;
using knkwebapi_v2.Tests.Services.Regions;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services.ValidationMethods;

/// <summary>
/// PandiO/knk-workspace#988: the spot form's two rules on the region field. The size check shows the class for the
/// facing or "too small" (W70, A02); the site check warns about a floor that isn't solid or space that isn't clear
/// (W44). A game server that can't be asked gives a passing result with a failureReason: the form says it couldn't
/// check and nothing blocks the save. Both ask in the scenario's world.
/// </summary>
public sealed class SiegeWeaponSpotValidatorTests : IDisposable
{
    private readonly KnKDbContext _db;
    private readonly FakeGameRegionQueryClient _game = new FakeGameRegionQueryClient()
        .Box("spot_large", 0, 64, 0, 8, 80, 14)
        .Box("spot_small", 0, 64, 0, 6, 68, 8)
        .Box("spot_tiny", 0, 64, 0, 4, 68, 4)
        .Box("spot_poly", 0, 64, 0, 8, 80, 14, shape: "polygon");

    public SiegeWeaponSpotValidatorTests()
    {
        _db = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        _db.Towns.Add(new Town { Id = 1, Name = "Cinix", Description = "", WgRegionId = "cinix", WorldName = "siege" });
        _db.SiegeScenarios.Add(new SiegeScenario { Id = 100, Name = "Siege of Cinix", TownId = 1, HubLocationId = 1000 });
        _db.Locations.Add(new Location { Id = 1000, World = "hub_world" });
        _db.SaveChanges();
    }

    public void Dispose() => _db.Dispose();

    private static Dictionary<string, object> Form(string facing = "North") =>
        new() { ["SiegeScenarioId"] = 100, ["Facing"] = facing };

    private Task<knkwebapi_v2.Services.Interfaces.ValidationMethodResult> Size(string? region, string facing = "North",
        object? dependency = null) =>
        new SiegeWeaponSpotSizeValidator(_db, _game).ValidateAsync(region, dependency, "{}", Form(facing));

    private Task<knkwebapi_v2.Services.Interfaces.ValidationMethodResult> Site(string? region) =>
        new SiegeWeaponSpotSiteValidator(_db, _game).ValidateAsync(region, null, "{}", Form());

    [Fact]
    public void Types_MatchTheSeed()
    {
        Assert.Equal("SiegeWeaponSpotSize", new SiegeWeaponSpotSizeValidator(_db, _game).ValidationType);
        Assert.Equal("SiegeWeaponSpotSite", new SiegeWeaponSpotSiteValidator(_db, _game).ValidationType);
        Assert.True(new SiegeWeaponSpotSizeValidator(_db, _game).ReadsExternalState);
    }

    [Fact]
    public async Task Size_ShowsTheClassForTheFacing()
    {
        var north = await Size("spot_large");
        Assert.True(north.IsValid);
        Assert.Equal("Size class Large: the region is 9 wide x 15 long x 17 high facing North.", north.Message);
        Assert.Equal("Large", north.Metadata!["sizeClass"]);

        var east = await Size("spot_large", "East");
        Assert.True(east.IsValid);
        Assert.StartsWith("Size class Medium: the region is 15 wide x 9 long x 17 high facing East.", east.Message);
        Assert.Equal(("spot_large", (string?)"siege"), _game.Asked[0]);
    }

    [Fact]
    public async Task Size_TheDependencyValueWinsOverTheFormValue()
    {
        var result = await Size("spot_large", "North", dependency: "East");
        Assert.Equal("Medium", result.Metadata!["sizeClass"]);
    }

    [Fact]
    public async Task Size_TooSmall_Fails_WithTheReason()
    {
        var tiny = await Size("spot_tiny");
        Assert.False(tiny.IsValid);
        Assert.Null(tiny.Metadata);
        Assert.Equal("The region is too small for a weapon facing North: 5 wide x 5 long x 5 high, the small class needs at least 7 wide x 9 long x 5 high.",
            tiny.Placeholders!["sizeReason"]);
        // 7 x 9 is small facing north but too short facing east.
        Assert.True((await Size("spot_small")).IsValid);
        Assert.False((await Size("spot_small", "East")).IsValid);
    }

    [Fact]
    public async Task Size_APolygon_CountsAsItsBoundingBox_AndSaysSo()
    {
        var result = await Size("spot_poly");
        Assert.True(result.IsValid);
        Assert.Contains("polygon; its bounding box counts, so draw a box", result.Message);
    }

    [Fact]
    public async Task Size_AnUnknownRegion_Fails()
    {
        var result = await Size("nowhere");
        Assert.False(result.IsValid);
        Assert.Contains("nowhere", result.Placeholders!["sizeReason"]);
    }

    [Fact]
    public async Task Size_GameServerOffline_PassesWithAFailureReason()
    {
        _game.Offline = true;
        var result = await Size("spot_tiny");
        Assert.True(result.IsValid);
        Assert.Equal("GameServerUnavailable", result.Metadata!["failureReason"]);
        Assert.StartsWith("Size class not checked: No game server is connected", result.Message);
    }

    [Fact]
    public async Task Size_WithoutARegion_PassesAndAsksNothing()
    {
        Assert.True((await Size(null)).IsValid);
        Assert.True((await Size(" ")).IsValid);
        Assert.Empty(_game.Asked);
    }

    [Fact]
    public async Task Site_Clear_Passes()
    {
        var result = await Site("spot_large");
        Assert.True(result.IsValid);
        Assert.Equal("The floor is solid and the space is clear.", result.Message);
    }

    [Fact]
    public async Task Site_OpenFloorOrBlockedSpace_Warns()
    {
        _game.Site("spot_large", openFloor: 4, blocked: 12, volumeChecked: true,
            "The floor isn't solid: 4 of 63 blocks under the spot are air or not solid, so a weapon would hang in the air.",
            "The volume isn't clear: 12 blocks inside the spot would be replaced by the weapon (they come back when it is removed).");
        var result = await Site("spot_large");
        Assert.False(result.IsValid);
        Assert.Contains("The floor isn't solid", result.Placeholders!["siteReason"]);
        Assert.Contains("The volume isn't clear", result.Placeholders["siteReason"]);
    }

    [Fact]
    public async Task Site_GameServerOfflineOrFlagOff_PassesWithAFailureReason()
    {
        _game.Offline = true;
        var result = await Site("spot_large");
        Assert.True(result.IsValid);
        Assert.Equal("GameServerUnavailable", result.Metadata!["failureReason"]);
        Assert.Contains("siege.weapons.enabled", result.Message);
    }

    // ---- both rules on the region field, through the real ValidationService (as the seed has them) ----

    private Task<knkwebapi_v2.Dtos.ValidationResultDto> BothRulesAsync(string region, string facing = "North")
    {
        var regionField = new FormField { Id = 701, FieldName = "WgRegionId", Label = "Region", FieldType = knkwebapi_v2.Enums.FieldType.String };
        var facingField = new FormField { Id = 702, FieldName = "Facing", Label = "Facing", FieldType = knkwebapi_v2.Enums.FieldType.Enum };
        var rules = new List<FieldValidationRule>
        {
            new() { Id = 1, FormFieldId = 701, FormField = regionField, ValidationType = "SiegeWeaponSpotSize", DependsOnFieldId = 702,
                DependsOnField = facingField, ConfigJson = "{}", ErrorMessage = "{sizeReason}", IsBlocking = true },
            new() { Id = 2, FormFieldId = 701, FormField = regionField, ValidationType = "SiegeWeaponSpotSite", ConfigJson = "{}",
                ErrorMessage = "{siteReason}", IsBlocking = false },
        };
        var repo = new Moq.Mock<knkwebapi_v2.Repositories.IFieldValidationRuleRepository>();
        repo.Setup(r => r.GetByFormFieldIdAsync(701)).Returns(Task.FromResult<IEnumerable<FieldValidationRule>>(rules));
        var service = new knkwebapi_v2.Services.ValidationService(repo.Object, new knkwebapi_v2.Services.Interfaces.IValidationMethod[]
        {
            new SiegeWeaponSpotSizeValidator(_db, _game), new SiegeWeaponSpotSiteValidator(_db, _game),
        }, new Moq.Mock<knkwebapi_v2.Services.Interfaces.IPlaceholderResolutionService>().Object);
        return service.ValidateFieldAsync(701, region, null, Form(facing));
    }

    [Fact]
    public async Task BothRules_TooSmallAndAWarning_TheBlockingSizeReasonIsShown_NotTheSiteReason()
    {
        _game.Site("spot_tiny", openFloor: 4, blocked: 0, volumeChecked: true, "The floor isn't solid: 4 of 25 blocks.");
        var result = await BothRulesAsync("spot_tiny");
        Assert.False(result.IsValid);
        Assert.True(result.IsBlocking);
        Assert.Equal("{sizeReason}", result.Message);
        Assert.StartsWith("The region is too small for a weapon facing North", result.Placeholders!["sizeReason"]?.ToString());
        Assert.Equal("The floor isn't solid: 4 of 25 blocks.", result.Placeholders["siteReason"]?.ToString());
    }

    [Fact]
    public async Task BothRules_Offline_PassButSayTheyCouldNotCheck()
    {
        _game.Offline = true;
        var result = await BothRulesAsync("spot_large");
        Assert.True(result.IsValid);
        Assert.Equal("GameServerUnavailable", result.Metadata!.FailureReason);
        Assert.Contains("Size class not checked", result.Message);
        Assert.Contains("Floor and space not checked", result.Message);
    }

    [Fact]
    public async Task BothRules_Pass_ShowTheClassAndTheClearSite()
    {
        var result = await BothRulesAsync("spot_large", "East");
        Assert.True(result.IsValid);
        Assert.Null(result.Metadata!.FailureReason);
        Assert.Equal("Size class Medium: the region is 15 wide x 9 long x 17 high facing East.; The floor is solid and the space is clear.", result.Message);
    }
}
