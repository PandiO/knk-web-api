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
/// Siege Phase 7a (docs/specs/siege-minigame/DESIGN.md §8.2, §8.4): the persisted gate lockdown -
/// snapshot first, then CurrentSiegeId/IsSiegeObjective/overrides; restore re-applies and deletes
/// the snapshot; startup recovery restores leftovers - plus runtime-config's area gate list.
///
/// Gate 400 (selected in scenario 100, district 10) has doors 4001/4002; gate 401 is another gate in
/// district 10 (an area gate); gate 402 is in district 12 of another town (outside the area).
/// </summary>
public class SiegeMatchGateServiceTests : IAsyncLifetime
{
    private KnKDbContext _context = null!;
    private SiegeMatchGateService _gates = null!;
    private SiegeMatchService _matches = null!;

    public async Task InitializeAsync()
    {
        _context = SiegeTestData.NewContext("SiegeGates");
        await SiegeTestData.SeedValidScenarioAsync(_context);

        _context.Towns.Add(new Town { Id = 2, Name = "Elsewhere", Description = "x", WgRegionId = "elsewhere" });
        _context.Districts.Add(new District { Id = 12, Name = "Far", Description = "d", WgRegionId = "far", TownId = 2 });
        _context.GateStructures.Add(new GateStructure { Id = 401, Name = "Side gate", Description = "g", WgRegionId = "side", DistrictId = 10, StreetId = 1 });
        _context.GateStructures.Add(new GateStructure { Id = 402, Name = "Far gate", Description = "g", WgRegionId = "far_gate", DistrictId = 12, StreetId = 1 });
        _context.GateDoors.Add(new GateDoor { Id = 4001, GateStructureId = 400, Name = "Left", HealthCurrent = 500, OpenedState = GateDoorOpenState.CLOSED });
        _context.GateDoors.Add(new GateDoor { Id = 4002, GateStructureId = 400, Name = "Right", HealthCurrent = 300, OpenedState = GateDoorOpenState.CLOSED });
        _context.GateDoors.Add(new GateDoor { Id = 4011, GateStructureId = 401, Name = "Side", HealthCurrent = 500, OpenedState = GateDoorOpenState.CLOSED });
        _context.SiegeLobbies.Add(new SiegeLobby
        {
            Id = 1, Name = "Cinix", Key = "cinix", IsEnabled = true,
            Rotation = { new SiegeLobbyScenario { SiegeScenarioId = 100 } }
        });
        await _context.SaveChangesAsync();

        // Gate 400 had an admin override before the siege; it must come back afterwards.
        var gate400 = await _context.GateStructures.SingleAsync(g => g.Id == 400);
        gate400.IsInvincibleOverride = true;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var repo = new SiegeMatchRepository(_context);
        _gates = new SiegeMatchGateService(repo);
        _matches = new SiegeMatchService(repo, new TitleService(new TitleBracketRepository(_context)));
    }

    public async Task DisposeAsync() => await _context.DisposeAsync();

    private async Task<int> NewMatchAsync()
    {
        var match = await _matches.CreateAsync(new SiegeMatchCreateDto { SiegeLobbyId = 1, SiegeScenarioId = 100 });
        _context.ChangeTracker.Clear();
        return match.Id;
    }

    private static SiegeGateLockdownDto Lockdown() => new()
    {
        Gates =
        {
            new SiegeGateLockdownEntryDto
            {
                GateStructureId = 400, IsObjectiveGate = true, Invincible = false, ForcedOpen = false,
                // The plugin's runtime view: the right door is mid-animation and damaged.
                Doors = { new SiegeGateDoorStateDto { GateDoorId = 4002, OpenedState = GateDoorOpenState.OPENING, HealthCurrent = 250, IsDestroyed = false } }
            },
            new SiegeGateLockdownEntryDto { GateStructureId = 401, Invincible = true, ForcedOpen = true }
        }
    };

    private async Task<GateStructure> GateAsync(int id)
    {
        _context.ChangeTracker.Clear();
        return await _context.GateStructures.Include(g => g.GateDoors).AsNoTracking().SingleAsync(g => g.Id == id);
    }

    [Fact]
    public async Task Lockdown_SnapshotsThePreState_ThenAppliesTheSiegeState()
    {
        var matchId = await NewMatchAsync();

        var snapshots = await _gates.LockdownAsync(matchId, Lockdown());

        Assert.Equal(new[] { 400, 401 }, snapshots.Select(s => s.GateStructureId));
        var s400 = snapshots.Single(s => s.GateStructureId == 400).Snapshot;
        Assert.True(s400.Structure.IsInvincibleOverride);        // the admin override before the siege
        Assert.False(s400.Structure.IsSiegeObjective);
        Assert.Equal(GateDoorOpenState.CLOSED, s400.Doors.Single(d => d.GateDoorId == 4001).OpenedState); // from the DB row
        Assert.Equal(250, s400.Doors.Single(d => d.GateDoorId == 4002).HealthCurrent);                   // from the plugin

        var gate400 = await GateAsync(400);
        Assert.Equal(matchId, gate400.CurrentSiegeId);
        Assert.True(gate400.IsSiegeObjective);
        Assert.False(gate400.IsInvincibleOverride);   // damageable selected gate
        Assert.False(gate400.AllowPassThroughOverride);
        Assert.False(gate400.CanRespawnOverride);
        Assert.Null(gate400.OpenedStateOverride);

        var gate401 = await GateAsync(401);
        Assert.True(gate401.IsInvincibleOverride);
        Assert.Equal(GateDoorOpenState.OPEN, gate401.OpenedStateOverride);
        Assert.Equal(matchId, gate401.CurrentSiegeId);
    }

