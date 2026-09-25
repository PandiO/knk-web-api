using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// A single clickable inventory item within a MenuSectionTemplate; per FR-2.1.3.
/// Name/lore are not plain string columns — they're VariableBindings (even a
/// literal, unresolved string is a STATIC-policy binding), per
/// IMPLEMENTATION_PLAN.md's "name and lore as variable-bound strings".
/// </summary>
public class MenuItemTemplate
{
    public int Id { get; set; }

    public int MenuSectionTemplateId { get; set; }
    public MenuSectionTemplate MenuSectionTemplate { get; set; } = null!;

    /// <summary>
    /// Explicit ordering — not inferred from list position — per
    /// IMPLEMENTATION_PLAN.md and FORMCONFIG_INTEGRATION.md's ordering concern.
    /// </summary>
    public int SortOrder { get; set; }

    /// <summary>Explicit slot within the section; null means auto-assigned (FR-2.4.5).</summary>
    public int? SlotOverride { get; set; }

    public int? MaterialRefId { get; set; }
    public MinecraftMaterialRef? Material { get; set; }

    public int Amount { get; set; } = 1;
    public string? ChatColorName { get; set; }
    public string? ChatColorDescription { get; set; }

    /// <summary>NORMAL/DISABLED/HIGHLIGHT/HIDDEN, per IMPLEMENTATION_PLAN.md.</summary>
    public MenuDisplayMode DisplayMode { get; set; } = MenuDisplayMode.Normal;

    /// <summary>Render-time gate: should this item be visible at all.</summary>
    public string? VisibilityPermission { get; set; }

    /// <summary>Click-time gate: can this item's action(s) actually execute.</summary>
    public string? ActionPermission { get; set; }

    /// <summary>
    /// InventoryMenu Phase 9 (E3): this item is the section's row template - the
    /// section's content source yields plain row objects and each one is rendered
    /// through this item with the getter-chain root <c>$row$</c> bound to that row.
    /// At most one per section; requires the section to have a ContentSourceId and
    /// the item to have no SlotOverride (validated in MenuTemplateService).
    /// </summary>
    public bool IsRowTemplate { get; set; } = false;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<VariableBinding> VariableBindings { get; set; } = new();
    public List<ActionBinding> Actions { get; set; } = new();

    /// <summary>Conditions that gate this item as a whole (ActionBindingId == null); see ActionBinding.Conditions for per-action ones.</summary>
    public List<ConditionBinding> Conditions { get; set; } = new();
}
