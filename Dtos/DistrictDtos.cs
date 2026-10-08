using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    public class DistrictDto : IDomainTeleportSettingsDto, IDomainNavigationDefaultDto
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

        [JsonPropertyName("locationId")]
        public int? LocationId { get; set; }
        
        // Optional embedded Location to allow cascading create/update
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

        [JsonPropertyName("townId")]
        public int TownId { get; set; }

        [JsonPropertyName("streetIds")]
        public List<int> StreetIds { get; set; } = new();

        // Optional embedded Town with primitives only to avoid cycles
        [JsonPropertyName("town")]
        public DistrictTownDto? Town { get; set; }

        // Optional embedded Streets collection
        [JsonPropertyName("streets")]
        public IEnumerable<DistrictStreetDto>? Streets { get; set; }

        // Optional embedded Structures collection
        [JsonPropertyName("structures")]
        public IEnumerable<DistrictStructureDto>? Structures { get; set; }
    }

    public class DistrictListDto
    {
        [JsonPropertyName("id")]
        public int? id { get; set; }

        [JsonPropertyName("name")]
        public string name { get; set; } = null!;

        [JsonPropertyName("description")]
        public string description { get; set; } = null!;

        [JsonPropertyName("wgRegionId")]
        public string wgRegionId { get; set; } = null!;

        [JsonPropertyName("townId")]
        public int townId { get; set; }

        [JsonPropertyName("townName")]
        public string? townName { get; set; }
    }
}

namespace knkwebapi_v2.Dtos
{
    // Lightweight Town DTO for embedding in District payloads (no back-references)
    public class DistrictTownDto
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

        [JsonPropertyName("locationId")]
        public int? LocationId { get; set; }
    }
}

namespace knkwebapi_v2.Dtos
{
    // Lightweight Street DTO for embedding in District payloads
    public class DistrictStreetDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    // Lightweight Structure DTO for embedding in District payloads
    public class DistrictStructureDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("houseNumber")]
        public int? HouseNumber { get; set; }

        [JsonPropertyName("streetId")]
        public int? StreetId { get; set; }
    }
}
