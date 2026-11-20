using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// One line of the siege shop (knk-workspace docs/specs/siege-shop/DESIGN.md § 5.4, § 6.1.2, P05, P06, P19;
// PandiO/knk-workspace#867). Kind Item sells an item blueprint in a quantity, kind SiegeWeapon a weapon template.
// SiegeScenarioId null = the mode's default list; set = an entry only that scenario sells. The brakes (price step,
// cooldown, cap) are counted per player, entry and match by the plugin (§ 5.4); this row only stores them.
// Validation lives in SiegeShopService.
[FormConfigurableEntity("SiegeShopEntry")]
public class SiegeShopEntry
{
    public int Id { get; set; }

    public SiegeGameMode GameMode { get; set; } = SiegeGameMode.Siege;

    [NavigationPair(nameof(SiegeScenario))]
    [RelatedEntityField(typeof(SiegeScenario))]
    public int? SiegeScenarioId { get; set; }
    [RelatedEntityField(typeof(SiegeScenario))]
    public SiegeScenario? SiegeScenario { get; set; }

    [NavigationPair(nameof(Category))]
    [RelatedEntityField(typeof(SiegeShopCategory))]
    public int CategoryId { get; set; }
    [RelatedEntityField(typeof(SiegeShopCategory))]
    public SiegeShopCategory Category { get; set; } = null!;

    public SiegeShopEntryKind Kind { get; set; } = SiegeShopEntryKind.Item;

    // Kind Item: the blueprint and how many one purchase gives (P06 "per standard amount").
    [NavigationPair(nameof(ItemBlueprint))]
    [RelatedEntityField(typeof(ItemBlueprint))]
    public int? ItemBlueprintId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint? ItemBlueprint { get; set; }
    public int Quantity { get; set; } = 1;

    // Kind SiegeWeapon: the weapon type; the unlock stays on the template (P20).
    [NavigationPair(nameof(SiegeWeaponTemplate))]
    [RelatedEntityField(typeof(SiegeWeaponTemplate))]
    public int? SiegeWeaponTemplateId { get; set; }
    [RelatedEntityField(typeof(SiegeWeaponTemplate))]
    public SiegeWeaponTemplate? SiegeWeaponTemplate { get; set; }

    // Optional look in the shop; empty = the target's own name and icon.
    public string? DisplayName { get; set; }
    public string? IconMaterial { get; set; }

    // Siege points. Null only for weapons: the template's MatchPrice (P19, A16).
    public int? Price { get; set; }
    public int? PriceStep { get; set; }
    public SiegeShopCurve PriceCurve { get; set; } = SiegeShopCurve.None;
    public double? PriceGrowthFactor { get; set; }   // Exponential only; null = 1.5
    public int? PriceMax { get; set; }

    public int? CooldownSeconds { get; set; }        // null = no cooldown
    public int? CooldownStepSeconds { get; set; }
    public SiegeShopCurve CooldownCurve { get; set; } = SiegeShopCurve.None;
    public double? CooldownGrowthFactor { get; set; } // Exponential only; null = 1.5
    public int? CooldownMaxSeconds { get; set; }

    public int? MaxPerMatch { get; set; }            // null = no cap

    public int SortOrder { get; set; }
    public bool Enabled { get; set; } = true;
}
