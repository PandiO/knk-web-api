using System;
using System.Collections.ObjectModel;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// The Domain warp settings (teleport DESIGN.md §3.7.1) as carried by the Domain/Town/District/
    /// Structure DTOs. Applied by DomainTeleportSettings.Apply after
    /// ITeleportDestinationService.ValidateSettingsAsync.
    /// </summary>
    public interface IDomainTeleportSettingsDto
    {
        bool? TeleportEnabled { get; set; }
        int? TeleportPriceGems { get; set; }
        int? TeleportMinTitleBracketId { get; set; }
        int? TeleportMinPremiumGroupId { get; set; }
        bool? TeleportRequiresDiscovery { get; set; }
    }

    public class DomainDto : IDomainTeleportSettingsDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;
        [JsonPropertyName("description")]
        public string Description { get; set; } = null!;
        [JsonPropertyName("createdAt")]
        public DateTime? CreatedAt { get; set; }
        [JsonPropertyName("allowEntry")]
        public bool? AllowEntry { get; set; }
        [JsonPropertyName("allowExit")]
        public bool? AllowExit { get; set; }
        [JsonPropertyName("wgRegionId")]
        public string WgRegionId { get; set; } = null!;

        // KNG-111: the Minecraft world of the domain and its region (region ids are unique per world).
        [JsonPropertyName("worldName")]
        public string? WorldName { get; set; }
        [JsonPropertyName("locationId")]
        public int? LocationId { get; set; }

        // Warp destination settings (teleport DESIGN.md §3.7.1). On create/update a null
        // teleportEnabled means "not on this form": all five are left as they are.
        [JsonPropertyName("teleportEnabled")]
        public bool? TeleportEnabled { get; set; }

        [JsonPropertyName("teleportPriceGems")]
        public int? TeleportPriceGems { get; set; }

        [JsonPropertyName("teleportMinTitleBracketId")]
        public int? TeleportMinTitleBracketId { get; set; }

        [JsonPropertyName("teleportMinPremiumGroupId")]
        public int? TeleportMinPremiumGroupId { get; set; }

        [JsonPropertyName("teleportRequiresDiscovery")]
        public bool? TeleportRequiresDiscovery { get; set; }
        [JsonPropertyName("parentDomainId")]
        public int? ParentDomainId { get; set; }
        [JsonPropertyName("parentDomain")]
        public ParentDomainDto? ParentDomain { get; set; }
    }

    public class ParentDomainDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;
        [JsonPropertyName("parentDomainId")]
        public int? ParentDomainId { get; set; }
        [JsonPropertyName("parentDomain")]
        public ParentDomainDto? ParentDomain { get; set; }
        public string domainSubType { get; set; } = null!;
    }

    public class DomainListDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;
        [JsonPropertyName("description")]
        public string Description { get; set; } = null!;
        [JsonPropertyName("wgRegionId")]
        public string WgRegionId { get; set; } = null!;

        // KNG-111: the Minecraft world of the domain and its region (region ids are unique per world).
        [JsonPropertyName("worldName")]
        public string? WorldName { get; set; }
        [JsonPropertyName("parentDomainId")]
        public int? ParentDomainId { get; set; }
        [JsonPropertyName("parentDomain")]
        public ParentDomainDto? ParentDomain { get; set; }
        // Concrete Domain subtype (Town/District/Structure/...), same convention as
        // DomainRegionDecisionDto.DomainType - lets a picker show "Ironhaven (Town)" instead of a bare name
        // (docs/specs/items/IMPLEMENTATION_PLAN.md §3.2/open question 4).
        [JsonPropertyName("domainType")]
        public string DomainType { get; set; } = null!;
        // Where /navigate <domain> leads without spawn/region (KNG-73): "Spawn" or "Region" - the
        // domain's own override, else its type's default. The game server's catalogue reads it here.
        [JsonPropertyName("navigationDefault")]
        public string NavigationDefault { get; set; } = "Spawn";
        // Whether the domain's entry/exit rule keeps the game server's road router off its roads (rev. 7
        // Part C, KNG-92): "Applies" or "Ignored" - the domain's own override, else its type's default.
        [JsonPropertyName("roadAccess")]
        public string RoadAccess { get; set; } = "Applies";
    }

    public class DomainRegionDecisionDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;
        [JsonPropertyName("description")]
        public string Description { get; set; } = null!;
        [JsonPropertyName("wgRegionId")]
        public string WgRegionId { get; set; } = null!;

        // KNG-111: the Minecraft world of the domain and its region (region ids are unique per world).
        [JsonPropertyName("worldName")]
        public string? WorldName { get; set; }
        [JsonPropertyName("allowEntry")]
        public bool AllowEntry { get; set; }
        [JsonPropertyName("allowExit")]
        public bool AllowExit { get; set; }
        [JsonPropertyName("domainType")]
        public string DomainType { get; set; } = null!;
        [JsonPropertyName("parentDomainDecisions")]
        public Collection<DomainRegionDecisionDto> ParentDomainDecisions { get; set; } = new Collection<DomainRegionDecisionDto>();
        [JsonPropertyName("childDomainDecisions")]
        public Collection<DomainRegionDecisionDto> ChildDomainDecisions { get; set; } = new Collection<DomainRegionDecisionDto>();
    }

    /// <summary>
    /// One domain's entry/exit rule, keyed by its WorldGuard region (KNG-56). The game server writes
    /// these onto the regions as flags WorldGuard saves, so the rules hold while this API is down.
    /// </summary>
    public class DomainAccessRuleDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;
        [JsonPropertyName("wgRegionId")]
        public string WgRegionId { get; set; } = null!;

        // KNG-111: the Minecraft world of the domain and its region (region ids are unique per world).
        [JsonPropertyName("worldName")]
        public string? WorldName { get; set; }
        [JsonPropertyName("allowEntry")]
        public bool AllowEntry { get; set; }
        [JsonPropertyName("allowExit")]
        public bool AllowExit { get; set; }
        [JsonPropertyName("domainType")]
        public string DomainType { get; set; } = null!;
    }

    public class DomainRegionQueryDto
    {
        public IEnumerable<String>? WgRegionIds { get; set; }

        /// <summary>
        /// KNG-111: the world the regions are in. When set, only domains of that world match (and, as a fallback, domains
        /// not yet assigned a world). When null, the lookup is world-blind, as before.
        /// </summary>
        public string? WorldName { get; set; }
        /***
            * If true, the hierarchy is traversed from top to bottom (i.e., parent to child).
            * If false, the hierarchy is traversed from bottom to top (i.e., child to parent).
            * Example: If true, ordering is done with Town -> District -> Structure.
            * If false, ordering is done with Structure -> District -> Town.
            ***/
        public bool? TopDownHierarchy { get; set; } = true;
    }
}
