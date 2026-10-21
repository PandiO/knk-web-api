using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>
/// A kit duelists may use in an Arena's kit modes (arena duels DESIGN.md §4.1, §8.4). M2M join, composite key
/// (ArenaId, KitId); SequenceNumber orders the list (named like ItemBlueprintOrigin's, so the FormWizard's
/// many-to-many step offers drag-to-reorder), the first is the default pick in KIT_CHOICE. A kit in use here
/// can't be deleted (Restrict).
/// </summary>
[FormConfigurableEntity("ArenaKit")]
public class ArenaKit
{
    [NavigationPair(nameof(Arena))]
    [RelatedEntityField(typeof(Arena))]
    public int ArenaId { get; set; }
    [RelatedEntityField(typeof(Arena))]
    public Arena Arena { get; set; } = null!;

    [NavigationPair(nameof(Kit))]
    [RelatedEntityField(typeof(Kit))]
    public int KitId { get; set; }
    [RelatedEntityField(typeof(Kit))]
    public Kit Kit { get; set; } = null!;

    public int SequenceNumber { get; set; }
}
