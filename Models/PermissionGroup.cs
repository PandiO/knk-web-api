using System.ComponentModel;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

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
    [DefaultValue(false)]
    public bool IsPremiumTier { get; set; } = false;

    /// <summary>
    /// The rank-based multiplier SalaryService applies for a member holding this group
    /// (docs/specs/user-features/DESIGN.md §5). Default 1.0 (neutral — matches an unset v1
    /// Donator.Multiplier precedent). A user's overall rank multiplier is the product of this
    /// value across every currently-active group membership they hold (developer-confirmed
    /// combination rule) — an empty membership set therefore yields 1.0, not 0.
    /// </summary>
    public decimal SalaryMultiplier { get; set; } = 1.0m;

    /// <summary>
    /// Rank-based multiplier on title promotion gem bonuses (TitleBracket.GemBonus, KNG-16),
    /// combined across active memberships and with User.PersonalGemBonusMultiplier the same way
    /// as SalaryMultiplier. Default 1.0 (neutral).
    /// </summary>
    public decimal GemBonusMultiplier { get; set; } = 1.0m;

    /// <summary>Rank-based multiplier on title promotion XP bonuses (TitleBracket.ExpBonus,
    /// KNG-16) — see GemBonusMultiplier.</summary>
    public decimal ExpBonusMultiplier { get; set; } = 1.0m;

    /// <summary>
    /// In-game chat styles for members whose display group this is (KNG-7): the premium tier's,
    /// or the Default group's for players without one. Stored as Minecraft "&amp;" formatting
    /// codes only ("&amp;e", "&amp;6&amp;l", hex "&amp;x&amp;f&amp;f&amp;a&amp;a&amp;0&amp;0" —
    /// see <see cref="knkwebapi_v2.Services.MinecraftTextStyle"/>), the format the FormWizard's
    /// "Minecraft text coloring" field setting previews. ChatPrimaryColor styles the title and
    /// username, ChatSecondaryColor the "-{ }-" brackets around the title (v1
    /// Donator.PrimaryColor/SecondColor). Null = the plugin's built-in default.
    /// </summary>
    public string? ChatPrimaryColor { get; set; }

    /// <inheritdoc cref="ChatPrimaryColor"/>
    public string? ChatSecondaryColor { get; set; }

    /// <summary>
    /// Tab-list / nametag color (scoreboard team color) for members whose display group this is
    /// (KNG-7). Separate from the chat colors on purpose — v1 let them differ (Default: gray name,
    /// green chat). Same "&amp;" code format as <see cref="ChatPrimaryColor"/>; a scoreboard team
    /// can only use the 16 named colors, so the plugin maps a hex color to the nearest one and
    /// ignores bold/italic etc. here.
    /// </summary>
    public string? NameColor { get; set; }

    // ===== Teleport fees and cooldowns (Linear KNG-41) =====
    // One block per kind of teleport: Request (/tpa and /tpahere, paid by the requester), Warp
    // (/warp and the teleport menu) and Spawn (/spawn). A player's value is taken from the first
    // group that sets it, in permission-resolution order: their groups from the highest Weight
    // down, each followed by its parent chain (TeleportGroupPolicy). Price and cooldown are
    // resolved separately. Nothing set anywhere = today's default (the plugin's
    // teleport.request.price-coins, the domain's gem price, a free /spawn,
    // teleport.cooldown-seconds). The *PriceMode field also gates an update: a DTO that omits it
    // leaves that kind's fields untouched (a form without them can't wipe them).

    /// <summary>How this group prices /tpa and /tpahere; None = no price set here.</summary>
    [DefaultValue(TeleportPriceMode.None)]
    public TeleportPriceMode TeleportRequestPriceMode { get; set; } = TeleportPriceMode.None;
    /// <summary>Multiplier mode: factor on the plugin's teleport.request.price-coins.</summary>
    public decimal? TeleportRequestPriceMultiplier { get; set; }
    /// <summary>Fixed mode: coins (null = 0).</summary>
    public int? TeleportRequestPriceCoins { get; set; }
    /// <summary>Fixed mode: gems (null = 0).</summary>
    public int? TeleportRequestPriceGems { get; set; }
    /// <summary>Fixed mode: experience points (null = 0); may demote the player's title.</summary>
    public int? TeleportRequestPriceExperience { get; set; }
    /// <summary>Replaces teleport.cooldown-seconds after a /tpa or /tpahere for the player who moved; null = not set here.</summary>
    public int? TeleportRequestCooldownSeconds { get; set; }

    /// <summary>How this group prices /warp; None = no price set here.</summary>
    [DefaultValue(TeleportPriceMode.None)]
    public TeleportPriceMode TeleportWarpPriceMode { get; set; } = TeleportPriceMode.None;
    /// <summary>Multiplier mode: factor on the destination's TeleportPriceGems.</summary>
    public decimal? TeleportWarpPriceMultiplier { get; set; }
    /// <summary>Fixed mode: coins (null = 0), replacing the destination's gem price.</summary>
    public int? TeleportWarpPriceCoins { get; set; }
    /// <summary>Fixed mode: gems (null = 0), replacing the destination's gem price.</summary>
    public int? TeleportWarpPriceGems { get; set; }
    /// <summary>Fixed mode: experience points (null = 0); may demote the player's title.</summary>
    public int? TeleportWarpPriceExperience { get; set; }
    /// <summary>Replaces teleport.cooldown-seconds after a /warp; null = not set here.</summary>
    public int? TeleportWarpCooldownSeconds { get; set; }

    /// <summary>How this group prices /spawn (Fixed only - /spawn has no default price); None = free unless another group sets one.</summary>
    [DefaultValue(TeleportPriceMode.None)]
    public TeleportPriceMode TeleportSpawnPriceMode { get; set; } = TeleportPriceMode.None;
    /// <summary>Fixed mode: coins (null = 0).</summary>
    public int? TeleportSpawnPriceCoins { get; set; }
    /// <summary>Fixed mode: gems (null = 0).</summary>
    public int? TeleportSpawnPriceGems { get; set; }
    /// <summary>Fixed mode: experience points (null = 0); may demote the player's title.</summary>
    public int? TeleportSpawnPriceExperience { get; set; }
    /// <summary>Replaces teleport.cooldown-seconds after a /spawn; null = not set here.</summary>
    public int? TeleportSpawnCooldownSeconds { get; set; }

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
