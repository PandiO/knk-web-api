using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

// A scenario's change to one of the mode's default shop entries (knk-workspace docs/specs/siege-shop/DESIGN.md
// § 5.3, § 6.1.3, P05; PandiO/knk-workspace#867). Hidden removes the entry from that scenario's shop; every other
// field is nullable and empty keeps the default. Unique per (scenario, entry). Owned by its scenario (cascade) and
// removed with its entry (cascade). Scenario-only entries are SiegeShopEntry rows with a SiegeScenarioId instead.
[FormConfigurableEntity("SiegeScenarioShopOverride")]
public class SiegeScenarioShopOverride
{
    public int Id { get; set; }

    [NavigationPair(nameof(SiegeScenario))]
    [RelatedEntityField(typeof(SiegeScenario))]
    public int SiegeScenarioId { get; set; }
    [RelatedEntityField(typeof(SiegeScenario))]
    public SiegeScenario SiegeScenario { get; set; } = null!;

    [NavigationPair(nameof(SiegeShopEntry))]
    [RelatedEntityField(typeof(SiegeShopEntry))]
    public int SiegeShopEntryId { get; set; }
    [RelatedEntityField(typeof(SiegeShopEntry))]
    public SiegeShopEntry SiegeShopEntry { get; set; } = null!;

    public bool Hidden { get; set; }

    public int? Price { get; set; }
    public int? Quantity { get; set; }
    public int? MaxPerMatch { get; set; }
    public int? CooldownSeconds { get; set; }
    public int? PriceStep { get; set; }
    public int? CooldownStepSeconds { get; set; }
    public int? SortOrder { get; set; }
}
