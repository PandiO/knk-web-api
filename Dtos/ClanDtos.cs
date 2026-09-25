using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Dtos
{
    // Siege Phase 1 DTOs: BannerDesign, BannerLayer, Clan (docs/specs/siege-minigame/DESIGN.md §3.1–3.2).

    // ---- BannerLayer ----

    public class BannerLayerDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("bannerDesignId")]
        public int BannerDesignId { get; set; }

        [JsonPropertyName("sortOrder")]
        public int SortOrder { get; set; }

        [JsonPropertyName("patternKey")]
        public string PatternKey { get; set; } = string.Empty;

        [JsonPropertyName("color")]
        public BannerDyeColor Color { get; set; }
    }

    // Create and update share one shape. SortOrder null on create = append on top of the existing
    // layers; null on update = keep the current position.
    public class BannerLayerUpsertDto
    {
        [JsonPropertyName("bannerDesignId")]
        public int? BannerDesignId { get; set; }

        [JsonPropertyName("sortOrder")]
        public int? SortOrder { get; set; }

        [Required]
        [JsonPropertyName("patternKey")]
        public string PatternKey { get; set; } = string.Empty;

        [JsonPropertyName("color")]
        public BannerDyeColor Color { get; set; } = BannerDyeColor.BLACK;
    }

    // ---- BannerDesign ----

    public class BannerDesignReadDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("baseColor")]
        public BannerDyeColor BaseColor { get; set; }

        // Ordered bottom -> top.
        [JsonPropertyName("layers")]
        public List<BannerLayerDto> Layers { get; set; } = new();

        // More than 6 layers can't be crafted on a survival loom (still renders fine, up to 16).
        [JsonPropertyName("exceedsSurvivalLoomLimit")]
        public bool ExceedsSurvivalLoomLimit { get; set; }
    }

    // Layers are managed through the layer endpoints only (owned child collection) - a "layers"
    // property in the payload is ignored.
    public class BannerDesignUpsertDto
    {
        [Required]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("baseColor")]
        public BannerDyeColor BaseColor { get; set; } = BannerDyeColor.WHITE;
    }

    public class BannerDesignListDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("baseColor")]
        public BannerDyeColor BaseColor { get; set; }

        [JsonPropertyName("layerCount")]
        public int LayerCount { get; set; }
    }

    // ---- Clan ----

    public class ClanReadDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("isNpc")]
        public bool IsNpc { get; set; }

        [JsonPropertyName("chatColor")]
        public string ChatColor { get; set; } = "WHITE";

        [JsonPropertyName("bannerDesignId")]
        public int BannerDesignId { get; set; }

        [JsonPropertyName("bannerDesign")]
        public BannerDesignReadDto? BannerDesign { get; set; }

        [JsonPropertyName("defaultForTownId")]
        public int? DefaultForTownId { get; set; }

        [JsonPropertyName("defaultForTownName")]
        public string? DefaultForTownName { get; set; }
    }

    public class ClanUpsertDto
    {
        [Required]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("isNpc")]
        public bool IsNpc { get; set; }

        [JsonPropertyName("chatColor")]
        public string? ChatColor { get; set; }

        [JsonPropertyName("bannerDesignId")]
        public int BannerDesignId { get; set; }

        [JsonPropertyName("defaultForTownId")]
        public int? DefaultForTownId { get; set; }
    }

    public class ClanListDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("isNpc")]
        public bool IsNpc { get; set; }

        [JsonPropertyName("chatColor")]
        public string ChatColor { get; set; } = "WHITE";

        [JsonPropertyName("bannerDesignId")]
        public int BannerDesignId { get; set; }

        [JsonPropertyName("bannerDesignName")]
        public string? BannerDesignName { get; set; }

        [JsonPropertyName("defaultForTownId")]
        public int? DefaultForTownId { get; set; }

        [JsonPropertyName("defaultForTownName")]
        public string? DefaultForTownName { get; set; }
    }
}
