using Moq;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Siege Phase 2 (docs/specs/siege-minigame/IMPLEMENTATION_PLAN.md Phase 2): the readiness matrix -
/// the complete scenario is ready, and breaking each DESIGN §3.9 rule on its own produces exactly
/// that one error. Location checks are field-validation rules configured on the siege forms (see
/// SiegeScenarioFieldRuleReadinessTests); with none configured nothing spatial is checked.
/// </summary>
public class SiegeScenarioReadinessTests
{
    private readonly Mock<ISiegeScenarioRepository> _repo = new();
    private readonly Mock<ILocationService> _locationService = new();

    private SiegeScenarioService Service() =>
        new(_repo.Object, _locationService.Object, SiegeTestData.NoRules(), SiegeTestData.Mapper());

    private async Task<SiegeScenarioReadinessDto> ReadinessOf(SiegeScenario scenario)
    {
        _repo.Setup(r => r.GetByIdAsync(scenario.Id)).ReturnsAsync(scenario);
        return await Service().GetReadinessAsync(scenario.Id);
    }

    private static SiegeTeam Team(SiegeScenario s, int id) => s.Teams.Single(t => t.Id == id);
    private static SiegeObjective Objective(SiegeScenario s, int id) => s.Objectives.Single(o => o.Id == id);

    [Fact]
    public async Task CompleteScenario_WithoutConfiguredRules_IsReady()
    {
        var result = await ReadinessOf(SiegeTestData.ValidScenario());

        Assert.True(result.IsReady, string.Join("; ", result.Errors.Select(e => e.Code)));
        Assert.Empty(result.Errors);
        Assert.Empty(result.Warnings);
        Assert.True(result.SpatialChecksRun);
        Assert.Equal(0, result.FieldRuleChecks);
    }

    public static IEnumerable<object[]> BrokenRules() => new List<object[]>
    {
        // Teams
        new object[] { SiegeReadinessCodes.TeamsMinTwo, (Action<SiegeScenario>)(s => s.Teams.Remove(Team(s, 202))) },
        new object[] { SiegeReadinessCodes.AlliancesMinTwo, (Action<SiegeScenario>)(s => Team(s, 202).AllianceGroup = 1) },
        new object[] { SiegeReadinessCodes.DefenderRequired, (Action<SiegeScenario>)(s => Team(s, 201).Role = SiegeTeamRole.Attacker) },
        new object[] { SiegeReadinessCodes.TeamNoSpawnpoint, (Action<SiegeScenario>)(s => Team(s, 202).Spawnpoints.Clear()) },
        new object[] { SiegeReadinessCodes.TeamIdentityIncomplete, (Action<SiegeScenario>)(s => Team(s, 202).BannerDesignId = null) },
        // Players / duration
        new object[] { SiegeReadinessCodes.PlayersMinBelowTeamCount, (Action<SiegeScenario>)(s => s.PlayersMin = 1) },
        new object[] { SiegeReadinessCodes.PlayersMaxBelowMin, (Action<SiegeScenario>)(s => { s.PlayersMin = 4; s.PlayersMax = 3; }) },
        new object[] { SiegeReadinessCodes.DurationRangeInvalid, (Action<SiegeScenario>)(s => s.MatchDurationMinSeconds = 2000) },
        // Districts
        new object[] { SiegeReadinessCodes.DistrictOutsideTown, (Action<SiegeScenario>)(s => s.Districts.Add(new SiegeScenarioDistrict
            {
                SiegeScenarioId = 100, DistrictId = 12,
                District = new District { Id = 12, Name = "Elsewhere", Description = "d", WgRegionId = "x", TownId = 2 }
            })) },
        // Objectives
        new object[] { SiegeReadinessCodes.ObjectivesMinOne, (Action<SiegeScenario>)(s => s.Objectives.Clear()) },
        new object[] { SiegeReadinessCodes.ObjectiveNoCaptureLocation, (Action<SiegeScenario>)(s =>
            {
                Objective(s, 501).LocationId = null;
                Objective(s, 501).Location = null;
            }) },
        new object[] { SiegeReadinessCodes.ObjectiveGateNotSelected, (Action<SiegeScenario>)(s =>
            {
                // The objective keeps its gate (and so its capture point) but the gate is no longer selected.
                s.Gates.Clear();
            }) },
        new object[] { SiegeReadinessCodes.ObjectiveHolderNotInScenario, (Action<SiegeScenario>)(s => Objective(s, 501).InitialHolderTeamId = 999) },
        // Gates
        new object[] { SiegeReadinessCodes.GateOwnerNotInScenario, (Action<SiegeScenario>)(s => s.Gates.Single().InitialOwnerTeamId = 999) },
        new object[] { SiegeReadinessCodes.GateOutsideScenarioArea, (Action<SiegeScenario>)(s =>
            {
                var gate = s.Gates.Single().GateStructure;
                gate.DistrictId = 11;
                gate.District = new District { Id = 11, Name = "Harbour", Description = "d", WgRegionId = "cinix_harbour", TownId = 1 };
            }) },
    };

    [Theory]
    [MemberData(nameof(BrokenRules))]
    public async Task EachStructuralRule_FailsAlone(string expectedCode, Action<SiegeScenario> breakRule)
    {
        var scenario = SiegeTestData.ValidScenario();
        breakRule(scenario);

        var result = await ReadinessOf(scenario);

        Assert.False(result.IsReady);
        Assert.Equal(new[] { expectedCode }, result.Errors.Select(e => e.Code).ToArray());
    }

    [Fact]
    public async Task NoInstantVictoryObjective_IsOnlyAWarning()
    {
        var scenario = SiegeTestData.ValidScenario();
        Objective(scenario, 501).InstantVictory = false;

        var result = await ReadinessOf(scenario);

        Assert.True(result.IsReady);
        Assert.Equal(SiegeReadinessCodes.NoInstantVictoryObjective, Assert.Single(result.Warnings).Code);
    }

    [Fact]
    public async Task NoDistricts_IsReadyWithoutWarnings()
    {
        // The area lockdown was removed (2026-09-26), so a scenario without districts gets no warning.
        var scenario = SiegeTestData.ValidScenario();
        scenario.Districts.Clear();   // the gate is still in the town, which is the area without districts

        var result = await ReadinessOf(scenario);

        Assert.True(result.IsReady);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public async Task UnknownScenario_Throws404()
    {
        _repo.Setup(r => r.GetByIdAsync(7)).ReturnsAsync((SiegeScenario?)null);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service().GetReadinessAsync(7));
    }
}
