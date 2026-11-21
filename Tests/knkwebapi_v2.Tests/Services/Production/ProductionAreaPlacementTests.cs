using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Services.Territory;
using knkwebapi_v2.Services.ValidationMethods;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Moq;
using Xunit;
using static knkwebapi_v2.Tests.Services.Production.ProductionWorldTestDb;

namespace knkwebapi_v2.Tests.Services.Production;

/// <summary>
/// Placement rule P-3 for production areas (PandiO/knk-workspace#883, production DESIGN § 3.4, R2) as the bundled
/// ProductionArea seed v3 ships it: imported as the default form, it runs when the API saves an area. An area of a
/// structure in a district stays inside the district's region; an area of a rural structure inside its town's
/// territory. Seeded as a warning (saved, <c>placementWarnings</c>); an admin's IsBlocking refuses the save; an offline
/// game server never blocks; with production off the territory half passes without a lookup (A21).
/// </summary>
public class ProductionAreaPlacementTests : IDisposable
{
    private readonly ProductionYieldTestDb _y = new();
    private readonly ServiceProvider _provider;
    private readonly Mock<IRegionService> _regions = new();
    private readonly Mock<ITerritoryLookupClient> _territory = new();
    private readonly ProductionAreaService _areas;

    public ProductionAreaPlacementTests()
    {
        var services = new ServiceCollection();
        services.AddSingleton(_y.T.Db);
        _provider = services.BuildServiceProvider();
        var db = _y.T.Db;
        var settings = new ProductionSettingsReader(db);
        var ruleRepo = new FieldValidationRuleRepository(db);
        var validation = new ValidationService(ruleRepo, new IValidationMethod[]
        {
            new ProductionAreaPlacementValidator(db, _regions.Object, _territory.Object, settings),
        }, new Mock<IPlaceholderResolutionService>().Object);
        var rules = new SavedEntityRuleValidator(new FormConfigurationRepository(db), ruleRepo, validation);
        _areas = new ProductionAreaService(db, settings, _y.Engine, _y.Yield, _y.T.Clock, rules);
    }

    public void Dispose()
    {
        _provider.Dispose();
        _y.Dispose();
    }

