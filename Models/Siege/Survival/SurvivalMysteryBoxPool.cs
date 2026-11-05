using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// Siege Survival phase 1 (DESIGN §8a, S24, S59; PandiO/knk-workspace#41): a Mystery Box pool of a scenario, for
// one variant. Items: entries are item blueprints with optional per-entry enchantment chances; enchanted books:
// entries are an enchantment and level. Every entry has a percentage chance; readiness requires the chances to
// add up to exactly 100 % (stored at 0.01 % precision, so the check is exact in basis points). Owned by the
// scenario (cascade); a pool a Mystery Box point still uses can't be deleted (Restrict).
[FormConfigurableEntity("SurvivalMysteryBoxPool")]
public class SurvivalMysteryBoxPool
{
    public int Id { get; set; }

    [NavigationPair(nameof(SiegeScenario))]
    [RelatedEntityField(typeof(SiegeScenario))]
    public int SiegeScenarioId { get; set; }
    [RelatedEntityField(typeof(SiegeScenario))]
    public SiegeScenario SiegeScenario { get; set; } = null!;

    public string Name { get; set; } = null!;
    public SurvivalMysteryBoxVariant Variant { get; set; } = SurvivalMysteryBoxVariant.Items;

    [RelatedEntityField(typeof(SurvivalMysteryBoxEntry))]
    public ICollection<SurvivalMysteryBoxEntry> Entries { get; set; } = new List<SurvivalMysteryBoxEntry>();
}

// One entry of a pool. Items variant: ItemBlueprintId (+ optional enchantment chances); enchanted-books
// variant: EnchantmentDefinitionId + EnchantmentLevel. ChancePercent at 0.01 % precision (1 % = 100 bp).
// Cascades with the pool; the catalog rows are Restrict.
[FormConfigurableEntity("SurvivalMysteryBoxEntry")]
public class SurvivalMysteryBoxEntry
{
    public int Id { get; set; }

    [NavigationPair(nameof(SurvivalMysteryBoxPool))]
    [RelatedEntityField(typeof(SurvivalMysteryBoxPool))]
    public int SurvivalMysteryBoxPoolId { get; set; }
    [RelatedEntityField(typeof(SurvivalMysteryBoxPool))]
    public SurvivalMysteryBoxPool SurvivalMysteryBoxPool { get; set; } = null!;

    public int SortOrder { get; set; }

    [NavigationPair(nameof(ItemBlueprint))]
    [RelatedEntityField(typeof(ItemBlueprint))]
    public int? ItemBlueprintId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint? ItemBlueprint { get; set; }

    [NavigationPair(nameof(EnchantmentDefinition))]
    [RelatedEntityField(typeof(EnchantmentDefinition))]
    public int? EnchantmentDefinitionId { get; set; }
    [RelatedEntityField(typeof(EnchantmentDefinition))]
    public EnchantmentDefinition? EnchantmentDefinition { get; set; }
    public int? EnchantmentLevel { get; set; }

    public decimal ChancePercent { get; set; } = 10;

    [RelatedEntityField(typeof(SurvivalMysteryBoxEntryEnchantment))]
    public ICollection<SurvivalMysteryBoxEntryEnchantment> Enchantments { get; set; } = new List<SurvivalMysteryBoxEntryEnchantment>();
}

// An optional enchantment of an item entry with its own independent chance (rolled on its own, so the chances of
// one entry need not add up). Cascades with the entry; the EnchantmentDefinition is Restrict.
[FormConfigurableEntity("SurvivalMysteryBoxEntryEnchantment")]
public class SurvivalMysteryBoxEntryEnchantment
{
    public int Id { get; set; }

    [NavigationPair(nameof(SurvivalMysteryBoxEntry))]
    [RelatedEntityField(typeof(SurvivalMysteryBoxEntry))]
    public int SurvivalMysteryBoxEntryId { get; set; }
    [RelatedEntityField(typeof(SurvivalMysteryBoxEntry))]
    public SurvivalMysteryBoxEntry SurvivalMysteryBoxEntry { get; set; } = null!;

    [NavigationPair(nameof(EnchantmentDefinition))]
    [RelatedEntityField(typeof(EnchantmentDefinition))]
    public int EnchantmentDefinitionId { get; set; }
    [RelatedEntityField(typeof(EnchantmentDefinition))]
    public EnchantmentDefinition EnchantmentDefinition { get; set; } = null!;

    public int Level { get; set; } = 1;
    public decimal ChancePercent { get; set; } = 25;
}
