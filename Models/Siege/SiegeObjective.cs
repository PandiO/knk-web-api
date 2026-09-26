using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

// A capture objective of a SiegeScenario (docs/specs/siege-minigame/DESIGN.md §3.6). Owned by its
// scenario (cascade). "Main" vs "side" objective is simply InstantVictory true/false.
//
// Location rule: LocationId is required unless GateStructureId is set, in which case the gate
// structure's own Location is the default capture point (fixes v2's optional=false gate FK vs
// location-only objectives). A referenced gate must also be one of the scenario's Gates (§3.9).
[FormConfigurableEntity("SiegeObjective")]
public class SiegeObjective
{
    public int Id { get; set; }

    [NavigationPair(nameof(SiegeScenario))]
    [RelatedEntityField(typeof(SiegeScenario))]
    public int SiegeScenarioId { get; set; }
    [RelatedEntityField(typeof(SiegeScenario))]
    public SiegeScenario SiegeScenario { get; set; } = null!;

    public int SortOrder { get; set; }
    public string Name { get; set; } = null!;

    // Capture point; world-bound. Restrict.
    [NavigationPair(nameof(Location))]
    [RelatedEntityField(typeof(Location))]
    public int? LocationId { get; set; }
    [RelatedEntityField(typeof(Location))]
    public Location? Location { get; set; }

    // D3 objective gate. Restrict: a gate used by an objective can't be deleted.
    [NavigationPair(nameof(GateStructure))]
    [RelatedEntityField(typeof(GateStructure))]
    public int? GateStructureId { get; set; }
    [RelatedEntityField(typeof(GateStructure))]
    public GateStructure? GateStructure { get; set; }

    public int CapturePoints { get; set; } = 500;
    public double CaptureRadius { get; set; } = 2.5;
    public bool InstantVictory { get; set; }

    // null -> the scenario's first Defender team (§7.1). SetNull when that team is deleted, so the
    // default applies again instead of blocking the team's deletion.
    [NavigationPair(nameof(InitialHolderTeam))]
    [RelatedEntityField(typeof(SiegeTeam))]
    public int? InitialHolderTeamId { get; set; }
    [RelatedEntityField(typeof(SiegeTeam))]
    public SiegeTeam? InitialHolderTeam { get; set; }

    // Vision §7.2 "held objectives double as spawnpoints".
    public bool SpawnWhenHeld { get; set; } = true;

    // D3 "default to open" on capture. Only resting states (OPEN/CLOSED) are accepted.
    public GateDoorOpenState GateStateOnCapture { get; set; } = GateDoorOpenState.OPEN;
}
