using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>
/// Join entity: a <see cref="User"/>'s membership in a <see cref="PermissionGroup"/>. A user can
/// hold multiple groups at once (e.g. a staff group and a premium group simultaneously — see
/// docs/specs/user-features/DESIGN.md §2.1), and each membership independently expires.
/// </summary>
[FormConfigurableEntity("UserPermissionGroup")]
public class UserPermissionGroup
{
    // Composite primary key (User + PermissionGroup combo)
    [NavigationPair(nameof(User))]
    [RelatedEntityField(typeof(User))]
    public int UserId { get; set; }
    [RelatedEntityField(typeof(User))]
    public User User { get; set; } = null!;

    [NavigationPair(nameof(PermissionGroup))]
    [RelatedEntityField(typeof(PermissionGroup))]
    public int PermissionGroupId { get; set; }
    [RelatedEntityField(typeof(PermissionGroup))]
    public PermissionGroup PermissionGroup { get; set; } = null!;

    /// <summary>Null = membership never expires.</summary>
    public DateTime? ExpiresAt { get; set; }
}
