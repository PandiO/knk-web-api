using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Siege Phase 2 (docs/specs/siege-minigame/IMPLEMENTATION_PLAN.md Phase 2): SiegeLobbyService CRUD
/// validation and GET /api/siege-lobbies/runtime-config - enabled lobbies only, READY scenarios only
/// (unready ones reported as skipped), team identity resolved from the Clan, "first Defender"
/// defaults applied. Runs against the real repositories on an InMemory database.
/// </summary>
public class SiegeLobbyServiceTests : IAsyncLifetime
{
    private KnKDbContext _context = null!;
    private SiegeLobbyService _service = null!;

    public async Task InitializeAsync()
    {
        _context = SiegeTestData.NewContext("SiegeLobby");
        await SiegeTestData.SeedValidScenarioAsync(_context);

        // Scenario 101: saved but incomplete (no teams, no objectives) -> not ready.
        _context.SiegeScenarios.Add(new SiegeScenario { Id = 101, Name = "Draft", TownId = 1, HubLocationId = 1000 });
        _context.SiegeLobbies.Add(new SiegeLobby
        {
            Id = 1, Name = "Siege — Cinix", Key = "cinix", IsEnabled = true,
            Rotation =
            {
                new SiegeLobbyScenario { SiegeScenarioId = 100, Weight = 3 },
                new SiegeLobbyScenario { SiegeScenarioId = 101, Weight = 1 }
            }
        });
        _context.SiegeLobbies.Add(new SiegeLobby
        {
            Id = 2, Name = "Disabled", Key = "off", IsEnabled = false,
            Rotation = { new SiegeLobbyScenario { SiegeScenarioId = 100 } }
        });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        _service = new SiegeLobbyService(
            new SiegeLobbyRepository(_context),
            new SiegeScenarioRepository(_context),
            new SiegeConfigurationService(new SiegeConfigurationRepository(_context)),
            SiegeTestData.Mapper());
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    // ---- Runtime config ----

    [Fact]
    public async Task RuntimeConfig_HasOnlyEnabledLobbies_AndOnlyReadyScenarios()
    {
        var config = await _service.GetRuntimeConfigAsync();

        var lobby = Assert.Single(config.Lobbies);
        Assert.Equal("cinix", lobby.Key);
        var entry = Assert.Single(lobby.Rotation);
        Assert.Equal(100, entry.Scenario.Id);
        Assert.Equal(3, entry.Weight);

        var skipped = Assert.Single(lobby.SkippedScenarios);
        Assert.Equal(101, skipped.SiegeScenarioId);
        Assert.Contains(skipped.Errors, e => e.Code == SiegeReadinessCodes.TeamsMinTwo);
    }

    [Fact]
    public async Task RuntimeConfig_ScenarioBecomesEligibleOnceReady()
    {
        // Break the ready scenario: it drops out of the rotation.
        var team = await _context.SiegeTeams.Include(t => t.Spawnpoints).SingleAsync(t => t.Id == 202);
        _context.SiegeSpawnpoints.RemoveRange(team.Spawnpoints);
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var lobby = Assert.Single((await _service.GetRuntimeConfigAsync()).Lobbies);
        Assert.Empty(lobby.Rotation);
        Assert.Equal(new[] { 100, 101 }, lobby.SkippedScenarios.Select(s => s.SiegeScenarioId).OrderBy(i => i).ToArray());
    }

    [Fact]
    public async Task RuntimeConfig_ResolvesTeamIdentity_ClanSourcedAndAdHoc()
    {
        var scenario = Assert.Single(Assert.Single((await _service.GetRuntimeConfigAsync()).Lobbies).Rotation).Scenario;

        var defenders = scenario.Teams.Single(t => t.Id == 201);
        Assert.Equal("Cinix Garrison", defenders.Name);     // from clan 5
        Assert.Equal("GOLD", defenders.ChatColor);
        Assert.Equal(50, defenders.BannerDesign!.Id);
        Assert.Equal(5, defenders.ClanId);

        var attackers = scenario.Teams.Single(t => t.Id == 202);
        Assert.Equal("Raiders", attackers.Name);            // ad-hoc
        Assert.Equal("RED", attackers.ChatColor);
        Assert.Equal(51, attackers.BannerDesign!.Id);
        Assert.Null(attackers.ClanId);
    }

    [Fact]
    public async Task RuntimeConfig_ClanTeamOverride_WinsPerField()
    {
        var team = await _context.SiegeTeams.SingleAsync(t => t.Id == 201);
        team.Name = "Royal Guard";
        team.BannerDesignId = 51;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var scenario = Assert.Single(Assert.Single((await _service.GetRuntimeConfigAsync()).Lobbies).Rotation).Scenario;
        var defenders = scenario.Teams.Single(t => t.Id == 201);
        Assert.Equal("Royal Guard", defenders.Name);   // override
        Assert.Equal("GOLD", defenders.ChatColor);     // still the clan's
        Assert.Equal(51, defenders.BannerDesign!.Id);  // override
    }

    [Fact]
    public async Task RuntimeConfig_AppliesFirstDefenderDefaults_AndGateCapturePoints()
    {
        var scenario = Assert.Single(Assert.Single((await _service.GetRuntimeConfigAsync()).Lobbies).Rotation).Scenario;

        Assert.All(scenario.Objectives, o => Assert.Equal(201, o.InitialHolderTeamId));
        var gatehouse = scenario.Objectives.Single(o => o.Id == 502);
        Assert.Equal(1003, gatehouse.CaptureLocation!.Id);   // the gate's location
        Assert.Equal(1004, scenario.Objectives.Single(o => o.Id == 501).CaptureLocation!.Id);

        var gate = Assert.Single(scenario.Gates);
        Assert.Equal(201, gate.InitialOwnerTeamId);
        Assert.True(gate.IsObjectiveGate);
        Assert.Equal(GateDoorOpenState.CLOSED, gate.InitialState);

        Assert.Equal("cinix", scenario.TownWgRegionId);
        Assert.Equal(1000, scenario.HubLocation!.Id);
        Assert.Equal("cinix_old", Assert.Single(scenario.Districts).WgRegionId);
    }

    [Fact]
    public async Task RuntimeConfig_CarriesTheGlobalConfiguration()
    {
        var config = await _service.GetRuntimeConfigAsync();

        Assert.Equal(1.5, config.Configuration.HeadshotMultiplier);
        Assert.Equal(new[] { 290, 60, 30, 15 }, config.Configuration.MatchmakingAnnouncementMarks);
        Assert.Equal(1, await _context.SiegeConfigurations.CountAsync());   // seeded on first read
    }

    // ---- Team picker search (Phase 3 verification item 3 depends on the scenario filter) ----

    [Fact]
    public async Task TeamSearch_ScopesToTheScenario_AndResolvesIdentity()
    {
        _context.SiegeTeams.Add(new SiegeTeam { Id = 290, SiegeScenarioId = 101, ClanId = 5 });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
        var scenarios = new SiegeScenarioService(new SiegeScenarioRepository(_context),
            new Moq.Mock<knkwebapi_v2.Services.ILocationService>().Object,
            Array.Empty<knkwebapi_v2.Services.Interfaces.IValidationMethod>(), SiegeTestData.Mapper());

        var page = await scenarios.SearchTeamsAsync(new PagedQueryDto
        {
            PageSize = 50,
            Filters = new() { ["siegeScenarioId"] = "100" }
        });

        Assert.Equal(new[] { 201, 202 }, page.Items.Select(t => t.Id).ToArray());
        Assert.Equal("Cinix Garrison", page.Items[0].ResolvedName);
        Assert.Equal(2, page.TotalCount);
    }

    // ---- CRUD validation ----

    private static SiegeLobbyUpsertDto LobbyDto(string key = "north") => new()
    {
        Name = "Siege — North",
        Key = key,
        IsEnabled = true,
        Rotation = new() { new SiegeLobbyScenarioUpsertDto { SiegeScenarioId = 100, Weight = 2 } }
    };

    [Fact]
    public async Task Create_NormalizesKey_AndStoresRotation()
    {
        var created = await _service.CreateAsync(LobbyDto(" North "));

        Assert.Equal("north", created.Key);
        var entry = Assert.Single(created.Rotation);
        Assert.Equal(100, entry.SiegeScenarioId);
        Assert.Equal("Siege of Cinix", entry.SiegeScenarioName);
        Assert.Equal(2, entry.Weight);
    }

    [Fact]
    public async Task Create_DuplicateKey_IsAConflict()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.CreateAsync(LobbyDto("CINIX")));
    }

    [Theory]
    [InlineData("has space")]
    [InlineData("")]
    [InlineData("ünïcode")]
    public async Task Create_RejectsBadKeys(string key)
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(LobbyDto(key)));
    }

    [Fact]
    public async Task Create_RejectsScheduledMode_UntilItIsBuilt()
    {
        var dto = LobbyDto();
        dto.Mode = SiegeLobbyMode.Scheduled;
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(dto));
    }

    [Theory]
    [InlineData(999, 1)]   // unknown scenario
    [InlineData(100, 0)]   // weight below 1
    public async Task Create_RejectsBadRotationEntries(int scenarioId, int weight)
    {
        var dto = LobbyDto();
        dto.Rotation = new() { new SiegeLobbyScenarioUpsertDto { SiegeScenarioId = scenarioId, Weight = weight } };
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(dto));
    }

    [Fact]
    public async Task Create_AllowsUnreadyScenariosInTheRotation()
    {
        var dto = LobbyDto();
        dto.Rotation!.Add(new SiegeLobbyScenarioUpsertDto { SiegeScenarioId = 101 });
        var created = await _service.CreateAsync(dto);
        Assert.Equal(2, created.Rotation.Count);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public async Task Create_RejectsVoteCandidateCountOutsideOneToThree(int count)
    {
        var dto = LobbyDto();
        dto.VoteCandidateCount = count;
        await Assert.ThrowsAsync<ArgumentException>(() => _service.CreateAsync(dto));
    }

    [Fact]
    public async Task Update_RotationIsReplacedAsASet_NullKeepsIt()
    {
        var dto = LobbyDto("cinix");
        dto.Rotation = null;
        await _service.UpdateAsync(1, dto);
        _context.ChangeTracker.Clear();
        Assert.Equal(2, (await _service.GetByIdAsync(1))!.Rotation.Count);

        dto.Rotation = new() { new SiegeLobbyScenarioUpsertDto { SiegeScenarioId = 100, Weight = 5 } };
        await _service.UpdateAsync(1, dto);
        _context.ChangeTracker.Clear();
        var entry = Assert.Single((await _service.GetByIdAsync(1))!.Rotation);
        Assert.Equal(5, entry.Weight);
    }

    [Fact]
    public async Task Delete_WithMatchHistory_IsRefused()
    {
        _context.SiegeMatches.Add(new SiegeMatch { Id = 1, SiegeLobbyId = 1, SiegeScenarioId = 100 });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() => _service.DeleteAsync(1));
    }

    [Fact]
    public async Task Delete_KeepsTheScenarios()
    {
        await _service.DeleteAsync(2);
        _context.ChangeTracker.Clear();

        Assert.False(await _context.SiegeLobbies.AnyAsync(l => l.Id == 2));
        Assert.True(await _context.SiegeScenarios.AnyAsync(s => s.Id == 100));
    }
}
