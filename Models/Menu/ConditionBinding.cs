namespace knkwebapi_v2.Models;

/// <summary>
/// A data-driven click-time condition: a registered ConditionRegistry key
/// plus a params map, not inline code. Always belongs to a MenuItemTemplate;
/// when ActionBindingId is also set, it gates only that one action rather
/// than the item as a whole (DESIGN_REVIEW.md §2.2: "per action (or per
/// MenuItem)").
/// </summary>
public class ConditionBinding
{
    public int Id { get; set; }

    public int MenuItemTemplateId { get; set; }
    public MenuItemTemplate MenuItemTemplate { get; set; } = null!;

    /// <summary>Null gates the whole item; set gates just that one action.</summary>
    public int? ActionBindingId { get; set; }
    public ActionBinding? ActionBinding { get; set; }

    /// <summary>Key into the plugin-side ConditionRegistry.</summary>
    public string ConditionTypeId { get; set; } = string.Empty;

    /// <summary>Key-value params for the condition, serialized as JSON.</summary>
    public string ParamsJson { get; set; } = "{}";

    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
