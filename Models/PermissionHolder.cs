using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>
/// TPT base for anything that can hold permission grants: a <see cref="User"/> (direct,
/// per-account grants) or a <see cref="PermissionGroup"/> (shared grants a set of users
/// inherits via membership). See docs/specs/user-features/DESIGN.md §2.1.
/// </summary>
[FormConfigurableEntity("PermissionHolder")]
public class PermissionHolder
{
    public int Id { get; set; }

    /// <summary>Optional chat prefix shown ahead of this holder's display name.</summary>
    public string? ChatPrefix { get; set; }

    /// <summary>Optional chat suffix shown after this holder's display name.</summary>
    public string? ChatSuffix { get; set; }

    [RelatedEntityField(typeof(PermissionGrant))]
    public ICollection<PermissionGrant> Grants { get; set; } = new List<PermissionGrant>();
}
