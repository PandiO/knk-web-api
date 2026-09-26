using System.Text.Json;
using System.Text.Json.Serialization;
using KnKWebAPI.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using knkwebapi_v2.Controllers;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.ValidationMethods;
using knkwebapi_v2.Tests.Services;
using Xunit;

namespace knkwebapi_v2.Tests.Api;

/// <summary>
/// Siege Phase 2 exit criterion (docs/specs/siege-minigame/IMPLEMENTATION_PLAN.md Phase 2: "Swagger
/// round-trip of a complete scenario graph; readiness goes green only when valid"), proven without a
/// database: the real controllers, services and repositories on InMemory, driven in the order of the
/// plan's Swagger script with the same JSON bodies, deserialized with the app's JSON settings
/// (Program.cs: no naming policy, string enums, case-insensitive web defaults).
/// </summary>
public class SiegeApiRoundTripTests : IAsyncLifetime
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        PropertyNamingPolicy = null,
        Converters = { new JsonStringEnumConverter() }
    };

    private KnKDbContext _context = null!;
    private SiegeScenariosController _scenarios = null!;
    private SiegeTeamsController _teams = null!;
    private SiegeLobbiesController _lobbies = null!;
    private SiegeConfigurationController _configuration = null!;
    private GateStructuresController _gates = null!;

    public async Task InitializeAsync()
    {
        _context = SiegeTestData.NewContext("SiegeRoundTrip");
        await SiegeTestData.SeedSharedRowsAsync(_context);

        var mapper = SiegeTestData.Mapper();
        // The real LocationInsideRegion rule; the plugin's region endpoint says "inside".
        var regionService = new Mock<IRegionService>();
        regionService.Setup(r => r.IsLocationInsideRegionAsync(It.IsAny<string>(), It.IsAny<double>(), It.IsAny<double>(), It.IsAny<bool>()))
            .ReturnsAsync(true);
        var locationService = new Mock<ILocationService>();
        var validators = new IValidationMethod[] { new LocationInsideRegionValidator(regionService.Object, locationService.Object) };

        var scenarioRepo = new SiegeScenarioRepository(_context);
        var scenarioService = new SiegeScenarioService(scenarioRepo, locationService.Object, validators, mapper);
        var configurationService = new SiegeConfigurationService(new SiegeConfigurationRepository(_context));

        _scenarios = new SiegeScenariosController(scenarioService);
        _teams = new SiegeTeamsController(scenarioService);
        _lobbies = new SiegeLobbiesController(new SiegeLobbyService(new SiegeLobbyRepository(_context), scenarioRepo, configurationService, mapper));
        _configuration = new SiegeConfigurationController(configurationService);
        _gates = new GateStructuresController(new GateStructureService(
            new GateStructureRepository(_context), new Mock<knkwebapi_v2.Repositories.Interfaces.ILocationRepository>().Object,
            locationService.Object, mapper));
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    private static T Body<T>(string json) => JsonSerializer.Deserialize<T>(json, Json)!;

    private static T Created<T>(IActionResult result) =>
        Assert.IsType<T>(Assert.IsType<CreatedAtRouteResult>(result).Value);

    private static T Ok<T>(IActionResult result) =>
        Assert.IsType<T>(Assert.IsType<OkObjectResult>(result).Value);

    // Each request runs in a fresh "request scope" as far as change tracking goes.
    private async Task<T> Call<T>(Func<Task<T>> request)
    {
        _context.ChangeTracker.Clear();
        return await request();
    }

    [Fact]
    public async Task CompleteScenarioGraph_RoundTrips_AndReadinessTurnsGreenOnlyWhenValid()
    {
        // 1. POST /api/SiegeScenarios
        var scenario = Created<SiegeScenarioReadDto>(await Call(() => _scenarios.Create(Body<SiegeScenarioUpsertDto>("""
            { "name": "Siege of Cinix", "townId": 1, "hubLocationId": 1000,
              "playersMin": 2, "playersMax": 20, "districts": [ { "districtId": 10 } ] }
            """))));
        var s = scenario.Id;

        // 2. GET /api/siege-scenarios/{id}/readiness - not ready yet
        var readiness = Ok<SiegeScenarioReadinessDto>(await Call(() => _scenarios.GetReadiness(s)));
        Assert.False(readiness.IsReady);
        Assert.Contains(readiness.Errors, e => e.Code == SiegeReadinessCodes.TeamsMinTwo);
        Assert.Contains(readiness.Errors, e => e.Code == SiegeReadinessCodes.ObjectivesMinOne);

        // 3. POST /api/SiegeScenarios/{id}/teams - clan-sourced defenders, ad-hoc attackers
        var defenders = Created<SiegeTeamReadDto>(await Call(() => _scenarios.CreateTeam(s, Body<SiegeTeamUpsertDto>("""
            { "role": "Defender", "allianceGroup": 1, "clanId": 5 }
            """))));
        var attackers = Created<SiegeTeamReadDto>(await Call(() => _scenarios.CreateTeam(s, Body<SiegeTeamUpsertDto>("""
            { "role": "Attacker", "allianceGroup": 2, "name": "Raiders", "chatColor": "RED", "bannerDesignId": 51 }
            """))));
        Assert.Equal("Cinix Garrison", defenders.ResolvedName);
        Assert.Equal("Raiders", attackers.ResolvedName);

        // Readiness still red: teams have no spawnpoints, no objectives.
        readiness = Ok<SiegeScenarioReadinessDto>(await Call(() => _scenarios.GetReadiness(s)));
        Assert.Contains(readiness.Errors, e => e.Code == SiegeReadinessCodes.TeamNoSpawnpoint);

        // 4. POST /api/SiegeTeams/{id}/spawnpoints
        Created<SiegeSpawnpointReadDto>(await Call(() => _teams.CreateSpawnpoint(defenders.Id, Body<SiegeSpawnpointUpsertDto>("""
            { "name": "Keep", "locationId": 1001 }
            """))));
        Created<SiegeSpawnpointReadDto>(await Call(() => _teams.CreateSpawnpoint(attackers.Id, Body<SiegeSpawnpointUpsertDto>("""
            { "name": "Camp", "locationId": 1002, "safeZoneRadius": 6 }
            """))));

        // 5. PUT /api/SiegeScenarios/{id} - select gate 400 (the scenario body again, plus "gates")
        Assert.IsType<NoContentResult>(await Call(() => _scenarios.Update(s, Body<SiegeScenarioUpsertDto>("""
            { "name": "Siege of Cinix", "townId": 1, "hubLocationId": 1000,
              "playersMin": 2, "playersMax": 20, "districts": [ { "districtId": 10 } ],
              "gates": [ { "gateStructureId": 400, "initialState": "CLOSED", "damageable": true } ] }
            """))));

        // 6. POST /api/SiegeScenarios/{id}/objectives - an instant-victory point and a gate objective
        Created<SiegeObjectiveReadDto>(await Call(() => _scenarios.CreateObjective(s, Body<SiegeObjectiveUpsertDto>("""
            { "name": "Keep", "instantVictory": true, "locationId": 1004 }
            """))));
        var gatehouse = Created<SiegeObjectiveReadDto>(await Call(() => _scenarios.CreateObjective(s, Body<SiegeObjectiveUpsertDto>("""
            { "name": "Gatehouse", "gateStructureId": 400 }
            """))));
        Assert.Equal("OPEN", gatehouse.GateStateOnCapture.ToString());

        // 7. Readiness is green now.
        readiness = Ok<SiegeScenarioReadinessDto>(await Call(() => _scenarios.GetReadiness(s)));
        Assert.True(readiness.IsReady, string.Join("; ", readiness.Errors.Select(e => e.Message)));
        Assert.True(readiness.SpatialChecksRun);

        // 8. GET /api/SiegeScenarios/{id} - the whole graph comes back
        var graph = Ok<SiegeScenarioReadDto>(await Call(() => _scenarios.GetById(s)));
        Assert.Equal(2, graph.Teams.Count);
        Assert.All(graph.Teams, t => Assert.Single(t.Spawnpoints));
        Assert.Equal(2, graph.Objectives.Count);
        Assert.Equal("Main gate", Assert.Single(graph.Gates).GateStructureName);
        Assert.Equal("Old Town", Assert.Single(graph.Districts).DistrictName);

        // 9. POST /api/SiegeLobbies, then GET /api/siege-lobbies/runtime-config
        Created<SiegeLobbyReadDto>(await Call(() => _lobbies.Create(Body<SiegeLobbyUpsertDto>($$"""
            { "name": "Siege — Cinix", "key": "cinix", "isEnabled": true,
              "rotation": [ { "siegeScenarioId": {{s}}, "weight": 1 } ] }
            """))));
        var runtime = Assert.IsType<SiegeRuntimeConfigDto>(Assert.IsType<OkObjectResult>((await Call(() => _lobbies.GetRuntimeConfig())).Result).Value);
        var runtimeScenario = Assert.Single(Assert.Single(runtime.Lobbies).Rotation).Scenario;
        Assert.Equal(s, runtimeScenario.Id);
        Assert.Equal("Cinix Garrison", runtimeScenario.Teams.Single(t => t.Id == defenders.Id).Name);

        // 10. PUT /api/SiegeConfiguration (partial)
        var config = Assert.IsType<SiegeConfigurationDto>(Assert.IsType<OkObjectResult>((await Call(() => _configuration.Update(
            Body<UpdateSiegeConfigurationDto>("""{ "headshotMultiplier": 1.0, "nonMemberGateView": "PassThroughOnly" }""")))).Result).Value);
        Assert.Equal(1.0, config.HeadshotMultiplier);
        Assert.Equal(10, config.CaptureAttackBase);

        // 11. DELETE /api/GateStructures/400 - refused while the scenario uses it
        var refused = Assert.IsType<ConflictObjectResult>(await Call(() => _gates.Delete(400)));
        Assert.Contains("BusinessRuleViolation", JsonSerializer.Serialize(refused.Value));

        // 12. Breaking a rule turns readiness red again: remove the gate from Gates.
        Assert.IsType<NoContentResult>(await Call(() => _scenarios.Update(s, Body<SiegeScenarioUpsertDto>("""
            { "name": "Siege of Cinix", "townId": 1, "hubLocationId": 1000,
              "playersMin": 2, "playersMax": 20, "gates": [] }
            """))));
        readiness = Ok<SiegeScenarioReadinessDto>(await Call(() => _scenarios.GetReadiness(s)));
        Assert.Equal(SiegeReadinessCodes.ObjectiveGateNotSelected, Assert.Single(readiness.Errors).Code);

        // 13. DELETE /api/SiegeScenarios/{id} - owned rows go, shared rows stay
        Assert.IsType<NoContentResult>(await Call(() => _scenarios.Delete(s)));
        Assert.IsType<NotFoundResult>(await Call(() => _scenarios.GetById(s)));
        _context.ChangeTracker.Clear();
        Assert.Equal(0, await _context.SiegeTeams.CountAsync());
        Assert.True(await _context.GateStructures.AnyAsync(g => g.Id == 400));
        Assert.True(await _context.Clans.AnyAsync(c => c.Id == 5));
        Assert.Equal(5, await _context.Locations.CountAsync());
    }

    [Fact]
    public async Task ValidationErrors_Are400_UnknownIds_Are404()
    {
        Assert.IsType<BadRequestObjectResult>(await _scenarios.Create(Body<SiegeScenarioUpsertDto>("""
            { "name": "No hub", "townId": 1 }
            """)));
        Assert.IsType<NotFoundResult>(await _scenarios.CreateTeam(999, Body<SiegeTeamUpsertDto>("""{ "clanId": 5 }""")));
        Assert.IsType<NotFoundResult>(await _scenarios.GetReadiness(999));
        Assert.IsType<CreatedAtRouteResult>(await Call(() => _lobbies.Create(Body<SiegeLobbyUpsertDto>("""{ "name": "A", "key": "dup" }"""))));
        Assert.IsType<ConflictObjectResult>(await Call(() => _lobbies.Create(Body<SiegeLobbyUpsertDto>("""{ "name": "B", "key": "DUP" }"""))));
    }
}
