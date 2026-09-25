using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

// Slot-indexed join entity for Kit.Contents (docs/specs/kits/DESIGN.md §2.2, revised §0a).
// Composite key (KitId, SlotIndex) — NOT (KitId, ItemBlueprintId) — since a scanned kit must
// reproduce a player's inventory layout exactly, including two non-stacking stacks of the same
// ItemBlueprint sitting in two different slots. KitId cascades (a Kit's contents are meaningless
// without their parent Kit); ItemBlueprintId is Restrict — this is the concrete fix for the
// legacy v2 cascade-delete bug (deleting a Kit must never cascade into a shared ItemBlueprint
// row still referenced elsewhere).
[FormConfigurableEntity("KitContent")]
public class KitContent
{
    [NavigationPair(nameof(Kit))]
    [RelatedEntityField(typeof(Kit))]
    public int KitId { get; set; }
    [RelatedEntityField(typeof(Kit))]
    public Kit Kit { get; set; } = null!;

    public int SlotIndex { get; set; } // 0-35, Bukkit PlayerInventory storage index

    [NavigationPair(nameof(ItemBlueprint))]
    [RelatedEntityField(typeof(ItemBlueprint))]
    public int ItemBlueprintId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint ItemBlueprint { get; set; } = null!;

    public int Quantity { get; set; } = 1; // the actual stack size for this slot
}
