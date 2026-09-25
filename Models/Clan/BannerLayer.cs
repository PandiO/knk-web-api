using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// One pattern layer of a BannerDesign (docs/specs/siege-minigame/DESIGN.md §3.1). Rendered bottom
// to top by (SortOrder, Id). SortOrder is deliberately not unique per banner: editing one layer at
// a time in the wizard would otherwise make swapping two layers impossible without a temp value.
[FormConfigurableEntity("BannerLayer")]
public class BannerLayer
{
    public int Id { get; set; }

    [NavigationPair(nameof(BannerDesign))]
    [RelatedEntityField(typeof(BannerDesign))]
    public int BannerDesignId { get; set; }
    [RelatedEntityField(typeof(BannerDesign))]
    public BannerDesign BannerDesign { get; set; } = null!;

    public int SortOrder { get; set; }

    // Bukkit PatternType registry key, e.g. "minecraft:stripe_top" - validated against
    // BannerPatternKeys rather than an enum, since Mojang adds pattern types between versions.
    public string PatternKey { get; set; } = null!;

    public BannerDyeColor Color { get; set; } = BannerDyeColor.BLACK;
}
