using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>
/// Optional override of <see cref="Grade.DropChance"/> for one type's box-grade roll (DESIGN.md §3.1). A grade
/// without a row keeps its DropChance. Composite key (LootboxTypeId, GradeId); edited through the type form.
/// </summary>
[FormConfigurableEntity("LootboxTypeGradeWeight")]
public class LootboxTypeGradeWeight
{
    [NavigationPair(nameof(LootboxType))]
    [RelatedEntityField(typeof(LootboxType))]
    public int LootboxTypeId { get; set; }
    [RelatedEntityField(typeof(LootboxType))]
    public LootboxType LootboxType { get; set; } = null!;

    [NavigationPair(nameof(Grade))]
    [RelatedEntityField(typeof(Grade))]
    public int GradeId { get; set; }
    [RelatedEntityField(typeof(Grade))]
    public Grade Grade { get; set; } = null!;

    public decimal Weight { get; set; }
}
