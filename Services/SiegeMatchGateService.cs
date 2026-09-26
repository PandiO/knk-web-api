using System.Text.Json;
using System.Text.Json.Serialization;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Siege Phase 7a (docs/specs/siege-minigame/DESIGN.md §8.2, §8.4): the persisted half of a
    /// match's gate lockdown. The plugin decides each gate's role (§8.1) and drives the runtime
    /// state; this service makes the database side crash-safe in one SaveChanges per call:
    /// <list type="bullet">
    /// <item><b>Lockdown</b>: a SiegeMatchGateSnapshot per gate (the structure values it changes +
    /// each door's open state/health/destroyed) is written first, then CurrentSiegeId,
    /// IsSiegeObjective and the overrides (AllowPassThrough = false, CanRespawn = false,
    /// IsInvincible per role, OpenedState = OPEN for area gates). Repeating it keeps the first
    /// snapshot (the real pre-state) and re-applies the overrides.</item>
    /// <item><b>Restore</b>: every snapshot of the match is re-applied (overrides, IsSiegeObjective,
    /// door rows), CurrentSiegeId cleared, the rows deleted; returns them so the plugin can restore
    /// the runtime state. Repeating it is a no-op.</item>
    /// <item><b>Restore stale</b>: the startup recovery - restores every snapshot left behind and
    /// clears CurrentSiegeId on gates whose match has none.</item>
    /// </list>
    /// </summary>
    public class SiegeMatchGateService : ISiegeMatchGateService
    {
        private static readonly JsonSerializerOptions Json = new()
        {
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly ISiegeMatchRepository _repo;
        private readonly ILogger<SiegeMatchGateService>? _logger;

        public SiegeMatchGateService(ISiegeMatchRepository repo, ILogger<SiegeMatchGateService>? logger = null)
        {
            _repo = repo;
            _logger = logger;
        }

        public async Task<List<SiegeGateSnapshotDto>> GetSnapshotsAsync(int siegeMatchId)
        {
            if (await _repo.GetByIdAsync(siegeMatchId, includeUsers: false) == null)
                throw new KeyNotFoundException($"SiegeMatch {siegeMatchId} not found.");
            return (await _repo.GetGateSnapshotsAsync(siegeMatchId)).Select(ToDto).ToList();
        }

        public async Task<List<SiegeGateSnapshotDto>> LockdownAsync(int siegeMatchId, SiegeGateLockdownDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            return await _repo.RunLockedAsync(siegeMatchId, async () =>
            {
                var match = await _repo.GetByIdAsync(siegeMatchId, includeUsers: false)
                    ?? throw new KeyNotFoundException($"SiegeMatch {siegeMatchId} not found.");
                if (match.Status == SiegeMatchStatus.Completed || match.Status == SiegeMatchStatus.Aborted)
                    throw new InvalidOperationException($"SiegeMatch {siegeMatchId} is already {match.Status}.");

                var entries = (dto.Gates ?? new List<SiegeGateLockdownEntryDto>())
                    .GroupBy(g => g.GateStructureId).Select(g => g.Last()).ToList();
                var gates = (await _repo.GetGateStructuresWithDoorsAsync(entries.Select(e => e.GateStructureId))).ToDictionary(g => g.Id);
                var missing = entries.Select(e => e.GateStructureId).Where(id => !gates.ContainsKey(id)).ToList();
                if (missing.Count > 0)
                    throw new ArgumentException($"GateStructure(s) {string.Join(", ", missing)} do not exist.");

                // A gate still held by another running match can't be locked down twice.
                var others = gates.Values.Where(g => g.CurrentSiegeId.HasValue && g.CurrentSiegeId != siegeMatchId)
                    .Select(g => g.CurrentSiegeId!.Value).Distinct().ToList();
                var otherStatuses = await _repo.GetMatchStatusesAsync(others);
                foreach (var gate in gates.Values.Where(g => g.CurrentSiegeId.HasValue && g.CurrentSiegeId != siegeMatchId))
                {
                    if (otherStatuses.TryGetValue(gate.CurrentSiegeId!.Value, out var status)
                        && (status == SiegeMatchStatus.Created || status == SiegeMatchStatus.InProgress))
                        throw new InvalidOperationException(
                            $"GateStructure {gate.Id} is locked down by SiegeMatch {gate.CurrentSiegeId}, which is still {status}.");
                    _logger?.LogWarning("GateStructure {GateId} still pointed at finished SiegeMatch {Other}; taking its current state as the pre-lockdown state",
                        gate.Id, gate.CurrentSiegeId);
                }

                var existing = (await _repo.GetGateSnapshotsAsync(siegeMatchId)).ToDictionary(s => s.GateStructureId);
                foreach (var entry in entries)
                {
                    var gate = gates[entry.GateStructureId];
                    var doorIds = gate.GateDoors.Select(d => d.Id).ToHashSet();
                    var foreign = (entry.Doors ?? new List<SiegeGateDoorStateDto>()).Select(d => d.GateDoorId).Where(id => !doorIds.Contains(id)).ToList();
                    if (foreign.Count > 0)
                        throw new ArgumentException($"GateDoor(s) {string.Join(", ", foreign)} do not belong to GateStructure {gate.Id}.");

                    if (!existing.ContainsKey(gate.Id))
                    {
                        var snapshot = new SiegeMatchGateSnapshot
                        {
                            SiegeMatchId = siegeMatchId,
                            GateStructureId = gate.Id,
                            SnapshotJson = JsonSerializer.Serialize(BuildSnapshot(gate, entry), Json),
                            CreatedAt = DateTime.UtcNow
                        };
                        _repo.AddGateSnapshot(snapshot);
                        existing[gate.Id] = snapshot;
                    }

                    gate.CurrentSiegeId = siegeMatchId;
                    gate.IsSiegeObjective = entry.IsObjectiveGate;
                    gate.AllowPassThroughOverride = false;
                    gate.CanRespawnOverride = false;
                    gate.IsInvincibleOverride = entry.Invincible;
                    // Area gates stay open for the match; selected gates follow their doors' own state.
                    gate.OpenedStateOverride = entry.ForcedOpen ? GateDoorOpenState.OPEN : null;
                }

                await _repo.SaveChangesAsync();
                return existing.Values.OrderBy(s => s.GateStructureId).Select(ToDto).ToList();
            });
        }

        public async Task<SiegeGateRestoreResultDto> RestoreAsync(int siegeMatchId)
        {
            return await _repo.RunLockedAsync(siegeMatchId, async () =>
            {
                if (await _repo.GetByIdAsync(siegeMatchId, includeUsers: false) == null)
                    throw new KeyNotFoundException($"SiegeMatch {siegeMatchId} not found.");
                var result = await RestoreSnapshotsAsync(await _repo.GetGateSnapshotsAsync(siegeMatchId));
                await _repo.SaveChangesAsync();
                return result;
            });
        }

        public async Task<SiegeGateRestoreResultDto> RestoreStaleAsync()
        {
            var result = await RestoreSnapshotsAsync(await _repo.GetGateSnapshotsAsync(null));

            // Gates still marked as in a siege that has no snapshot left: nothing to re-apply, so
            // only the siege marker is cleared (IsSiegeObjective is runtime-maintained, DESIGN §8.5).
            // (The query runs against the database, where the restores above aren't saved yet, so
            // re-check in memory and skip the gates just restored.)
            var restoredIds = result.Restored.Select(r => r.GateStructureId).ToHashSet();
            foreach (var gate in (await _repo.GetGatesInSiegeAsync()).Where(g => g.CurrentSiegeId != null && !restoredIds.Contains(g.Id)))
            {
                gate.CurrentSiegeId = null;
                gate.IsSiegeObjective = false;
                result.ClearedGateStructureIds.Add(gate.Id);
            }

            await _repo.SaveChangesAsync();
            if (result.Restored.Count > 0 || result.ClearedGateStructureIds.Count > 0)
            {
                _logger?.LogWarning("Siege gate recovery restored {Restored} gate snapshot(s) and cleared {Cleared} stale siege marker(s)",
                    result.Restored.Count, result.ClearedGateStructureIds.Count);
            }
            return result;
        }

        // ===== internals =====

        private async Task<SiegeGateRestoreResultDto> RestoreSnapshotsAsync(List<SiegeMatchGateSnapshot> snapshots)
        {
            var result = new SiegeGateRestoreResultDto();
            if (snapshots.Count == 0) return result;

            var gates = (await _repo.GetGateStructuresWithDoorsAsync(snapshots.Select(s => s.GateStructureId))).ToDictionary(g => g.Id);
            foreach (var row in snapshots)
            {
                var content = Parse(row);
                if (gates.TryGetValue(row.GateStructureId, out var gate))
                {
                    gate.IsInvincibleOverride = content.Structure.IsInvincibleOverride;
                    gate.AllowPassThroughOverride = content.Structure.AllowPassThroughOverride;
                    gate.CanRespawnOverride = content.Structure.CanRespawnOverride;
                    gate.OpenedStateOverride = content.Structure.OpenedStateOverride;
                    gate.IsSiegeObjective = content.Structure.IsSiegeObjective;
                    if (gate.CurrentSiegeId == row.SiegeMatchId) gate.CurrentSiegeId = null;

                    var doors = gate.GateDoors.ToDictionary(d => d.Id);
                    foreach (var door in content.Doors)
                    {
                        if (!doors.TryGetValue(door.GateDoorId, out var target)) continue;
                        target.OpenedState = Resting(door.OpenedState);
                        target.HealthCurrent = door.HealthCurrent;
                        target.IsDestroyed = door.IsDestroyed;
                    }
                }
                result.Restored.Add(ToDto(row, content));
            }
            _repo.RemoveGateSnapshots(snapshots);
            return result;
        }

        private static SiegeGateSnapshotContentDto BuildSnapshot(GateStructure gate, SiegeGateLockdownEntryDto entry)
        {
            var supplied = (entry.Doors ?? new List<SiegeGateDoorStateDto>()).GroupBy(d => d.GateDoorId).ToDictionary(g => g.Key, g => g.Last());
            return new SiegeGateSnapshotContentDto
            {
                Structure = new SiegeGateOverridesSnapshotDto
                {
                    IsInvincibleOverride = gate.IsInvincibleOverride,
                    AllowPassThroughOverride = gate.AllowPassThroughOverride,
                    CanRespawnOverride = gate.CanRespawnOverride,
                    OpenedStateOverride = gate.OpenedStateOverride,
                    IsSiegeObjective = gate.IsSiegeObjective
                },
                Doors = gate.GateDoors.OrderBy(d => d.Id).Select(d => supplied.TryGetValue(d.Id, out var s)
                    ? new SiegeGateDoorStateDto { GateDoorId = d.Id, OpenedState = s.OpenedState, HealthCurrent = s.HealthCurrent, IsDestroyed = s.IsDestroyed }
                    : new SiegeGateDoorStateDto { GateDoorId = d.Id, OpenedState = d.OpenedState, HealthCurrent = d.HealthCurrent, IsDestroyed = d.IsDestroyed })
                    .ToList()
            };
        }

        // A transient state collapses to the resting state it was heading to (DESIGN §8.5).
        private static GateDoorOpenState Resting(GateDoorOpenState state) => state switch
        {
            GateDoorOpenState.OPENING => GateDoorOpenState.OPEN,
            GateDoorOpenState.CLOSING or GateDoorOpenState.JAMMED => GateDoorOpenState.CLOSED,
            _ => state
        };

        private static SiegeGateSnapshotContentDto Parse(SiegeMatchGateSnapshot row)
        {
            try
            {
                return JsonSerializer.Deserialize<SiegeGateSnapshotContentDto>(row.SnapshotJson, Json) ?? new SiegeGateSnapshotContentDto();
            }
            catch (JsonException)
            {
                return new SiegeGateSnapshotContentDto();
            }
        }

        private static SiegeGateSnapshotDto ToDto(SiegeMatchGateSnapshot row) => ToDto(row, Parse(row));

        private static SiegeGateSnapshotDto ToDto(SiegeMatchGateSnapshot row, SiegeGateSnapshotContentDto content) => new()
        {
            SiegeMatchId = row.SiegeMatchId,
            GateStructureId = row.GateStructureId,
            CreatedAt = row.CreatedAt,
            Snapshot = content
        };
    }
}
