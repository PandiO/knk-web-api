using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

// An admin-selected gate of a SiegeScenario (docs/specs/siege-minigame/DESIGN.md §3.7, D3): M2M
// join between SiegeScenario and GateStructure with join fields, composite key. Selected gates are
// active, damageable and opened/closed by their owning team's alliance; every other gate in the
// scenario area is forced open for the match. A gate is an objective gate when some
// SiegeObjective.GateStructureId references it.
[FormConfigurableEntity("SiegeScenarioGate")]
public class SiegeScenarioGate
{
    [NavigationPair(nameof(SiegeScenario))]
    [RelatedEntityField(typeof(SiegeScenario))]
    public int SiegeScenarioId { get; set; }
    [RelatedEntityField(typeof(SiegeScenario))]
    public SiegeScenario SiegeScenario { get; set; } = null!;

    // Restrict: a gate selected by a scenario can't be deleted.
    [NavigationPair(nameof(GateStructure))]
    [RelatedEntityField(typeof(GateStructure))]
    public int GateStructureId { get; set; }
    [RelatedEntityField(typeof(GateStructure))]
    public GateStructure GateStructure { get; set; } = null!;

    // null -> the scenario's first Defender team. SetNull when that team is deleted.
    [NavigationPair(nameof(InitialOwnerTeam))]
    [RelatedEntityField(typeof(SiegeTeam))]
    public int? InitialOwnerTeamId { get; set; }
    [RelatedEntityField(typeof(SiegeTeam))]
    public SiegeTeam? InitialOwnerTeam { get; set; }

    // State forced at match start. Only resting states (OPEN/CLOSED) are accepted.
    public GateDoorOpenState InitialState { get; set; } = GateDoorOpenState.CLOSED;

    // Enemies of the owner can damage/destroy it; destroyed gates stay destroyed until the match ends.
    public bool Damageable { get; set; } = true;
}
