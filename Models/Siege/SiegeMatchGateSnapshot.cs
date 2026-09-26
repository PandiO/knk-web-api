namespace knkwebapi_v2.Models;

// Pre-lockdown state of one gate structure in a SiegeMatch (docs/specs/siege-minigame/DESIGN.md
// §3.10, §8.2, §8.4): structure overrides + per-door open state/health/destroyed, written before any
// change, so it is the crash-safe restore source (and the non-member view's pre-state, §8.5).
// Runtime row, no [FormConfigurableEntity]. Cascades with its match; the gate is Restrict.
public class SiegeMatchGateSnapshot
{
    public int Id { get; set; }

    public int SiegeMatchId { get; set; }
    public SiegeMatch SiegeMatch { get; set; } = null!;

    public int GateStructureId { get; set; }
    public GateStructure GateStructure { get; set; } = null!;

    public string SnapshotJson { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
