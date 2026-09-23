using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

[FormConfigurableEntity("ItemBlueprintTag")]
public class ItemBlueprintTag
{
    // Composite primary key (ItemBlueprint + Tag combo)
    [NavigationPair(nameof(ItemBlueprint))]
    [RelatedEntityField(typeof(ItemBlueprint))]
    public int ItemBlueprintId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint ItemBlueprint { get; set; } = null!;

    [NavigationPair(nameof(Tag))]
    [RelatedEntityField(typeof(Tag))]
    public int TagId { get; set; }
    [RelatedEntityField(typeof(Tag))]
    public Tag Tag { get; set; } = null!;
}
