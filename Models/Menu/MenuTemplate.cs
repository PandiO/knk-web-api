using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// Top-level InventoryMenu definition (IMPLEMENTATION_PLAN.md Phase 1).
/// Root of the Menu → MenuSection → MenuItem composite tree; per FR-2.1.1.
/// </summary>
public class MenuTemplate
{
    public int Id { get; set; }

    /// <summary>Stable lookup key the plugin resolves by (e.g. "kits.overview").</summary>
    public string Key { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
    public string? Description { get; set; }

    /// <summary>Row count (FR-2.1.1); width is always 9, per standard Minecraft inventories.</summary>
    public int Height { get; set; } = 3;

    public MenuGrowthMode Growth { get; set; } = MenuGrowthMode.Static;

    public int? BackgroundMaterialRefId { get; set; }
    public MinecraftMaterialRef? BackgroundMaterial { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<MenuSectionTemplate> Sections { get; set; } = new();
}
