using System.Collections.Generic;
using System;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    public class StructureDto : IDomainTeleportSettingsDto, IDomainNavigationDefaultDto, IDomainNameDto
    {
        // KNG-254: the domain's sub-regions, read-only here (edited through api/DomainSubRegions, the form's owned list).
        [JsonPropertyName("subRegions")]
        public List<DomainSubRegionDto>? SubRegions { get; set; }

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

        // KNG-195: embedded Location, like Town/District/GateStructure. A spawn point captured in game or
        // created on the form arrives here (no id = create, id = update); a plain pick sends locationId.
        [JsonPropertyName("location")]
        public LocationDto? Location { get; set; }

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

        // /navigate default (KNG-73): null on create/update = not on this form; "" or "TypeDefault"
        // = follow the domain type; "Spawn" / "Region" = this domain's own default.
        [JsonPropertyName("navigationDefaultOverride")]
        public string? NavigationDefaultOverride { get; set; }

        // Rev. 7 Part C (KNG-92): null on create/update = not on this form; "" or "TypeDefault" = follow
        // the domain type; "Applies" / "Ignored" = whether this domain's entry rule keeps routes off its roads.
        [JsonPropertyName("roadAccessOverride")]
        public string? RoadAccessOverride { get; set; }

        // Placement (P2, PandiO/knk-workspace#91): in a district (districtId + streetId + houseNumber) or rural
        // (townId, no street, houseNumber 0). Exactly one of districtId and townId is set; 0 counts as not set.
        [JsonPropertyName("streetId")]
        public int? StreetId { get; set; }

        [JsonPropertyName("districtId")]
        public int? DistrictId { get; set; }

        [JsonPropertyName("townId")]
        public int? TownId { get; set; }

        /// <summary>Read only: the district's town, or townId for a rural structure.</summary>
        [JsonPropertyName("effectiveTownId")]
        public int? EffectiveTownId { get; set; }

        /// <summary>Read only: true when the structure has no district (townId set).</summary>
        [JsonPropertyName("isRural")]
        public bool? IsRural { get; set; }

        /// <summary>Read only, on create: placement rules that failed as warnings (P-1 to P-3 warn by default).</summary>
        [JsonPropertyName("placementWarnings")]
        public List<string>? PlacementWarnings { get; set; }

        // Territory (PandiO/knk-workspace#326), read only here: loyalty is set through
        // PUT api/Territory/structures/{id}/loyalty, the flag by the game server's territory report.
        [JsonPropertyName("loyaltyTownId")]
        public int? LoyaltyTownId { get; set; }

        [JsonPropertyName("territoryFlag")]
        public string? TerritoryFlag { get; set; }

        [JsonPropertyName("territoryFlaggedAt")]
        public DateTime? TerritoryFlaggedAt { get; set; }

        [JsonPropertyName("territoryConflictId")]
        public int? TerritoryConflictId { get; set; }

        [JsonPropertyName("houseNumber")]
        public int HouseNumber { get; set; }

        // Optional embedded lightweight navigations
        [JsonPropertyName("street")]
        public StructureStreetDto? Street { get; set; }

        [JsonPropertyName("district")]
        public StructureDistrictDto? District { get; set; }

        /// <summary>Read only: the rural structure's town (null for a structure in a district).</summary>
        [JsonPropertyName("town")]
        public StructureTownDto? Town { get; set; }
    }

    public class StructureListDto
    {
        [JsonPropertyName("id")]
        public int? id { get; set; }

        [JsonPropertyName("name")]
        public string name { get; set; } = null!;

        [JsonPropertyName("description")]
        public string description { get; set; } = null!;

        [JsonPropertyName("wgRegionId")]
        public string wgRegionId { get; set; } = null!;

        // KNG-111: the Minecraft world of the domain and its region (region ids are unique per world).
        [JsonPropertyName("worldName")]
        public string? WorldName { get; set; }

        [JsonPropertyName("houseNumber")]
        public int houseNumber { get; set; }

        [JsonPropertyName("streetId")]
        public int? streetId { get; set; }

        [JsonPropertyName("streetName")]
        public string? streetName { get; set; }

        [JsonPropertyName("districtId")]
        public int? districtId { get; set; }

        // P2 (#91): the structure's town (its district's, or its own when rural), so the game server's territory
        // report can check rural structures too (L2 note: it skipped structures without a district).
        [JsonPropertyName("townId")]
        public int? townId { get; set; }

        [JsonPropertyName("isRural")]
        public bool isRural { get; set; }

        [JsonPropertyName("territoryFlag")]
        public string? territoryFlag { get; set; }

        [JsonPropertyName("districtName")]
        public string? districtName { get; set; }

        // KNG-166: the district's town, for pickers. Read-only.
        [JsonPropertyName("townName")]
        public string? townName { get; set; }
    }
}

namespace knkwebapi_v2.Dtos
{
    // Lightweight District DTO for embedding in Structure payloads
    public class StructureDistrictDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("allowEntry")]
        public bool? AllowEntry { get; set; }

        [JsonPropertyName("allowExit")]
        public bool? AllowExit { get; set; }

        [JsonPropertyName("wgRegionId")]
        public string? WgRegionId { get; set; }

        // KNG-111: the Minecraft world of the domain and its region (region ids are unique per world).
        [JsonPropertyName("worldName")]
        public string? WorldName { get; set; }

        // KNG-166: a picker row for a structure created in a form names its town, like a search row.
        [JsonPropertyName("townName")]
        public string? TownName { get; set; }
    }

    // Lightweight Town DTO for a rural structure (P2, #91)
    public class StructureTownDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("wgRegionId")]
        public string? WgRegionId { get; set; }

        [JsonPropertyName("worldName")]
        public string? WorldName { get; set; }
    }

    // Lightweight Street DTO for embedding in Structure payloads
    public class StructureStreetDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }
}
