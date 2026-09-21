namespace knkwebapi_v2.Models;

/// <summary>
/// A data-driven click action on a MenuItemTemplate: a registered
/// ActionRegistry key plus a params map, not inline code — keeps the
/// persisted schema stable regardless of what a future FormConfig UI adds.
/// </summary>
public class ActionBinding
{
    public int Id { get; set; }

    public int MenuItemTemplateId { get; set; }
    public MenuItemTemplate MenuItemTemplate { get; set; } = null!;

    /// <summary>Key into the plugin-side ActionRegistry.</summary>
    public string ActionTypeId { get; set; } = string.Empty;

    /// <summary>Key-value params for the action, serialized as JSON.</summary>
    public string ParamsJson { get; set; } = "{}";

    /// <summary>Execution order when an item carries more than one action.</summary>
    public int SortOrder { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    /// <summary>Conditions scoped to just this action (see ConditionBinding.ActionBindingId).</summary>
    public List<ConditionBinding> Conditions { get; set; } = new();
}
