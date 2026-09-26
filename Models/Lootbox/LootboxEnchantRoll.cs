using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>
/// One enchantment a type's items may roll (DESIGN.md §3.1 step 4): for boxes of at least
/// <see cref="MinBoxStars"/>, a <see cref="ChancePercent"/> hit gives a level uniform in
/// [<see cref="MinLevel"/>, <see cref="MaxLevel"/>], then clamped by the item grade's cap (vanilla) or the
/// definition max (custom). Evaluated in <see cref="SortOrder"/>.
/// </summary>
[FormConfigurableEntity("LootboxEnchantRoll")]
public class LootboxEnchantRoll
{
    public int Id { get; set; }

    [NavigationPair(nameof(LootboxType))]
    [RelatedEntityField(typeof(LootboxType))]
    public int LootboxTypeId { get; set; }
    [RelatedEntityField(typeof(LootboxType))]
    public LootboxType LootboxType { get; set; } = null!;

    [NavigationPair(nameof(EnchantmentDefinition))]
    [RelatedEntityField(typeof(EnchantmentDefinition))]
    public int EnchantmentDefinitionId { get; set; }
    [RelatedEntityField(typeof(EnchantmentDefinition))]
    public EnchantmentDefinition EnchantmentDefinition { get; set; } = null!;

    // Percent, 0-100 (decimal(7,4)).
    public decimal ChancePercent { get; set; }

    public int MinLevel { get; set; } = 1;
    public int MaxLevel { get; set; } = 1;

    public int MinBoxStars { get; set; } = 1;
    public int SortOrder { get; set; }
}
