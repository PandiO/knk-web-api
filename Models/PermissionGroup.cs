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

    /// <summary>
    /// The rank-based multiplier SalaryService applies for a member holding this group
    /// (docs/specs/user-features/DESIGN.md §5). Default 1.0 (neutral — matches an unset v1
    /// Donator.Multiplier precedent). A user's overall rank multiplier is the product of this
    /// value across every currently-active group membership they hold (developer-confirmed
    /// combination rule) — an empty membership set therefore yields 1.0, not 0.
    /// </summary>
    public decimal SalaryMultiplier { get; set; } = 1.0m;

    /// <summary>
    /// In-game chat colors for members whose display group this is (KNG-7): the premium tier's,
    /// or the Default group's for players without one. Stored as a Minecraft color name
    /// ("YELLOW", "DARK_RED", ... — see <see cref="knkwebapi_v2.Services.MinecraftChatColors"/>).
    /// ChatPrimaryColor colors the title and username, ChatSecondaryColor the "-{ }-" brackets
    /// around the title (v1 Donator.PrimaryColor/SecondColor). Null = the plugin's built-in
    /// default.
    /// </summary>
    public string? ChatPrimaryColor { get; set; }

    /// <inheritdoc cref="ChatPrimaryColor"/>
    public string? ChatSecondaryColor { get; set; }

    /// <summary>
    /// Tab-list / nametag color (scoreboard team color) for members whose display group this is
    /// (KNG-7). Separate from the chat colors on purpose — v1 let them differ (Default: gray name,
    /// green chat). Same color-name format as <see cref="ChatPrimaryColor"/>.
    /// </summary>
    public string? NameColor { get; set; }

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
