using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

// Chronologically-ordered provenance collection for an ItemBlueprint (see docs/specs/items/IMPLEMENTATION_PLAN.md §3.2).
// SequenceNumber convention: 0-based. SequenceNumber = 0 is the production/procurement origin (the only
// entry Phase 1 actually populates); higher numbers are later alteration-history entries, reserved for
// future use. Unlike ItemBlueprintDefaultEnchantment, this has its own Id PK rather than a composite key
// on (ItemBlueprintId, DomainId) — the same Domain can legitimately appear twice in one item's history.
[FormConfigurableEntity("ItemBlueprintOrigin")]
public class ItemBlueprintOrigin
{
    public int Id { get; set; }

    [NavigationPair(nameof(ItemBlueprint))]
    [RelatedEntityField(typeof(ItemBlueprint))]
    public int ItemBlueprintId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint ItemBlueprint { get; set; } = null!;

    [NavigationPair(nameof(Domain))]
    [RelatedEntityField(typeof(Domain))]
    public int DomainId { get; set; }
    [RelatedEntityField(typeof(Domain))]
    public Domain Domain { get; set; } = null!;

    public int SequenceNumber { get; set; }
}
