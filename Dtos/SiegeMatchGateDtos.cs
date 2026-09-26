using System.Text.Json.Serialization;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Dtos
{
    // Siege Phase 7a DTOs (docs/specs/siege-minigame/DESIGN.md §8.1-8.4): the gate lockdown of a
    // match (snapshot → CurrentSiegeId → overrides, one transaction) and its restore.

    // One door's runtime state as the plugin sees it right before the lockdown.
    public class SiegeGateDoorStateDto
    {
        [JsonPropertyName("gateDoorId")] public int GateDoorId { get; set; }
        [JsonPropertyName("openedState")] public GateDoorOpenState OpenedState { get; set; }
        [JsonPropertyName("healthCurrent")] public double HealthCurrent { get; set; }
        [JsonPropertyName("isDestroyed")] public bool IsDestroyed { get; set; }
    }

    // One affected gate structure and the role the plugin gives it in the match (DESIGN §8.1).
    public class SiegeGateLockdownEntryDto
    {
        [JsonPropertyName("gateStructureId")] public int GateStructureId { get; set; }
        // Referenced by an objective (GateStructure.IsSiegeObjective is set for the match).
        [JsonPropertyName("isObjectiveGate")] public bool IsObjectiveGate { get; set; }
        // Selected but not Damageable, or an area gate: IsInvincibleOverride = true.
        [JsonPropertyName("invincible")] public bool Invincible { get; set; }
        // Area gate (not selected): OpenedStateOverride = OPEN for the match.
        [JsonPropertyName("forcedOpen")] public bool ForcedOpen { get; set; }
        // The doors' pre-lockdown runtime state; doors not listed are snapshotted from their DB row.
        [JsonPropertyName("doors")] public List<SiegeGateDoorStateDto> Doors { get; set; } = new();
    }

    public class SiegeGateLockdownDto
    {
        [JsonPropertyName("gates")] public List<SiegeGateLockdownEntryDto> Gates { get; set; } = new();
    }

    // The structure-level values a lockdown changes, as they were before it.
    public class SiegeGateOverridesSnapshotDto
    {
        [JsonPropertyName("isInvincibleOverride")] public bool? IsInvincibleOverride { get; set; }
        [JsonPropertyName("allowPassThroughOverride")] public bool? AllowPassThroughOverride { get; set; }
        [JsonPropertyName("canRespawnOverride")] public bool? CanRespawnOverride { get; set; }
        [JsonPropertyName("openedStateOverride")] public GateDoorOpenState? OpenedStateOverride { get; set; }
        [JsonPropertyName("isSiegeObjective")] public bool IsSiegeObjective { get; set; }
    }

    // SiegeMatchGateSnapshot.SnapshotJson (version 1).
    public class SiegeGateSnapshotContentDto
    {
        [JsonPropertyName("version")] public int Version { get; set; } = 1;
        [JsonPropertyName("structure")] public SiegeGateOverridesSnapshotDto Structure { get; set; } = new();
        [JsonPropertyName("doors")] public List<SiegeGateDoorStateDto> Doors { get; set; } = new();
    }

    public class SiegeGateSnapshotDto
    {
        [JsonPropertyName("siegeMatchId")] public int SiegeMatchId { get; set; }
        [JsonPropertyName("gateStructureId")] public int GateStructureId { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("snapshot")] public SiegeGateSnapshotContentDto Snapshot { get; set; } = new();
    }

    public class SiegeGateRestoreResultDto
    {
        // The snapshots that were re-applied (and deleted), so the plugin can restore the runtime state.
        [JsonPropertyName("restored")] public List<SiegeGateSnapshotDto> Restored { get; set; } = new();
        // Gates whose CurrentSiegeId pointed at a finished match with no snapshot: only the id was cleared.
        [JsonPropertyName("clearedGateStructureIds")] public List<int> ClearedGateStructureIds { get; set; } = new();
    }
}
