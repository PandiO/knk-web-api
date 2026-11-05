using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

// Siege Survival phase 1 (DESIGN §8 "purchase points", §10 "Purchase point", S44; PandiO/knk-workspace#41): a
// fixed equipment purchase point of a scenario, available in every equipment mode, with its offers (an item
// blueprint at a match-currency price). Owned by the scenario (cascade); the Location row is Restrict.
[FormConfigurableEntity("SurvivalPurchasePoint")]
public class SurvivalPurchasePoint
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

    // Blocks from the point within which a player can buy.
    public double InteractionRange { get; set; } = 3;

    [RelatedEntityField(typeof(SurvivalPurchaseOffer))]
    public ICollection<SurvivalPurchaseOffer> Offers { get; set; } = new List<SurvivalPurchaseOffer>();
}

// One product of a purchase point: an item blueprint, a quantity and its price in match currency. Cascades with
// the point; the ItemBlueprint is Restrict (catalog convention).
[FormConfigurableEntity("SurvivalPurchaseOffer")]
public class SurvivalPurchaseOffer
{
    public int Id { get; set; }

    [NavigationPair(nameof(SurvivalPurchasePoint))]
    [RelatedEntityField(typeof(SurvivalPurchasePoint))]
    public int SurvivalPurchasePointId { get; set; }
    [RelatedEntityField(typeof(SurvivalPurchasePoint))]
    public SurvivalPurchasePoint SurvivalPurchasePoint { get; set; } = null!;

    [NavigationPair(nameof(ItemBlueprint))]
    [RelatedEntityField(typeof(ItemBlueprint))]
    public int ItemBlueprintId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint ItemBlueprint { get; set; } = null!;

    public int Quantity { get; set; } = 1;
    public int Price { get; set; } = 100;
    public int SortOrder { get; set; }
}
