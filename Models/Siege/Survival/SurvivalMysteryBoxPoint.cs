using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

// Siege Survival phase 1 (DESIGN §8a, §10 "Mystery Box point", S34, S35, S39; PandiO/knk-workspace#41): a Mystery
// Box of a scenario: the block it sits on (default an end portal frame, configurable per point), its pool, the
// roll price (default 1000 match currency) and the claim timeout (default 10 s). Owned by the scenario (cascade);
// its pool is Restrict (delete or swap the point first); the Location row is Restrict.
[FormConfigurableEntity("SurvivalMysteryBoxPoint")]
public class SurvivalMysteryBoxPoint
{
    public int Id { get; set; }

    [NavigationPair(nameof(SiegeScenario))]
    [RelatedEntityField(typeof(SiegeScenario))]
    public int SiegeScenarioId { get; set; }
    [RelatedEntityField(typeof(SiegeScenario))]
    public SiegeScenario SiegeScenario { get; set; } = null!;

    public string Name { get; set; } = null!;
    public int SortOrder { get; set; }

    [NavigationPair(nameof(Location))]
    [RelatedEntityField(typeof(Location))]
    public int LocationId { get; set; }
    [RelatedEntityField(typeof(Location))]
    public Location Location { get; set; } = null!;

    public double InteractionRange { get; set; } = 3;

    [NavigationPair(nameof(SurvivalMysteryBoxPool))]
    [RelatedEntityField(typeof(SurvivalMysteryBoxPool))]
    public int SurvivalMysteryBoxPoolId { get; set; }
    [RelatedEntityField(typeof(SurvivalMysteryBoxPool))]
    public SurvivalMysteryBoxPool SurvivalMysteryBoxPool { get; set; } = null!;

    // Minecraft block key of the box block (S35).
    public string BlockType { get; set; } = "minecraft:end_portal_frame";
    public int RollPrice { get; set; } = 1000;
    public int ClaimTimeoutMillis { get; set; } = 10_000;
}
