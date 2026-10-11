using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    public class TownDto : IDomainTeleportSettingsDto, IDomainNavigationDefaultDto
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

        [JsonPropertyName("streetIds")]
        public List<int>? StreetIds { get; set; } = new();
        [JsonPropertyName("streets")]
        public List<TownStreetDto>? Streets { get; set; } = new();
        [JsonPropertyName("districtIds")]
        public List<int>? DistrictIds { get; set; } = new();
        [JsonPropertyName("districts")]
        public List<TownDistrictDto> Districts { get; set; } = new();
    }

    public class TownListDto
    {
        [JsonPropertyName("id")]
        public int? id { get; set; }

        [JsonPropertyName("name")]
        public string name { get; set; } = null!;

        [JsonPropertyName("description")]
        public string description { get; set; } = null!;

        [JsonPropertyName("wgRegionId")]
        public string wgRegionId { get; set; } = null!;
    }
}

namespace knkwebapi_v2.Dtos
{
    // Lightweight Street DTO for embedding in Town payloads
    public class TownStreetDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    // Lightweight District DTO for embedding in Town payloads
    public class TownDistrictDto
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
    }
}
