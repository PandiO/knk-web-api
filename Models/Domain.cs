using System;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

[FormConfigurableEntity("Domain")]
public class Domain
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;

    public string Description { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public bool AllowEntry { get; set; } = true;

    public bool AllowExit { get; set; } = true;

    public string WgRegionId { get; set; } = null!;

    [NavigationPair(nameof(Location))]
    [RelatedEntityField(typeof(Location))]
    public int? LocationId { get; set; }
    [RelatedEntityField(typeof(Location))]
    public Location? Location { get; set; }

    // ===== Warp destination (docs/specs/teleport/DESIGN.md §3.7, Linear KNG-17 Phase 5) =====
    // A domain is a /warp target when TeleportEnabled, it has a Location and AllowEntry is on.
    // TeleportDestinationService evaluates the requirements server-side, in this order: title,
    // premium tier, discovery, price.

    /// <summary>Listed as a /warp destination (its Location is the arrival point).</summary>
    public bool TeleportEnabled { get; set; }

    /// <summary>Gems charged per warp, after the warmup (ledger reason TELEPORT_FEE). 0 = free.</summary>
    public int TeleportPriceGems { get; set; }

    /// <summary>Minimum title: the player's ExperiencePoints must reach this bracket's MinExperience.</summary>
    [NavigationPair(nameof(TeleportMinTitleBracket))]
    [RelatedEntityField(typeof(TitleBracket))]
    public int? TeleportMinTitleBracketId { get; set; }
    [RelatedEntityField(typeof(TitleBracket))]
    public TitleBracket? TeleportMinTitleBracket { get; set; }

    /// <summary>Minimum premium tier (a PermissionGroup with IsPremiumTier): the player's highest
    /// active premium group must have at least this group's Weight.</summary>
    [NavigationPair(nameof(TeleportMinPremiumGroup))]
    [RelatedEntityField(typeof(PermissionGroup))]
    public int? TeleportMinPremiumGroupId { get; set; }
    [RelatedEntityField(typeof(PermissionGroup))]
    public PermissionGroup? TeleportMinPremiumGroup { get; set; }

    /// <summary>Only players who discovered this domain (UserDomainDiscovery) may warp here.</summary>
    public bool TeleportRequiresDiscovery { get; set; }

    // ===== Navigation default (KNG-73, docs/specs/navigation/DESIGN.md §6.1) =====

    /// <summary>Where <c>/navigate &lt;domain&gt;</c> leads without <c>spawn</c>/<c>region</c>: null follows the
    /// type's <see cref="DomainNavigationDefault"/>, a value overrides it for this domain.</summary>
    public NavigationDestinationMode? NavigationDefaultOverride { get; set; }

    /// <summary>Whether this domain's entry/exit rule keeps the road router off the roads in its region (rev. 7
    /// Part C, KNG-92, docs/specs/navigation/REV7_PROPOSAL.md §4): null follows the type's
    /// <see cref="DomainNavigationDefault.RoadAccess"/>, a value overrides it for this domain.</summary>
    public RoadAccessRule? RoadAccessOverride { get; set; }
}
