using Microsoft.EntityFrameworkCore;
using Moq;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Siege Phase 2 (docs/specs/siege-minigame/IMPLEMENTATION_PLAN.md Phase 2) delete behaviour, per
/// DESIGN §3: owned children cascade from their owner; shared world/catalog rows (Town, District,
/// Location, GateStructure, Clan, BannerDesign, TitleBracket, User) are never deleted through a siege
/// row, and deleting one that a siege row still uses is refused. The EF model's delete rules are
/// asserted directly (they become the migration's ON DELETE clauses); the behavioural tests run the
/// real services and repositories on InMemory.
/// </summary>
public class SiegeDeleteBehaviourTests : IAsyncLifetime
{
    private KnKDbContext _context = null!;

    public async Task InitializeAsync()
    {
        _context = SiegeTestData.NewContext("SiegeDelete");
        await SiegeTestData.SeedValidScenarioAsync(_context);
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    private SiegeScenarioService ScenarioService() => new(
        new SiegeScenarioRepository(_context), new Mock<ILocationService>().Object,
        Array.Empty<IValidationMethod>(), SiegeTestData.Mapper());

    // ---- Model delete rules ----

    public static IEnumerable<object[]> ExpectedDeleteRules() => new List<object[]>
    {
        // Owned children cascade
        new object[] { typeof(SiegeTeam), nameof(SiegeTeam.SiegeScenarioId), DeleteBehavior.Cascade },
        new object[] { typeof(SiegeSpawnpoint), nameof(SiegeSpawnpoint.SiegeTeamId), DeleteBehavior.Cascade },
        new object[] { typeof(SiegeObjective), nameof(SiegeObjective.SiegeScenarioId), DeleteBehavior.Cascade },
        new object[] { typeof(SiegeScenarioGate), nameof(SiegeScenarioGate.SiegeScenarioId), DeleteBehavior.Cascade },
        new object[] { typeof(SiegeScenarioDistrict), nameof(SiegeScenarioDistrict.SiegeScenarioId), DeleteBehavior.Cascade },
        new object[] { typeof(SiegeLobbyScenario), nameof(SiegeLobbyScenario.SiegeLobbyId), DeleteBehavior.Cascade },
        new object[] { typeof(SiegeLobbyScenario), nameof(SiegeLobbyScenario.SiegeScenarioId), DeleteBehavior.Cascade },
        new object[] { typeof(SiegeMatchParticipant), nameof(SiegeMatchParticipant.SiegeMatchId), DeleteBehavior.Cascade },
        new object[] { typeof(SiegeMatchObjectiveResult), nameof(SiegeMatchObjectiveResult.SiegeMatchId), DeleteBehavior.Cascade },
        new object[] { typeof(SiegeMatchGateSnapshot), nameof(SiegeMatchGateSnapshot.SiegeMatchId), DeleteBehavior.Cascade },
        // Shared rows are Restrict
        new object[] { typeof(SiegeScenario), nameof(SiegeScenario.TownId), DeleteBehavior.Restrict },
        new object[] { typeof(SiegeScenario), nameof(SiegeScenario.HubLocationId), DeleteBehavior.Restrict },
        new object[] { typeof(SiegeScenario), nameof(SiegeScenario.MinTitleBracketId), DeleteBehavior.Restrict },
        new object[] { typeof(SiegeScenarioDistrict), nameof(SiegeScenarioDistrict.DistrictId), DeleteBehavior.Restrict },
        new object[] { typeof(SiegeScenarioGate), nameof(SiegeScenarioGate.GateStructureId), DeleteBehavior.Restrict },
        new object[] { typeof(SiegeTeam), nameof(SiegeTeam.ClanId), DeleteBehavior.Restrict },
        new object[] { typeof(SiegeTeam), nameof(SiegeTeam.BannerDesignId), DeleteBehavior.Restrict },
        new object[] { typeof(SiegeSpawnpoint), nameof(SiegeSpawnpoint.LocationId), DeleteBehavior.Restrict },
        new object[] { typeof(SiegeObjective), nameof(SiegeObjective.LocationId), DeleteBehavior.Restrict },
        new object[] { typeof(SiegeObjective), nameof(SiegeObjective.GateStructureId), DeleteBehavior.Restrict },
        new object[] { typeof(SiegeMatchParticipant), nameof(SiegeMatchParticipant.UserId), DeleteBehavior.Restrict },
        new object[] { typeof(SiegeMatchObjectiveResult), nameof(SiegeMatchObjectiveResult.CapturedByUserId), DeleteBehavior.Restrict },
        new object[] { typeof(SiegeMatchGateSnapshot), nameof(SiegeMatchGateSnapshot.GateStructureId), DeleteBehavior.Restrict },
        // Match history pins its lobby/scenario
        new object[] { typeof(SiegeMatch), nameof(SiegeMatch.SiegeLobbyId), DeleteBehavior.Restrict },
        new object[] { typeof(SiegeMatch), nameof(SiegeMatch.SiegeScenarioId), DeleteBehavior.Restrict },
        // Team references from sibling rows fall back to the "first Defender" default
        new object[] { typeof(SiegeObjective), nameof(SiegeObjective.InitialHolderTeamId), DeleteBehavior.SetNull },
        new object[] { typeof(SiegeScenarioGate), nameof(SiegeScenarioGate.InitialOwnerTeamId), DeleteBehavior.SetNull },
        new object[] { typeof(SiegeMatchParticipant), nameof(SiegeMatchParticipant.SiegeTeamId), DeleteBehavior.SetNull },
        new object[] { typeof(SiegeMatchObjectiveResult), nameof(SiegeMatchObjectiveResult.SiegeObjectiveId), DeleteBehavior.SetNull },
        new object[] { typeof(SiegeMatchObjectiveResult), nameof(SiegeMatchObjectiveResult.FinalHolderTeamId), DeleteBehavior.SetNull },
        // DESIGN §3.10: the gate's CurrentSiegeId placeholder is now a real FK to SiegeMatch
        new object[] { typeof(GateStructure), nameof(GateStructure.CurrentSiegeId), DeleteBehavior.SetNull },
    };

    [Theory]
    [MemberData(nameof(ExpectedDeleteRules))]
    public void Model_DeleteRule(Type entity, string foreignKeyProperty, DeleteBehavior expected)
    {
        var fk = _context.Model.FindEntityType(entity)!.GetForeignKeys()
            .Single(f => f.Properties.Count == 1 && f.Properties[0].Name == foreignKeyProperty);
        Assert.Equal(expected, fk.DeleteBehavior);
    }

    [Fact]
    public void Model_CurrentSiegeId_PointsAtSiegeMatch()
    {
        var fk = _context.Model.FindEntityType(typeof(GateStructure))!.GetForeignKeys()
            .Single(f => f.Properties[0].Name == nameof(GateStructure.CurrentSiegeId));
        Assert.Equal(typeof(SiegeMatch), fk.PrincipalEntityType.ClrType);
    }

    // ---- Behaviour ----

    [Fact]
    public async Task DeletingAScenario_RemovesItsOwnedRows_AndLeavesSharedRowsIntact()
    {
        await ScenarioService().DeleteAsync(100);
        _context.ChangeTracker.Clear();

        Assert.Equal(0, await _context.SiegeScenarios.CountAsync());
        Assert.Equal(0, await _context.SiegeTeams.CountAsync());
        Assert.Equal(0, await _context.SiegeSpawnpoints.CountAsync());
        Assert.Equal(0, await _context.SiegeObjectives.CountAsync());
        Assert.Equal(0, await _context.SiegeScenarioGates.CountAsync());
        Assert.Equal(0, await _context.SiegeScenarioDistricts.CountAsync());

        Assert.True(await _context.GateStructures.AnyAsync(g => g.Id == 400));
        Assert.Equal(5, await _context.Locations.CountAsync());
        Assert.True(await _context.Clans.AnyAsync(c => c.Id == 5));
        Assert.True(await _context.Towns.AnyAsync(t => t.Id == 1));
        Assert.True(await _context.Districts.AnyAsync(d => d.Id == 10));
        Assert.Equal(2, await _context.BannerDesigns.CountAsync());
    }

    [Fact]
    public async Task DeletingATeam_RemovesItsSpawnpoints_AndResetsHolderAndOwnerToTheDefault()
    {
        var objective = await _context.SiegeObjectives.SingleAsync(o => o.Id == 501);
        objective.InitialHolderTeamId = 202;
        var gate = await _context.SiegeScenarioGates.SingleAsync();
        gate.InitialOwnerTeamId = 202;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        await ScenarioService().DeleteTeamAsync(202);
        _context.ChangeTracker.Clear();

        Assert.False(await _context.SiegeSpawnpoints.AnyAsync(p => p.SiegeTeamId == 202));
        Assert.Null((await _context.SiegeObjectives.SingleAsync(o => o.Id == 501)).InitialHolderTeamId);
        Assert.Null((await _context.SiegeScenarioGates.SingleAsync()).InitialOwnerTeamId);
        Assert.Equal(1002, (await _context.Locations.SingleAsync(l => l.Id == 1002)).Id);   // spawn location kept
    }

    [Fact]
    public async Task DeletingAGateUsedByAScenario_IsRefused()
    {
        var service = new GateStructureService(
            new GateStructureRepository(_context), new Mock<ILocationRepository>().Object,
            new Mock<ILocationService>().Object, SiegeTestData.Mapper());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(400));
        Assert.Contains("siege scenario", ex.Message);
        Assert.True(await _context.GateStructures.AnyAsync(g => g.Id == 400));
    }

    [Fact]
    public async Task DeletingAGate_NoLongerUsed_StillWorksAfterTheScenarioIsGone()
    {
        await ScenarioService().DeleteAsync(100);
        _context.ChangeTracker.Clear();
        var service = new GateStructureService(
            new GateStructureRepository(_context), new Mock<ILocationRepository>().Object,
            new Mock<ILocationService>().Object, SiegeTestData.Mapper());

        await service.DeleteAsync(400);
        _context.ChangeTracker.Clear();

        Assert.False(await _context.GateStructures.AnyAsync(g => g.Id == 400));
    }

    [Fact]
    public async Task DeletingAClanUsedByATeam_IsRefused()
    {
        var service = new ClanService(new ClanRepository(_context), SiegeTestData.Mapper());

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(5));
        Assert.True(await _context.Clans.AnyAsync(c => c.Id == 5));
    }

    [Fact]
    public async Task DeletingABannerUsedByATeam_IsRefused()
    {
        // Banner 51 belongs to the ad-hoc team only (no clan uses it).
        var service = new BannerDesignService(new BannerDesignRepository(_context), SiegeTestData.Mapper());

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => service.DeleteAsync(51));
        Assert.Contains("siege team", ex.Message);
    }
}
