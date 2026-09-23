using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// A getter-chain expression bound to one rendered property of a
/// MenuSectionTemplate or a MenuItemTemplate (e.g. Name, or one lore line),
/// with the DESIGN_REVIEW.md §1 cache-invalidation policy attached.
/// Exactly one of MenuSectionTemplateId/MenuItemTemplateId is set — same
/// nullable-dual-FK shape already used by DisplayConditionGroup's
/// TargetStepId/TargetFieldId.
/// </summary>
public class VariableBinding
{
    public int Id { get; set; }

    public int? MenuSectionTemplateId { get; set; }
    public MenuSectionTemplate? MenuSectionTemplate { get; set; }

    public int? MenuItemTemplateId { get; set; }
    public MenuItemTemplate? MenuItemTemplate { get; set; }

    /// <summary>Which rendered property this populates, e.g. "Name", "Lore".</summary>
    public string TargetProperty { get; set; } = string.Empty;

    /// <summary>Ordering among bindings sharing the same TargetProperty (multi-line lore).</summary>
    public int SortOrder { get; set; }

    /// <summary>
    /// Getter-chain pattern (e.g. "$player.getName$"), or a literal string with
    /// no placeholders for a STATIC binding.
    /// </summary>
    public string Expression { get; set; } = string.Empty;

    public VariableRefreshPolicy RefreshPolicy { get; set; } = VariableRefreshPolicy.OnDirty;

    /// <summary>Only meaningful when RefreshPolicy == Ttl.</summary>
    public int? TtlTicks { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
