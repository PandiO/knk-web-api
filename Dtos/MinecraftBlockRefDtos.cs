using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    public class MinecraftBlockRefDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("namespaceKey")]
        public string NamespaceKey { get; set; } = null!;

        [JsonPropertyName("blockStateString")]
        public string? BlockStateString { get; set; }

        [JsonPropertyName("logicalType")]
        public string? LogicalType { get; set; }

        [JsonPropertyName("iconUrl")]
        public string? IconUrl { get; set; }

        // KNG-200 §2.2 (read-only; set by the backfill).
        [JsonPropertyName("materialRefId")]
        public int? MaterialRefId { get; set; }

        [JsonPropertyName("stateKey")]
        public string? StateKey { get; set; }

        [JsonPropertyName("isPartial")]
        public bool? IsPartial { get; set; }
    }

    public class MinecraftBlockRefCreateDto
    {
        [JsonPropertyName("namespaceKey")]
        public string NamespaceKey { get; set; } = null!;

        [JsonPropertyName("blockStateString")]
        public string? BlockStateString { get; set; }

        [JsonPropertyName("logicalType")]
        public string? LogicalType { get; set; }

        [JsonPropertyName("iconUrl")]
        public string? IconUrl { get; set; }
    }

    public class MinecraftBlockRefUpdateDto
    {
        [JsonPropertyName("namespaceKey")]
        public string NamespaceKey { get; set; } = null!;

        [JsonPropertyName("blockStateString")]
        public string? BlockStateString { get; set; }

        [JsonPropertyName("logicalType")]
        public string? LogicalType { get; set; }

        [JsonPropertyName("iconUrl")]
        public string? IconUrl { get; set; }
    }

    public class MinecraftBlockRefListDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("namespaceKey")]
        public string NamespaceKey { get; set; } = null!;

        [JsonPropertyName("blockStateString")]
        public string? BlockStateString { get; set; }

        [JsonPropertyName("logicalType")]
        public string? LogicalType { get; set; }

        [JsonPropertyName("displayName")]
        public string? DisplayName { get; set; }

        [JsonPropertyName("isPersisted")]
        public bool? IsPersisted { get; set; }

        [JsonPropertyName("iconUrl")]
        public string? IconUrl { get; set; }
    }
}