    [Fact]
    public async Task Lockdown_Repeated_KeepsTheFirstSnapshot()
    {
        var matchId = await NewMatchAsync();
        await _gates.LockdownAsync(matchId, Lockdown());
        _context.ChangeTracker.Clear();

        var again = await _gates.LockdownAsync(matchId, Lockdown());

        Assert.Equal(2, again.Count);
        Assert.True(again.Single(s => s.GateStructureId == 400).Snapshot.Structure.IsInvincibleOverride); // not the siege value
        Assert.Equal(2, await _context.SiegeMatchGateSnapshots.CountAsync());
    }

    [Fact]
    public async Task Lockdown_InvalidInput_OrAGateHeldByAnotherRunningMatch_IsRefused()
    {
        var first = await NewMatchAsync();
        var second = await NewMatchAsync();

        await Assert.ThrowsAsync<ArgumentException>(() => _gates.LockdownAsync(first, new SiegeGateLockdownDto
        { Gates = { new SiegeGateLockdownEntryDto { GateStructureId = 999 } } }));
        await Assert.ThrowsAsync<ArgumentException>(() => _gates.LockdownAsync(first, new SiegeGateLockdownDto
        { Gates = { new SiegeGateLockdownEntryDto { GateStructureId = 400, Doors = { new SiegeGateDoorStateDto { GateDoorId = 4011 } } } } }));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _gates.LockdownAsync(999, Lockdown()));

        await _gates.LockdownAsync(first, Lockdown());
        _context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _gates.LockdownAsync(second, Lockdown()));

        await _matches.AbortAsync(first, new SiegeMatchAbortDto { EndReason = SiegeMatchEndReason.ServerRestart });
        _context.ChangeTracker.Clear();
        await Assert.ThrowsAsync<InvalidOperationException>(() => _gates.LockdownAsync(first, Lockdown())); // finished match
    }

    [Fact]
    public async Task Restore_ReappliesTheSnapshot_DeletesIt_AndIsIdempotent()
    {
        var matchId = await NewMatchAsync();
        await _gates.LockdownAsync(matchId, Lockdown());

        // During the match the right door was destroyed.
        var door = await _context.GateDoors.SingleAsync(d => d.Id == 4002);
        door.IsDestroyed = true;
        door.HealthCurrent = 0;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var result = await _gates.RestoreAsync(matchId);

        Assert.Equal(new[] { 400, 401 }, result.Restored.Select(r => r.GateStructureId).OrderBy(x => x));
        var gate400 = await GateAsync(400);
        Assert.Null(gate400.CurrentSiegeId);
        Assert.False(gate400.IsSiegeObjective);
        Assert.True(gate400.IsInvincibleOverride);  // the admin override is back
        Assert.Null(gate400.AllowPassThroughOverride);
        Assert.Null(gate400.CanRespawnOverride);
        var right = gate400.GateDoors.Single(d => d.Id == 4002);
        Assert.False(right.IsDestroyed);
        Assert.Equal(250, right.HealthCurrent);
        Assert.Equal(GateDoorOpenState.OPEN, right.OpenedState); // OPENING collapses to OPEN
        var gate401 = await GateAsync(401);
        Assert.Null(gate401.IsInvincibleOverride);
        Assert.Null(gate401.OpenedStateOverride);
        Assert.Empty(await _context.SiegeMatchGateSnapshots.ToListAsync());

        _context.ChangeTracker.Clear();
        Assert.Empty((await _gates.RestoreAsync(matchId)).Restored);
        Assert.Empty(await _gates.GetSnapshotsAsync(matchId));
    }

    [Fact]
    public async Task RestoreStale_RestoresLeftoverSnapshots_AndClearsMarkersWithoutOne()
    {
        var matchId = await NewMatchAsync();
        await _gates.LockdownAsync(matchId, Lockdown());
        // A gate marked as in a siege with no snapshot (e.g. written by hand or an old build).
        var far = await _context.GateStructures.SingleAsync(g => g.Id == 402);
        far.CurrentSiegeId = matchId;
        far.IsSiegeObjective = true;
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();

        var result = await _gates.RestoreStaleAsync();

        Assert.Equal(new[] { 400, 401 }, result.Restored.Select(r => r.GateStructureId).OrderBy(x => x));
        Assert.Equal(new[] { 402 }, result.ClearedGateStructureIds);
        Assert.Null((await GateAsync(400)).CurrentSiegeId);
        var gate402 = await GateAsync(402);
        Assert.Null(gate402.CurrentSiegeId);
        Assert.False(gate402.IsSiegeObjective);

        _context.ChangeTracker.Clear();
        var again = await _gates.RestoreStaleAsync();
        Assert.Empty(again.Restored);
        Assert.Empty(again.ClearedGateStructureIds);
    }

    [Fact]
    public async Task RuntimeConfig_ListsTheOtherGatesOfTheScenarioArea()
    {
        var service = new SiegeLobbyService(
            new SiegeLobbyRepository(_context),
            new SiegeScenarioRepository(_context),
            new SiegeConfigurationService(new SiegeConfigurationRepository(_context)),
            SiegeTestData.Mapper());

        var config = await service.GetRuntimeConfigAsync();

        var scenario = config.Lobbies.Single().Rotation.Single().Scenario;
        Assert.Equal(new[] { 401 }, scenario.AreaGateStructureIds); // 400 is selected, 402 is in another town
    }
}
