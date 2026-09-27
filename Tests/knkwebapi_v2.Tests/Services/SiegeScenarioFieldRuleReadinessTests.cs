using Moq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.ValidationMethods;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Readiness runs the field-validation rules configured on the default siege forms (2026-09-27; they
/// replaced the hard-coded hub/spawnpoint/objective-inside-town checks). Locations in the fixture:
/// hub 1000, spawnpoints 1001 (team 201) and 1002 (team 'Raiders' 202), objective 501 at 1004,
/// objective 502 on gate 400 without a location of its own.
/// </summary>
public class SiegeScenarioFieldRuleReadinessTests
{
    private readonly Mock<ISiegeScenarioRepository> _repo = new();
    private readonly Mock<ILocationService> _locationService = new();
    private readonly SiegeFormRules _forms = new();

    private async Task<SiegeScenarioReadinessDto> ReadinessOf(SiegeScenario scenario, IValidationMethod validator)
    {
        _repo.Setup(r => r.GetByIdAsync(scenario.Id)).ReturnsAsync(scenario);
        var service = new SiegeScenarioService(_repo.Object, _locationService.Object, _forms.Validator(validator), SiegeTestData.Mapper());
        return await service.GetReadinessAsync(scenario.Id);
    }

    [Fact]
    public async Task NoRulesConfigured_ALocationOutsideTheTown_IsStillReady()
    {
        var validator = SiegeTestData.RegionValidator(new HashSet<int> { 1000, 1002, 1004 });

        var result = await ReadinessOf(SiegeTestData.ValidScenario(), validator.Object);

        Assert.True(result.IsReady);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.Equal(0, result.FieldRuleChecks);
        validator.Verify(v => v.ValidateAsync(It.IsAny<object?>(), It.IsAny<object?>(), It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>()), Times.Never);
    }

    [Fact]
    public async Task SpawnpointRule_DependsOnTheScenarioFormsTown_AndFlagsThePointOutside()
    {
        _forms.AddLocationInsideTownRule(SiegeFormRules.SpawnpointLocationFieldId);
        var validator = SiegeTestData.RegionValidator(new HashSet<int> { 1002 });

        var result = await ReadinessOf(SiegeTestData.ValidScenario(), validator.Object);

        Assert.False(result.IsReady);
        var error = Assert.Single(result.Errors);
        Assert.Equal(SiegeReadinessCodes.FieldRuleFailed, error.Code);
        Assert.Equal(nameof(SiegeSpawnpoint), error.EntityType);
        Assert.Equal(302, error.EntityId);
        Assert.StartsWith("Spawnpoint 'Camp' of team 'Raiders' - Location: ", error.Message);
        Assert.Contains("outside Cinix's boundaries", error.Message);   // {Town.Name} from the scenario's town
        Assert.Equal(2, result.FieldRuleChecks);
        Assert.True(result.SpatialChecksRun);

        // The spawnpoint form has no Town field: the dependency is the grandparent scenario's town.
        validator.Verify(v => v.ValidateAsync(
            It.IsAny<Location>(),
            It.Is<object?>(d => d is Town && ((Town)d).WgRegionId == "cinix"),
            It.IsAny<string?>(), It.IsAny<Dictionary<string, object>?>()), Times.Exactly(2));
    }

    [Fact]
    public async Task NonBlockingRule_IsAWarning_AndTheScenarioStaysReady()
    {
        _forms.AddLocationInsideTownRule(SiegeFormRules.SpawnpointLocationFieldId, blocking: false);

        var result = await ReadinessOf(SiegeTestData.ValidScenario(), SiegeTestData.RegionValidator(new HashSet<int> { 1002 }).Object);

        Assert.True(result.IsReady);
        var warning = Assert.Single(result.Warnings);
        Assert.Equal(SiegeReadinessCodes.FieldRuleFailed, warning.Code);
        Assert.Equal(302, warning.EntityId);
    }

    [Fact]
    public async Task HubRule_OnTheScenarioForm_ReportsTheScenario()
    {
        _forms.AddLocationInsideTownRule(SiegeFormRules.HubLocationFieldId);

        var result = await ReadinessOf(SiegeTestData.ValidScenario(), SiegeTestData.RegionValidator(new HashSet<int> { 1000 }).Object);

        var error = Assert.Single(result.Errors);
        Assert.Equal(nameof(SiegeScenario), error.EntityType);
        Assert.Equal(100, error.EntityId);
        Assert.StartsWith("Scenario - HubLocationId: ", error.Message);
    }

    [Fact]
    public async Task ObjectiveRule_ChecksOnlyObjectivesWithALocationOfTheirOwn()
    {
        _forms.AddLocationInsideTownRule(SiegeFormRules.ObjectiveLocationFieldId);

        var result = await ReadinessOf(SiegeTestData.ValidScenario(), SiegeTestData.RegionValidator(new HashSet<int> { 1004 }).Object);

        // 502 captures at its gate and has no LocationId, so its (empty) field isn't checked.
        Assert.Equal(1, result.FieldRuleChecks);
        var error = Assert.Single(result.Errors);
        Assert.Equal(501, error.EntityId);
        Assert.StartsWith("Objective 'Keep' - Capture location: ", error.Message);
    }

    [Fact]
    public async Task PluginUnreachable_IsOneWarning_AndReportsTheChecksNotRun()
    {
        _forms.AddLocationInsideTownRule(SiegeFormRules.HubLocationFieldId);
        _forms.AddLocationInsideTownRule(SiegeFormRules.SpawnpointLocationFieldId);

        var result = await ReadinessOf(SiegeTestData.ValidScenario(), SiegeTestData.RegionValidator(unreachable: true).Object);

        Assert.True(result.IsReady);
        Assert.False(result.SpatialChecksRun);
        Assert.Equal(SiegeReadinessCodes.SpatialChecksUnavailable, Assert.Single(result.Warnings).Code);
    }

    // The real LocationInsideRegionValidator behind a configured rule: proves the saved graph reaches
    // IRegionService with the scenario town's region and the spawnpoint's x/z.
    [Fact]
    public async Task RealLocationInsideRegionValidator_ReportsASpawnpointOutsideTheTown()
    {
        _forms.AddLocationInsideTownRule(SiegeFormRules.SpawnpointLocationFieldId);
        var scenario = SiegeTestData.ValidScenario();
        scenario.Teams.Single(t => t.Id == 202).Spawnpoints.Single().Location!.X = 5000;
        var regionService = new Mock<IRegionService>();
        regionService.Setup(r => r.IsLocationInsideRegionAsync("cinix", It.IsAny<double>(), It.IsAny<double>(), false))
            .ReturnsAsync((string region, double x, double z, bool boundary) => x < 1000);

        var result = await ReadinessOf(scenario, new LocationInsideRegionValidator(regionService.Object, _locationService.Object));

        var error = Assert.Single(result.Errors);
        Assert.Equal(302, error.EntityId);
        Assert.Contains("Location 5000, 0 is outside Cinix's boundaries.", error.Message);
        Assert.True(result.SpatialChecksRun);
    }

    [Fact]
    public async Task RealLocationInsideRegionValidator_PluginDown_IsAWarning()
    {
        _forms.AddLocationInsideTownRule(SiegeFormRules.SpawnpointLocationFieldId);
        var regionService = new Mock<IRegionService>();
        regionService.Setup(r => r.IsLocationInsideRegionAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>()))
            .ThrowsAsync(new RegionServiceUnavailableException("down", new HttpRequestException()));

        var result = await ReadinessOf(SiegeTestData.ValidScenario(), new LocationInsideRegionValidator(regionService.Object, _locationService.Object));

        Assert.True(result.IsReady);
        Assert.False(result.SpatialChecksRun);
        Assert.Equal(SiegeReadinessCodes.SpatialChecksUnavailable, Assert.Single(result.Warnings).Code);
    }
}
