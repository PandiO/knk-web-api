using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
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
/// Placement rules P-1 and P-2 as the bundled seeds ship them (production DESIGN § 3.4, R2, A06): imported as the default
/// form, they run when the API saves a warehouse or production structure. Seeded as warnings (saved, warning returned);
/// an admin's IsBlocking refuses the save; a rule only runs when its own dependency is filled; an offline game server
/// never blocks; with production off the territory rule passes without a lookup (A21).
/// </summary>
public class PlacementRulesTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly KnKDbContext _db;
    private readonly Mock<IRegionService> _regions = new();
    private readonly Mock<ITerritoryLookupClient> _territory = new();

    public PlacementRulesTests()
    {
        var name = Guid.NewGuid().ToString();
        var services = new ServiceCollection();
        services.AddDbContext<KnKDbContext>(o => o.UseInMemoryDatabase(name));
        _provider = services.BuildServiceProvider();
        _db = _provider.GetRequiredService<KnKDbContext>();
        Seed(_db, productionEnabled: true);
    }

    public void Dispose() => _provider.Dispose();

    private async Task ImportSeedAsync(string seed)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "knkwebapi_v2.csproj"))) dir = dir.Parent;
        var portability = new FormConfigurationPortabilityService(_db, new FormConfigurationRepository(_db),
            new FormTemplateValidationService(), new MetadataService(_provider.GetRequiredService<IServiceScopeFactory>()),
            new FormConfigurationSeedOptions { Directory = Path.Combine(dir!.FullName, "Data", "FormConfigurationSeeds") });
        var result = await portability.ImportSeedAsync(seed, dryRun: false, makeDefault: true);
        Assert.NotNull(result);
        _db.ChangeTracker.Clear();
    }

    private ISavedEntityRuleValidator Rules()
    {
        var ruleRepo = new FieldValidationRuleRepository(_db);
        var validation = new ValidationService(ruleRepo, new IValidationMethod[]
        {
            new RegionContainmentValidator(_regions.Object),
            new InsideTerritoryValidator(_territory.Object, new ProductionSettingsReader(_db)),
        }, new Mock<IPlaceholderResolutionService>().Object);
        return new SavedEntityRuleValidator(new FormConfigurationRepository(_db), ruleRepo, validation);
    }

    private void Territory(bool inside) => _territory
        .Setup(t => t.IsRegionInsideAsync(TerritoryHolderKind.Town, Phoenix, It.IsAny<string>(), It.IsAny<string?>()))
        .ReturnsAsync(inside);

    private async Task MakeBlockingAsync(string validationType)
    {
        foreach (var rule in _db.FieldValidationRules.Where(r => r.ValidationType == validationType)) rule.IsBlocking = true;
        await _db.SaveChangesAsync();
        _db.ChangeTracker.Clear();
    }

    [Fact]
    public async Task P2_RuralOutsideItsTownsTerritory_WarnsByDefault_AndSaves()
    {
        await ImportSeedAsync("ProductionStructure");
        Territory(inside: false);

        var created = await Producers(_db, Rules()).CreateAsync(Rural());

        Assert.Single(_db.ProductionStructures.AsNoTracking());
        Assert.Contains(created.PlacementWarnings!, w => w.Contains("territory"));
        _territory.Verify(t => t.IsRegionInsideAsync(TerritoryHolderKind.Town, Phoenix, "tmp_q", "world"), Times.Once);
        _regions.VerifyNoOtherCalls(); // P-1 has no district to check against
    }

    [Fact]
    public async Task AnUpdate_ReturnsThePlacementWarnings_AsCreateDoes()
    {
        // PandiO/knk-workspace#831: an update used to answer 204 and drop the warnings.
        await ImportSeedAsync("ProductionStructure");
        Territory(inside: true);
        var producers = Producers(_db, Rules());
        var created = await producers.CreateAsync(Rural());
        Assert.Empty(created.PlacementWarnings!);

        Territory(inside: false);
        created.Description = "moved";
        var updated = await producers.UpdateAsync(created.Id!.Value, created);
        Assert.Contains(updated.PlacementWarnings!, w => w.Contains("territory"));
        Assert.Equal("moved", updated.Description);
    }

    [Fact]
    public async Task TheStructureSeedV3_HasATownField_SoARuralStructureCanBeMadeFromIt()
    {
        // PandiO/knk-workspace#831: the generic Structure form had only the district fields.
        await ImportSeedAsync("Structure");
        var config = _db.FormConfigurations.Include(c => c.Steps).ThenInclude(s => s.Fields).Single(c => c.EntityTypeName == "Structure");
        Assert.Equal("Structure - Default (seed v3)", config.Name);
        var fields = config.Steps.SelectMany(s => s.Fields).ToList();
        Assert.Equal("Town", fields.Single(f => f.FieldName == "TownId").ObjectType);
        Assert.False(fields.Single(f => f.FieldName == "DistrictId").Required);
    }

    [Fact]
    public void PlainMessage_DropsTheParameterSuffix_OnlyWhenThereIsOne()
    {
        // PandiO/knk-workspace#831: the web app showed "... (Parameter 'dto')".
        Assert.Equal("level must be 1 to 10.",
            knkwebapi_v2.Extensions.ArgumentExceptionExtensions.PlainMessage(new ArgumentException("level must be 1 to 10.", "dto")));
        Assert.Equal("no parameter",
            knkwebapi_v2.Extensions.ArgumentExceptionExtensions.PlainMessage(new ArgumentException("no parameter")));
    }

    [Fact]
    public async Task P2_SetToBlocking_RefusesTheSave()
    {
        await ImportSeedAsync("ProductionStructure");
        await MakeBlockingAsync(InsideTerritoryValidator.Type);
        Territory(inside: false);

        var ex = await Assert.ThrowsAsync<ArgumentException>(() => Producers(_db, Rules()).CreateAsync(Rural()));

        Assert.Contains("territory", ex.Message);
        Assert.Empty(_db.ProductionStructures.AsNoTracking());
        Assert.Empty(_db.Storages.AsNoTracking());
    }

    [Fact]
    public async Task P2_InsideTheTerritory_SavesWithoutWarnings()
    {
        await ImportSeedAsync("ProductionStructure");
        await MakeBlockingAsync(InsideTerritoryValidator.Type);
        Territory(inside: true);

        var created = await Producers(_db, Rules()).CreateAsync(Rural());

        Assert.Empty(created.PlacementWarnings!);
    }

    [Fact]
    public async Task P1_WarehouseRegionOutsideItsDistrict_WarnsByDefault_AndBlocksWhenSet()
    {
        await ImportSeedAsync("Warehouse");
        _regions.Setup(r => r.IsRegionContainedAsync("domain_21", "tmp_wh", true, "world")).ReturnsAsync(false);

        var warned = await Warehouses(_db, Rules()).CreateAsync(InMarket());
        Assert.Contains(warned.PlacementWarnings!, w => w.Contains("district"));
        _territory.VerifyNoOtherCalls(); // P-2 has no town to check against

        await MakeBlockingAsync("RegionContainment");
        await Assert.ThrowsAsync<ArgumentException>(() => Warehouses(_db, Rules()).CreateAsync(InMarket("Second")));
        Assert.Single(_db.Warehouses.AsNoTracking());
    }

    [Fact]
    public async Task BlockingRule_WhenTheGameServerIsOffline_DoesNotBlock_AndSaysItWasNotChecked()
    {
        await ImportSeedAsync("ProductionStructure");
        await MakeBlockingAsync(InsideTerritoryValidator.Type);
        _territory.Setup(t => t.IsRegionInsideAsync(It.IsAny<TerritoryHolderKind>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<string?>()))
            .ThrowsAsync(new TerritoryLookupUnavailableException("the game server is not reachable"));

        var created = await Producers(_db, Rules()).CreateAsync(Rural());

        Assert.Contains(created.PlacementWarnings!, w => w.Contains("not checked"));
    }

    [Fact]
    public async Task ProductionOff_TheTerritoryRulePasses_WithoutALookup()
    {
        var settings = _db.GameSettings.Single();
        settings.ProductionSettingsJson = knkwebapi_v2.Json.JsonColumn.Serialize(new knkwebapi_v2.Dtos.ProductionSettingsDto { Enabled = false });
        await _db.SaveChangesAsync();
        await ImportSeedAsync("ProductionStructure");
        await MakeBlockingAsync(InsideTerritoryValidator.Type);

        var created = await Producers(_db, Rules()).CreateAsync(Rural());

        Assert.Empty(created.PlacementWarnings!);
        _territory.VerifyNoOtherCalls();
    }
}
