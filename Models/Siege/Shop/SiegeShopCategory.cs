using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

// A siege shop category (knk-workspace docs/specs/siege-shop/DESIGN.md § 6.1.1, P19; PandiO/knk-workspace#867).
// Seeded: siege-weapons, consumables, magic. A category without visible entries is not shown in the shop (§ 5.3.2).
[FormConfigurableEntity("SiegeShopCategory")]
public class SiegeShopCategory
{
    public int Id { get; set; }

    // Stable key the plugin and seeds use ("consumables"); unique.
    public string Key { get; set; } = null!;
    public string Name { get; set; } = null!;

    // Namespaced material key for the category's icon in the shop menu ("minecraft:golden_apple"); optional.
    public string? IconMaterial { get; set; }
    public int SortOrder { get; set; }
}
