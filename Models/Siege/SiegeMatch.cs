using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// One siege match (docs/specs/siege-minigame/DESIGN.md §3.10) - runtime/history row written by the
// plugin through the match endpoints (Phase 6), so no [FormConfigurableEntity] (append-only,
// AuditLogEntry/KitClaim convention). Neither legacy version persisted matches.
//
// Lobby and scenario are Restrict: a lobby or scenario with match history can't be deleted (disable
// it / drop it from the rotation instead). GateStructure.CurrentSiegeId points here (SetNull).
public class SiegeMatch
{
    public int Id { get; set; }

    public int SiegeLobbyId { get; set; }
    public SiegeLobby SiegeLobby { get; set; } = null!;

    public int SiegeScenarioId { get; set; }
    public SiegeScenario SiegeScenario { get; set; } = null!;

    public SiegeMatchStatus Status { get; set; } = SiegeMatchStatus.Created;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }

    public SiegeMatchEndReason? EndReason { get; set; }

    // null = draw or aborted.
    public int? WinningAllianceGroup { get; set; }

    // Owned history rows - cascade with the match.
    public ICollection<SiegeMatchParticipant> Participants { get; set; } = new List<SiegeMatchParticipant>();
    public ICollection<SiegeMatchObjectiveResult> ObjectiveResults { get; set; } = new List<SiegeMatchObjectiveResult>();
    public ICollection<SiegeMatchGateSnapshot> GateSnapshots { get; set; } = new List<SiegeMatchGateSnapshot>();
}
