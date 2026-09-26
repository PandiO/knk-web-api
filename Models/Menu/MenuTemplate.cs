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
    public MinecraftMaterialRef? BackgroundMaterialRef { get; set; }

    /// <summary>
    /// InventoryMenu Phase 9 (E4): when set (&gt; 0), every open instance of this
    /// menu is re-rendered every N ticks, honouring each binding's RefreshPolicy
    /// (so Ttl bindings actually update while the menu stays open). Null = off.
    /// </summary>
    public int? AutoRefreshTicks { get; set; }

    /// <summary>
    /// Smallest row count a <see cref="MenuGrowthMode.Dynamic"/> menu may shrink to (menu follow-up
    /// 2026-09-26): the plugin drops rows left empty after rendering, down to this. Null = 1.
    /// Ignored for Static menus.
    /// </summary>
    public int? MinHeight { get; set; }

    /// <summary>
    /// Background filler material by name (e.g. "BLACK_STAINED_GLASS_PANE") for slots nothing was
    /// rendered in - easier to author than <see cref="BackgroundMaterialRefId"/>, which wins when
    /// both are set. Neither set: the plugin's default (light gray stained glass pane).
    /// </summary>
    public string? BackgroundMaterial { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<MenuSectionTemplate> Sections { get; set; } = new();
}
