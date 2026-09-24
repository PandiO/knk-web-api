using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>
/// A named bundle of permission grants users can be assigned to (staff rank, premium tier,
/// etc.). Single-parent inheritance chain via <see cref="ParentGroupId"/> — see
/// docs/specs/user-features/DESIGN.md §1/§2.1.
/// </summary>
[FormConfigurableEntity("PermissionGroup")]
public class PermissionGroup : PermissionHolder
{
    public string Name { get; set; } = null!;

    /// <summary>
    /// Tie-break for prefix/suffix display when a user is in multiple groups at once
    /// (LuckPerms-style, highest wins) and the priority used when resolving group-level
    /// permission grants (highest weight checked first).
    /// </summary>
    public int Weight { get; set; }

    /// <summary>
    /// Marks this group as a premium tier (DESIGN.md §4) rather than a staff/general group.
    /// Premium tiers are ordinary groups in every other respect — this flag only tells UIs and
    /// <see cref="knkwebapi_v2.Services.UserPermissionGroupService"/> which of a user's
    /// memberships to surface as their "premium tier" (the highest-Weight active one).
    /// </summary>
    public bool IsPremiumTier { get; set; }

    [NavigationPair(nameof(ParentGroup))]
    [RelatedEntityField(typeof(PermissionGroup))]
    public int? ParentGroupId { get; set; }

    [RelatedEntityField(typeof(PermissionGroup))]
    public PermissionGroup? ParentGroup { get; set; }

    // One-to-many: ParentGroupId -> ChildGroups
    [RelatedEntityField(typeof(PermissionGroup))]
    public ICollection<PermissionGroup> ChildGroups { get; set; } = new List<PermissionGroup>();

    [RelatedEntityField(typeof(UserPermissionGroup))]
    public ICollection<UserPermissionGroup> UserMemberships { get; set; } = new List<UserPermissionGroup>();
}
