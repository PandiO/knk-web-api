using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

// Siege Survival phase 1 (DESIGN §7 "Enemy types", S42, S56; PandiO/knk-workspace#41): a vanilla monster type the
// admin selected for the scenario's normal waves, with its draw weight and the first round it can appear in.
// Owned by the scenario (cascade). EntityType is the vanilla entity type key, stored upper case ("ZOMBIE").
[FormConfigurableEntity("SurvivalMonsterType")]
public class SurvivalMonsterType
{
    public int Id { get; set; }

    [NavigationPair(nameof(SiegeScenario))]
    [RelatedEntityField(typeof(SiegeScenario))]
    public int SiegeScenarioId { get; set; }
    [RelatedEntityField(typeof(SiegeScenario))]
    public SiegeScenario SiegeScenario { get; set; } = null!;

    public string EntityType { get; set; } = null!;
    public double Weight { get; set; } = 1;
    public int UnlockRound { get; set; } = 1;
    public int SortOrder { get; set; }
}
