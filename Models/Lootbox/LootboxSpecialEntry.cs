using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>
/// A jackpot item (DESIGN.md §3.1 step 1, §3.5): checked before the normal roll for boxes of at least
/// <see cref="MinBoxStars"/>, each with its own independent <see cref="ChancePerMillion"/> roll in
/// <see cref="SortOrder"/>; the first hit wins and gives the blueprint as-is (default enchantments, no rolled
/// ones). <see cref="LootboxTypeId"/> null = any type. Its blueprint also carries the <c>Lootbox Special</c> tag,
/// which keeps it out of the normal pools.
/// </summary>
[FormConfigurableEntity("LootboxSpecialEntry")]
public class LootboxSpecialEntry
{
    public int Id { get; set; }

    [NavigationPair(nameof(LootboxType))]
    [RelatedEntityField(typeof(LootboxType))]
    public int? LootboxTypeId { get; set; }
    [RelatedEntityField(typeof(LootboxType))]
    public LootboxType? LootboxType { get; set; }

    [NavigationPair(nameof(ItemBlueprint))]
    [RelatedEntityField(typeof(ItemBlueprint))]
    public int ItemBlueprintId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint ItemBlueprint { get; set; } = null!;

    // 0-1,000,000 (2000 = 0.2%).
    public int ChancePerMillion { get; set; }

    public int MinBoxStars { get; set; } = 5;
    public bool Enabled { get; set; } = true;
    public int SortOrder { get; set; }
}
