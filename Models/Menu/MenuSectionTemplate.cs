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

    /// <summary>
    /// Rows at the top of this section's footprint a Dynamic menu never removes even when empty
    /// (menu follow-up 2026-09-26). Null/0 = every empty row may go.
    /// </summary>
    public int? MinHeight { get; set; }

    /// <summary>Whole-section permission gate (DESIGN_REVIEW.md §2.4), e.g. "knk.admin.tools".</summary>
    public string? VisibilityPermission { get; set; }

    /// <summary>
    /// IMPLEMENTATION_PLAN.md Phase 5 / DESIGN_REVIEW.md §2.1 §2.3: whether this
    /// content-listing section participates in the session's active search/filter
    /// content query (applied before pagination). One flag gates both search text
    /// and FilterBar facet values, since both compose through the same shared
    /// predicate concept rather than two independent mechanisms.
    /// </summary>
    public bool Searchable { get; set; } = false;

    /// <summary>
    /// IMPLEMENTATION_PLAN.md Phase 8: key into the plugin-side
    /// MenuContentSourceRegistry - when set, this section's auto-placed
    /// content comes from a real paged/cursor query against that registered
    /// source (e.g. "catalog.itemblueprints") instead of this section's own
    /// <see cref="Items"/>. Mirrors <see cref="ActionBinding.ActionTypeId"/>'s
    /// "registered key, not inline code" convention. Null/empty means this
    /// section keeps the pre-Phase-8 behavior (auto content from <see
    /// cref="Items"/>) - the default for every existing section.
    /// </summary>
    public string? ContentSourceId { get; set; }

    /// <summary>
    /// Key-value params for the content source, serialized as JSON - same
    /// convention as <see cref="ActionBinding.ParamsJson"/>. Unused by the
    /// one source this phase ships (it takes its page/search/filter
    /// parameters from the plugin-side, runtime-only MenuSession instead),
    /// but kept for parity with Action/ConditionBinding's shape so a future
    /// source needing static author-supplied config doesn't need a schema
    /// change.
    /// </summary>
    public string ContentSourceParamsJson { get; set; } = "{}";

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<MenuItemTemplate> Items { get; set; } = new();
    public List<VariableBinding> VariableBindings { get; set; } = new();
}
