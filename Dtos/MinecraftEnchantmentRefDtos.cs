using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace knkwebapi_v2.Dtos
{
    public class MinecraftEnchantmentRefDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("namespaceKey")]
        public string NamespaceKey { get; set; } = string.Empty;

        [JsonPropertyName("legacyName")]
        public string? LegacyName { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("iconUrl")]
        public string? IconUrl { get; set; }

        [JsonPropertyName("maxLevel")]
        public int? MaxLevel { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("isCustom")]
        public bool IsCustom { get; set; }

        [JsonPropertyName("catalogStatus")]
        public string? CatalogStatus { get; set; }

        [JsonPropertyName("lastSeenVersion")]
        public string? LastSeenVersion { get; set; }
    }

    public class MinecraftEnchantmentRefListDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("namespaceKey")]
        public string NamespaceKey { get; set; } = string.Empty;

        [JsonPropertyName("legacyName")]
        public string? LegacyName { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("iconUrl")]
        public string? IconUrl { get; set; }

        [JsonPropertyName("maxLevel")]
        public int? MaxLevel { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("isCustom")]
        public bool IsCustom { get; set; }

        /// <summary>Unverified, Active or Missing (KNG-250).</summary>
        [JsonPropertyName("catalogStatus")]
        public string? CatalogStatus { get; set; }

        [JsonPropertyName("lastSeenVersion")]
        public string? LastSeenVersion { get; set; }
    }

    public class MinecraftEnchantmentRefCreateDto
    {
        [Required]
        [MaxLength(191)]
        [JsonPropertyName("namespaceKey")]
        public string NamespaceKey { get; set; } = string.Empty;

        [JsonPropertyName("legacyName")]
        public string? LegacyName { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("iconUrl")]
        public string? IconUrl { get; set; }

        [JsonPropertyName("maxLevel")]
        public int? MaxLevel { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }
    }

    public class MinecraftEnchantmentRefUpdateDto
    {
        [JsonPropertyName("legacyName")]
        public string? LegacyName { get; set; }

        [JsonPropertyName("category")]
        public string? Category { get; set; }

        [JsonPropertyName("iconUrl")]
        public string? IconUrl { get; set; }

        [JsonPropertyName("maxLevel")]
        public int? MaxLevel { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }
    }
}
