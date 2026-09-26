using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// Admin adjustment of a type's item pool (DESIGN.md §3.1 step 3). The pool is every graded blueprint of the
/// type's category (not tagged <c>Lootbox Special</c>); an <see cref="LootboxPoolMode.Include"/> row adds a
/// blueprint from anywhere (and may give an ungraded one a grade via <see cref="GradeIdOverride"/>) or re-weights
/// one, an <see cref="LootboxPoolMode.Exclude"/> row removes one. Composite key (LootboxTypeId, ItemBlueprintId).
/// ItemBlueprint is Restrict (vision §9.2 no-cascade rule).
/// </summary>
[FormConfigurableEntity("LootboxPoolEntry")]
public class LootboxPoolEntry
{
    [NavigationPair(nameof(LootboxType))]
    [RelatedEntityField(typeof(LootboxType))]
    public int LootboxTypeId { get; set; }
    [RelatedEntityField(typeof(LootboxType))]
    public LootboxType LootboxType { get; set; } = null!;

    [NavigationPair(nameof(ItemBlueprint))]
    [RelatedEntityField(typeof(ItemBlueprint))]
    public int ItemBlueprintId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint ItemBlueprint { get; set; } = null!;

    public LootboxPoolMode Mode { get; set; } = LootboxPoolMode.Include;

    // Relative weight among the pool items of the same grade; null = 1.
    public decimal? WeightOverride { get; set; }

    [NavigationPair(nameof(GradeOverride))]
    [RelatedEntityField(typeof(Grade))]
    public int? GradeIdOverride { get; set; }
    [RelatedEntityField(typeof(Grade))]
    public Grade? GradeOverride { get; set; }
}
