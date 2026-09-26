using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

// SiegeScenario <-> District M2M join (docs/specs/siege-minigame/DESIGN.md §3.3), composite key
// like ItemBlueprintDefaultEnchantment. The join row cascades with its scenario; the District is a
// shared world row (Restrict).
[FormConfigurableEntity("SiegeScenarioDistrict")]
public class SiegeScenarioDistrict
{
    [NavigationPair(nameof(SiegeScenario))]
    [RelatedEntityField(typeof(SiegeScenario))]
    public int SiegeScenarioId { get; set; }
    [RelatedEntityField(typeof(SiegeScenario))]
    public SiegeScenario SiegeScenario { get; set; } = null!;

    [NavigationPair(nameof(District))]
    [RelatedEntityField(typeof(District))]
    public int DistrictId { get; set; }
    [RelatedEntityField(typeof(District))]
    public District District { get; set; } = null!;
}
