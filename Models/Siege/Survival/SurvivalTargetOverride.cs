using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// Siege Survival phase 1 (DESIGN §4 "Objective and gate eligibility overrides", §10; PandiO/knk-workspace#41): the
// NPC targeting override of one objective or one gate of the scenario: follow standard rules, explicitly allow or
// explicitly deny. Exactly one of SiegeObjectiveId / GateStructureId is set; at most one row per target and
// scenario. A target without a row follows the standard rules. Owned by the scenario; cascades with the scenario
// and with its target (an override is meaningless without it).
[FormConfigurableEntity("SurvivalTargetOverride")]
public class SurvivalTargetOverride
{
    public int Id { get; set; }

    [NavigationPair(nameof(SiegeScenario))]
    [RelatedEntityField(typeof(SiegeScenario))]
    public int SiegeScenarioId { get; set; }
    [RelatedEntityField(typeof(SiegeScenario))]
    public SiegeScenario SiegeScenario { get; set; } = null!;

    [NavigationPair(nameof(SiegeObjective))]
    [RelatedEntityField(typeof(SiegeObjective))]
    public int? SiegeObjectiveId { get; set; }
    [RelatedEntityField(typeof(SiegeObjective))]
    public SiegeObjective? SiegeObjective { get; set; }

    [NavigationPair(nameof(GateStructure))]
    [RelatedEntityField(typeof(GateStructure))]
    public int? GateStructureId { get; set; }
    [RelatedEntityField(typeof(GateStructure))]
    public GateStructure? GateStructure { get; set; }

    public SurvivalTargetOverrideMode Mode { get; set; } = SurvivalTargetOverrideMode.Standard;
}