    private async Task ImportSeedAsync()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "knkwebapi_v2.csproj"))) dir = dir.Parent;
        var db = _y.T.Db;
        var portability = new FormConfigurationPortabilityService(db, new FormConfigurationRepository(db),
            new FormTemplateValidationService(), new MetadataService(_provider.GetRequiredService<IServiceScopeFactory>()),
            new FormConfigurationSeedOptions { Directory = Path.Combine(dir!.FullName, "Data", "FormConfigurationSeeds") });
        Assert.NotNull(await portability.ImportSeedAsync("ProductionArea", dryRun: false, makeDefault: true));
        db.ChangeTracker.Clear();
    }

    private async Task<int> InDistrictAsync() => (await _y.Structures.CreateAsync(new ProductionStructureDto
    {
        Name = "Sawmill", Description = "", WgRegionId = "tmp_saw", DistrictId = Market, StreetId = Main, HouseNumber = 3,
        ProductionKindId = ProductionYieldTestDb.LoggerKind,
    })).Id!.Value;

    private Task<ProductionAreaDto> RegionAsync(int structure, string region = "forest_r") => _areas.CreateAsync(new ProductionAreaDto
    {
        ProductionStructureId = structure, Name = "Forest", Shape = "Region", WgRegionId = region, WorldName = "world",
        MinX = 0, MaxX = 9, MinY = 60, MaxY = 62, MinZ = 0, MaxZ = 9,
    });

    private Task<ProductionAreaDto> CuboidAsync(int structure) => _areas.CreateAsync(new ProductionAreaDto
    {
        ProductionStructureId = structure, Name = "Pit", Shape = "Cuboid", WorldName = "world",
        MinX = 100, MaxX = 110, MinY = 0, MaxY = 10, MinZ = 200, MaxZ = 205,
    });

    [Fact]
    public async Task TheSeedV3_HasP3_OnTheStructureField_AsAWarning()
    {
        await ImportSeedAsync();
        var config = _y.T.Db.FormConfigurations.Include(c => c.Steps).ThenInclude(s => s.Fields).Single(c => c.EntityTypeName == "ProductionArea");
        Assert.Equal("ProductionArea - Default (seed v3)", config.Name);
        var fieldIds = config.Steps.SelectMany(s => s.Fields).Select(f => f.Id).ToList();
        var rule = _y.T.Db.FieldValidationRules.Single(r => fieldIds.Contains(r.FormFieldId));
        Assert.Equal((ProductionAreaPlacementValidator.Type, false), (rule.ValidationType, rule.IsBlocking));
        Assert.Equal("ProductionStructureId", config.Steps.SelectMany(s => s.Fields).Single(f => f.Id == rule.FormFieldId).FieldName);
    }

    [Fact]
    public async Task ARuralStructuresArea_OutsideItsTownsTerritory_WarnsAndSaves()
    {
        await ImportSeedAsync();
        var logger = await _y.LoggerAsync();
        _territory.Setup(t => t.IsRegionInsideAsync(TerritoryHolderKind.Town, Phoenix, "forest_r", "world")).ReturnsAsync(false);

        var area = await RegionAsync(logger);

        Assert.Contains(area.PlacementWarnings!, w => w.Contains("Phoenix") && w.Contains("territory"));
        Assert.Single(_y.T.Db.ProductionAreas.AsNoTracking());
        _regions.VerifyNoOtherCalls(); // a rural structure has no district to check against

        // Inside: no warning; an update answers the warnings too.
        _territory.Setup(t => t.IsRegionInsideAsync(TerritoryHolderKind.Town, Phoenix, "forest_r", "world")).ReturnsAsync(true);
        var updated = await _areas.UpdateAsync(area.Id, new ProductionAreaDto { Name = "Old forest" });
        Assert.Empty(updated.PlacementWarnings!);
    }

    [Fact]
    public async Task ARadiusArea_IsCheckedByItsCentreAndFourEdgePoints()
    {
        await ImportSeedAsync();
        var logger = await _y.LoggerAsync();
        var asked = new List<(double, double)>();
        _territory.Setup(t => t.IsLocationInsideAsync(TerritoryHolderKind.Town, Phoenix, It.IsAny<double>(), It.IsAny<double>(), It.IsAny<string?>()))
            .Callback((TerritoryHolderKind _, int _, double x, double z, string? _) => asked.Add((x, z)))
            .ReturnsAsync((TerritoryHolderKind _, int _, double x, double _, string? _) => x < 30);

        var area = await _y.Areas.CreateAsync(new ProductionAreaDto
        {
            ProductionStructureId = logger, Name = "Grove", Shape = "Radius", Radius = 20, CenterX = 0, CenterY = 64, CenterZ = 0,
        }); // the fixture's service has no rules: nothing asked
        Assert.Empty(asked);

        var checkedArea = await _areas.CreateAsync(new ProductionAreaDto
        {
            ProductionStructureId = logger, Name = "Edge", Shape = "Radius", Radius = 20, CenterX = 15, CenterY = 64, CenterZ = 0,
        });
        Assert.Contains(checkedArea.PlacementWarnings!, w => w.Contains("territory"));
        Assert.Contains((35d, 0d), asked); // centre + radius on x is outside
        Assert.NotNull(area);
    }

    [Fact]
    public async Task AnAreaOfAStructureInADistrict_StaysInsideTheDistrictsRegion()
    {
        await ImportSeedAsync();
        var sawmill = await InDistrictAsync();
        _regions.Setup(r => r.IsRegionContainedAsync("domain_21", "forest_r", true, It.IsAny<string?>())).ReturnsAsync(false);
        var corners = new List<(double, double)>();
        _regions.Setup(r => r.IsLocationInsideRegionAsync("domain_21", It.IsAny<double>(), It.IsAny<double>(), true, It.IsAny<string?>()))
            .Callback((string _, double x, double z, bool _, string? _) => corners.Add((x, z)))
            .ReturnsAsync(true);

        var region = await RegionAsync(sawmill);
        Assert.Contains(region.PlacementWarnings!, w => w.Contains("Market"));

        var cuboid = await CuboidAsync(sawmill);
        Assert.Empty(cuboid.PlacementWarnings!);
        Assert.Equal(new[] { (100d, 200d), (100d, 205d), (110d, 200d), (110d, 205d) }, corners.ToArray());
        _territory.VerifyNoOtherCalls(); // P-3 asks the district, not the territory
    }

    [Fact]
    public async Task SetToBlocking_P3RefusesTheSave_AndAnOfflineServerNeverBlocks()
    {
        await ImportSeedAsync();
        foreach (var rule in _y.T.Db.FieldValidationRules) rule.IsBlocking = true;
        await _y.T.Db.SaveChangesAsync();
        _y.T.Db.ChangeTracker.Clear();
        var logger = await _y.LoggerAsync();
        _territory.Setup(t => t.IsRegionInsideAsync(TerritoryHolderKind.Town, Phoenix, It.IsAny<string>(), It.IsAny<string?>())).ReturnsAsync(false);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => RegionAsync(logger));
        Assert.Contains("territory", ex.Message);
        Assert.Empty(_y.T.Db.ProductionAreas.AsNoTracking());

        _territory.Setup(t => t.IsRegionInsideAsync(TerritoryHolderKind.Town, Phoenix, It.IsAny<string>(), It.IsAny<string?>()))
            .ThrowsAsync(new TerritoryLookupUnavailableException("offline"));
        var saved = await RegionAsync(logger);
        Assert.Contains(saved.PlacementWarnings!, w => w.Contains("not checked"));
    }

    [Fact]
    public async Task WithProductionOff_TheTerritoryHalfPassesWithoutALookup()
    {
        await ImportSeedAsync();
        var logger = await _y.LoggerAsync();
        var row = _y.T.Db.GameSettings.Single();
        row.ProductionSettingsJson = knkwebapi_v2.Json.JsonColumn.Serialize(new ProductionSettingsDto { Enabled = false });
        await _y.T.Db.SaveChangesAsync();
        _y.T.Db.ChangeTracker.Clear();

        var area = await RegionAsync(logger);

        Assert.Empty(area.PlacementWarnings!);
        _territory.VerifyNoOtherCalls();
    }
}
