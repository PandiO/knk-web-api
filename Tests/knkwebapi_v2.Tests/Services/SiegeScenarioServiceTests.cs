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
/// Siege Phase 2 (docs/specs/siege-minigame/IMPLEMENTATION_PLAN.md Phase 2): SiegeScenarioService
/// save-time validation, team identity resolution (clan / override / ad-hoc), owned-child behaviour
/// and the match-history delete guard.
/// </summary>
public class SiegeScenarioServiceTests
{
    private readonly Mock<ISiegeScenarioRepository> _repo = new();
    private readonly Mock<ILocationService> _locationService = new();
    private readonly SiegeScenarioService _service;
    private SiegeScenario? _addedScenario;
    private SiegeTeam? _addedTeam;
    private SiegeObjective? _addedObjective;

    public SiegeScenarioServiceTests()
    {
        var town = SiegeTestData.Town();
        _repo.Setup(r => r.GetTownAsync(1)).ReturnsAsync(town);
        _repo.Setup(r => r.LocationExistsAsync(It.Is<int>(id => id >= 1000))).ReturnsAsync(true);
        _repo.Setup(r => r.GetDistrictsAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync((IEnumerable<int> ids) => ids.Select(id => new District
            {
                Id = id, Name = $"District {id}", Description = "d", WgRegionId = $"d{id}", TownId = id == 12 ? 2 : 1
            }).ToList());
        _repo.Setup(r => r.GetGateStructuresAsync(It.IsAny<IEnumerable<int>>()))
            .ReturnsAsync((IEnumerable<int> ids) => ids.Select(id => new GateStructure
            {
                Id = id, Name = $"Gate {id}", Description = "g", WgRegionId = $"g{id}",
                DistrictId = id == 411 ? 11 : 10,
                District = new District { Id = id == 411 ? 11 : 10, Name = "d", Description = "d", WgRegionId = "d", TownId = 1 },
                LocationId = id == 499 ? null : 1003
            }).ToList());
        _repo.Setup(r => r.GetClanAsync(5)).ReturnsAsync(new Clan { Id = 5, Name = "Cinix Garrison", ChatColor = "GOLD", BannerDesignId = 50 });
        _repo.Setup(r => r.BannerDesignExistsAsync(It.IsIn(50, 51))).ReturnsAsync(true);
        _repo.Setup(r => r.AddAsync(It.IsAny<SiegeScenario>()))
            .Callback<SiegeScenario>(s => { s.Id = 100; _addedScenario = s; })
            .Returns(Task.CompletedTask);
        _repo.Setup(r => r.AddTeamAsync(It.IsAny<SiegeTeam>()))
            .Callback<SiegeTeam>(t => { t.Id = 777; _addedTeam = t; })
            .Returns(Task.CompletedTask);
        _repo.Setup(r => r.AddObjectiveAsync(It.IsAny<SiegeObjective>()))
            .Callback<SiegeObjective>(o => { o.Id = 888; _addedObjective = o; })
            .Returns(Task.CompletedTask);

        _service = new SiegeScenarioService(_repo.Object, _locationService.Object, Array.Empty<IValidationMethod>(), SiegeTestData.Mapper());
    }

    private static SiegeScenarioUpsertDto ScenarioDto() => new()
    {
        Name = "Siege of Cinix",
        TownId = 1,
        HubLocationId = 1000,
        Districts = new() { new SiegeScenarioDistrictUpsertDto { DistrictId = 10 } }
    };

    private SiegeScenario StoredScenario()
    {
        var scenario = SiegeTestData.ValidScenario();
        _repo.Setup(r => r.GetByIdAsync(100)).ReturnsAsync(scenario);
        return scenario;
    }

    // ---- Scenario ----

    [Fact]
    public async Task CreateScenario_StoresFieldsAndJoins()
    {
        var dto = ScenarioDto();
        dto.Gates = new() { new SiegeScenarioGateUpsertDto { GateStructureId = 400, InitialState = GateDoorOpenState.OPEN, Damageable = false } };

        await _service.CreateAsync(dto);

        Assert.Equal(1000, _addedScenario!.HubLocationId);
        Assert.Equal(10, Assert.Single(_addedScenario.Districts).DistrictId);
        var gate = Assert.Single(_addedScenario.Gates);
        Assert.Equal(GateDoorOpenState.OPEN, gate.InitialState);
        Assert.False(gate.Damageable);
        Assert.Null(gate.InitialOwnerTeamId);
    }

    [Fact]
    public async Task CreateScenario_WithInlineHubLocation_CreatesTheLocation()
    {
        var dto = ScenarioDto();
        dto.HubLocationId = null;
        dto.HubLocation = new LocationDto { Name = "Hub", X = 1, Y = 64, Z = 2, World = "world" };
        _locationService.Setup(l => l.CreateAsync(dto.HubLocation)).ReturnsAsync(new LocationDto { Id = 1234 });

        await _service.CreateAsync(dto);

        Assert.Equal(1234, _addedScenario!.HubLocationId);
    }

    [Fact]
    public async Task CreateScenario_RequiresAHubLocation()
    {
        var dto = ScenarioDto();
        dto.HubLocationId = null;
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(dto));
    }

