using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// A logical grouping of menu items within a MenuTemplate; per FR-2.1.2.
/// </summary>
public class MenuSectionTemplate
{
    public int Id { get; set; }

    public int MenuTemplateId { get; set; }
    public MenuTemplate MenuTemplate { get; set; } = null!;

    /// <summary>Unique within the parent MenuTemplate (FR-2.1.2).</summary>
    public string Name { get; set; } = string.Empty;

    public MenuSectionKind Kind { get; set; } = MenuSectionKind.ContentGrid;

    /// <summary>
    /// Explicit ordering — not inferred from list position — per
    /// IMPLEMENTATION_PLAN.md and FORMCONFIG_INTEGRATION.md's ordering concern.
    /// </summary>
    public int SortOrder { get; set; }

    public int DisplaySlot { get; set; }
    public int Width { get; set; } = 9;
    public int Height { get; set; } = 1;

    public MenuPositionMode PositionMode { get; set; } = MenuPositionMode.Static;
    public MenuAlignVertical AlignVertical { get; set; } = MenuAlignVertical.Top;
    public MenuAlignHorizontal AlignHorizontal { get; set; } = MenuAlignHorizontal.Left;
    public MenuOverflowMode Overflow { get; set; } = MenuOverflowMode.Hide;
    public MenuListMode ListMode { get; set; } = MenuListMode.Default;
    public MenuRenderPriority Priority { get; set; } = MenuRenderPriority.Medium;

    /// <summary>Whole-section permission gate (DESIGN_REVIEW.md §2.4), e.g. "knk.admin.tools".</summary>
    public string? VisibilityPermission { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<MenuItemTemplate> Items { get; set; } = new();
    public List<VariableBinding> VariableBindings { get; set; } = new();
}
