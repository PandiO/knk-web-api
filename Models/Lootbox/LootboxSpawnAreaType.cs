using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>Join row: a lootbox type allowed in a spawn area. Composite key; cascades from both sides.</summary>
[FormConfigurableEntity("LootboxSpawnAreaType")]
public class LootboxSpawnAreaType
{
    [NavigationPair(nameof(LootboxSpawnArea))]
    [RelatedEntityField(typeof(LootboxSpawnArea))]
    public int LootboxSpawnAreaId { get; set; }
    [RelatedEntityField(typeof(LootboxSpawnArea))]
    public LootboxSpawnArea LootboxSpawnArea { get; set; } = null!;

    [NavigationPair(nameof(LootboxType))]
    [RelatedEntityField(typeof(LootboxType))]
    public int LootboxTypeId { get; set; }
    [RelatedEntityField(typeof(LootboxType))]
    public LootboxType LootboxType { get; set; } = null!;
}