    [Fact]
    public async Task CreateScenario_RejectsDistrictOfAnotherTown()
    {
        var dto = ScenarioDto();
        dto.Districts!.Add(new SiegeScenarioDistrictUpsertDto { DistrictId = 12 });
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(dto));
        Assert.Contains("doesn't belong to town", ex.Message);
    }

    [Fact]
    public async Task CreateScenario_RejectsGateOutsideTheSelectedDistricts()
    {
        var dto = ScenarioDto();
        dto.Gates = new() { new SiegeScenarioGateUpsertDto { GateStructureId = 411 } };   // gate in district 11
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(dto));
    }

    [Theory]
    [InlineData(GateDoorOpenState.OPENING)]
    [InlineData(GateDoorOpenState.JAMMED)]
    public async Task CreateScenario_RejectsNonRestingGateState(GateDoorOpenState state)
    {
        var dto = ScenarioDto();
        dto.Gates = new() { new SiegeScenarioGateUpsertDto { GateStructureId = 400, InitialState = state } };
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(dto));
    }

    [Fact]
    public async Task CreateScenario_RejectsGateOwnerTeam_SinceANewScenarioHasNoTeams()
    {
        var dto = ScenarioDto();
        dto.Gates = new() { new SiegeScenarioGateUpsertDto { GateStructureId = 400, InitialOwnerTeamId = 201 } };
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(dto));
    }

    [Theory]
    [InlineData(5, 4, 300, 1800)]     // max < min players
    [InlineData(2, 50, 2000, 1800)]   // min > max duration
    [InlineData(0, 50, 300, 1800)]    // no players
    public async Task CreateScenario_RejectsInvalidRanges(int min, int max, int durMin, int durMax)
    {
        var dto = ScenarioDto();
        dto.PlayersMin = min; dto.PlayersMax = max;
        dto.MatchDurationMinSeconds = durMin; dto.MatchDurationMaxSeconds = durMax;
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(dto));
    }

    [Fact]
    public async Task UpdateScenario_AssignsGateOwnerFromItsOwnTeams_AndLeavesTeamsAlone()
    {
        var scenario = StoredScenario();
        var dto = ScenarioDto();
        dto.Name = "Renamed";
        dto.Gates = new() { new SiegeScenarioGateUpsertDto { GateStructureId = 400, InitialOwnerTeamId = 202 } };

        await _service.UpdateAsync(100, dto);

        Assert.Equal("Renamed", scenario.Name);
        Assert.Equal(202, Assert.Single(scenario.Gates).InitialOwnerTeamId);
        Assert.Equal(2, scenario.Teams.Count);          // owned children untouched by the parent's update
        Assert.Equal(2, scenario.Objectives.Count);
        _repo.Verify(r => r.UpdateAsync(scenario), Times.Once);
    }

    [Fact]
    public async Task UpdateScenario_NullJoinLists_LeaveThemUnchanged_EmptyListsClearThem()
    {
        var scenario = StoredScenario();
        var dto = ScenarioDto();
        dto.Districts = null;
        dto.Gates = null;
        await _service.UpdateAsync(100, dto);
        Assert.Single(scenario.Districts);
        Assert.Single(scenario.Gates);

        dto.Districts = new();
        dto.Gates = new();
        await _service.UpdateAsync(100, dto);
        Assert.Empty(scenario.Districts);
        Assert.Empty(scenario.Gates);
    }

    [Fact]
    public async Task UpdateScenario_KeepsTheExistingJoinRowInstance()
    {
        var scenario = StoredScenario();
        var existingRow = scenario.Gates.Single();
        var dto = ScenarioDto();
        dto.Gates = new() { new SiegeScenarioGateUpsertDto { GateStructureId = 400, Damageable = false } };

        await _service.UpdateAsync(100, dto);

        Assert.Same(existingRow, Assert.Single(scenario.Gates));
        Assert.False(existingRow.Damageable);
    }

    [Fact]
    public async Task DeleteScenario_WithMatchHistory_IsRefused()
    {
        StoredScenario();
        _repo.Setup(r => r.HasMatchHistoryAsync(100)).ReturnsAsync(true);

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeleteAsync(100));
        _repo.Verify(r => r.DeleteAsync(It.IsAny<SiegeScenario>()), Times.Never);
    }

    // ---- Teams + identity resolution ----

    [Fact]
    public async Task CreateTeam_ClanSourced_ResolvesIdentityFromTheClan_AndAppendsSortOrder()
    {
        StoredScenario();
        _repo.Setup(r => r.GetTeamByIdAsync(777)).ReturnsAsync(() => { _addedTeam!.Clan = new Clan { Id = 5, Name = "Cinix Garrison", ChatColor = "GOLD", BannerDesignId = 50 }; return _addedTeam; });

        var dto = await _service.CreateTeamAsync(100, new SiegeTeamUpsertDto { Role = SiegeTeamRole.Defender, AllianceGroup = 1, ClanId = 5 });

        Assert.Equal(2, _addedTeam!.SortOrder);   // after teams 0 and 1
        Assert.Equal("Cinix Garrison", dto.ResolvedName);
        Assert.Equal("GOLD", dto.ResolvedChatColor);
        Assert.Equal(50, dto.ResolvedBannerDesignId);
    }

    [Fact]
    public void Identity_TeamOverridesWinPerField_BlankMeansUnset()
    {
        var clan = new Clan { Id = 5, Name = "Cinix Garrison", ChatColor = "GOLD", BannerDesignId = 50 };
        var team = new SiegeTeam { ClanId = 5, Clan = clan, Name = "Royal Guard", ChatColor = "  ", BannerDesignId = null };

        Assert.Equal("Royal Guard", SiegeTeamIdentity.ResolveName(team));   // override
        Assert.Equal("GOLD", SiegeTeamIdentity.ResolveChatColor(team));      // blank -> clan
        Assert.Equal(50, SiegeTeamIdentity.ResolveBannerDesignId(team));    // null -> clan

        team.BannerDesignId = 51;
        Assert.Equal(51, SiegeTeamIdentity.ResolveBannerDesignId(team));
    }

    [Fact]
    public void Identity_AdHocTeam_UsesItsOwnValues_AndMissingValuesStayNull()
    {
        var team = new SiegeTeam { Name = "Raiders", ChatColor = "RED", BannerDesignId = 51 };
        Assert.Equal("Raiders", SiegeTeamIdentity.ResolveName(team));
        Assert.Equal("RED", SiegeTeamIdentity.ResolveChatColor(team));
        Assert.Equal(51, SiegeTeamIdentity.ResolveBannerDesignId(team));

        team.ChatColor = null;
        Assert.Null(SiegeTeamIdentity.ResolveChatColor(team));
    }

    [Theory]
    [InlineData(null, "RED", 51)]
    [InlineData("Raiders", null, 51)]
    [InlineData("Raiders", "RED", null)]
    public async Task CreateTeam_AdHocWithoutFullIdentity_IsRejected(string? name, string? color, int? banner)
    {
        StoredScenario();
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateTeamAsync(100,
            new SiegeTeamUpsertDto { AllianceGroup = 2, Name = name, ChatColor = color, BannerDesignId = banner }));
    }

    [Fact]
    public async Task CreateTeam_RejectsUnknownChatColour_AndNormalizesKnownOnes()
    {
        StoredScenario();
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateTeamAsync(100,
            new SiegeTeamUpsertDto { ClanId = 5, ChatColor = "PINK" }));

        await _service.CreateTeamAsync(100, new SiegeTeamUpsertDto { ClanId = 5, ChatColor = "dark_red" });
        Assert.Equal("DARK_RED", _addedTeam!.ChatColor);
    }

    [Fact]
    public async Task CreateTeam_BodyScenarioMismatch_IsRejected()
    {
        StoredScenario();
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateTeamAsync(100,
            new SiegeTeamUpsertDto { SiegeScenarioId = 5, ClanId = 5 }));
    }

    [Fact]
    public async Task CreateTeam_UnknownScenario_Is404()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _service.CreateTeamAsync(55, new SiegeTeamUpsertDto { ClanId = 5 }));
    }

    // ---- Spawnpoints ----

    [Fact]
    public async Task CreateSpawnpoint_RequiresALocation_AndAppendsSortOrder()
    {
        var team = StoredScenario().Teams.First();
        _repo.Setup(r => r.GetTeamByIdAsync(201)).ReturnsAsync(team);
        SiegeSpawnpoint? added = null;
        _repo.Setup(r => r.AddSpawnpointAsync(It.IsAny<SiegeSpawnpoint>())).Callback<SiegeSpawnpoint>(p => added = p).Returns(Task.CompletedTask);

        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateSpawnpointAsync(201, new SiegeSpawnpointUpsertDto { Name = "Tower" }));

        await _service.CreateSpawnpointAsync(201, new SiegeSpawnpointUpsertDto { Name = "Tower", LocationId = 1005 });
        Assert.Equal(1, added!.SortOrder);
        Assert.Equal(1005, added.LocationId);
    }

    // ---- Objectives ----

    [Fact]
    public async Task CreateObjective_GateMustBeSelectedFirst()
    {
        StoredScenario();
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateObjectiveAsync(100,
            new SiegeObjectiveUpsertDto { Name = "Postern", GateStructureId = 450 }));
        Assert.Contains("selected gates", ex.Message);
    }

    [Fact]
    public async Task CreateObjective_WithSelectedGate_UsesTheGateLocation()
    {
        StoredScenario();
        await _service.CreateObjectiveAsync(100, new SiegeObjectiveUpsertDto { Name = "Gatehouse 2", GateStructureId = 400 });

        Assert.Equal(400, _addedObjective!.GateStructureId);
        Assert.Null(_addedObjective.LocationId);
        Assert.Equal(GateDoorOpenState.OPEN, _addedObjective.GateStateOnCapture);   // D3 default
    }

    [Fact]
    public async Task CreateObjective_NeedsALocationOrAGate()
    {
        StoredScenario();
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateObjectiveAsync(100, new SiegeObjectiveUpsertDto { Name = "Nowhere" }));
    }

    [Fact]
    public async Task CreateObjective_GateWithoutLocation_NeedsItsOwnLocation()
    {
        var scenario = StoredScenario();
        scenario.Gates.Single().GateStructure.LocationId = null;
        scenario.Gates.Single().GateStructure.Location = null;

        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateObjectiveAsync(100,
            new SiegeObjectiveUpsertDto { Name = "Gatehouse 2", GateStructureId = 400 }));

        await _service.CreateObjectiveAsync(100, new SiegeObjectiveUpsertDto { Name = "Gatehouse 2", GateStructureId = 400, LocationId = 1006 });
        Assert.Equal(1006, _addedObjective!.LocationId);
    }

    [Fact]
    public async Task CreateObjective_HolderMustBeATeamOfThisScenario()
    {
        StoredScenario();
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateObjectiveAsync(100,
            new SiegeObjectiveUpsertDto { Name = "Keep 2", LocationId = 1004, InitialHolderTeamId = 999 }));

        await _service.CreateObjectiveAsync(100, new SiegeObjectiveUpsertDto { Name = "Keep 2", LocationId = 1004, InitialHolderTeamId = 202 });
        Assert.Equal(202, _addedObjective!.InitialHolderTeamId);
    }

    [Fact]
    public async Task CreateObjective_ZeroMeansNoHolderAndNoGate()
    {
        StoredScenario();
        await _service.CreateObjectiveAsync(100, new SiegeObjectiveUpsertDto
        {
            Name = "Keep 2", LocationId = 1004, InitialHolderTeamId = 0, GateStructureId = 0
        });
        Assert.Null(_addedObjective!.InitialHolderTeamId);
        Assert.Null(_addedObjective.GateStructureId);
    }
}
