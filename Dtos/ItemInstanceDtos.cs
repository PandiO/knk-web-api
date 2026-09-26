using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    // GET /api/ItemInstances/{id} (docs/specs/lootboxes/DESIGN.md §3.3): the admin lookup of an item's PDC id.
    public class ItemInstanceDto
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("itemBlueprintId")]
        public int ItemBlueprintId { get; set; }

        [JsonPropertyName("itemBlueprint")]
        public ItemBlueprintNavDto? ItemBlueprint { get; set; }

        [JsonPropertyName("gradeId")]
        public int? GradeId { get; set; }

        [JsonPropertyName("grade")]
        public GradeNavDto? Grade { get; set; }

        [JsonPropertyName("ownerUserId")]
        public int? OwnerUserId { get; set; }

        [JsonPropertyName("ownerUsername")]
        public string? OwnerUsername { get; set; }

        [JsonPropertyName("origin")]
        public string Origin { get; set; } = string.Empty;

        [JsonPropertyName("originRef")]
        public string? OriginRef { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("ownerCount")]
        public int OwnerCount { get; set; }

        [JsonPropertyName("customDisplayName")]
        public string? CustomDisplayName { get; set; }

        [JsonPropertyName("isSoulbound")]
        public bool IsSoulbound { get; set; }

        [JsonPropertyName("isGhosted")]
        public bool IsGhosted { get; set; }

        [JsonPropertyName("enchantments")]
        public List<ItemInstanceEnchantmentDto> Enchantments { get; set; } = new();
    }

    public class ItemInstanceEnchantmentDto
    {
        [JsonPropertyName("enchantmentDefinitionId")]
        public int EnchantmentDefinitionId { get; set; }

        [JsonPropertyName("key")]
        public string Key { get; set; } = string.Empty;

        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; } = string.Empty;

        [JsonPropertyName("isCustom")]
        public bool IsCustom { get; set; }

        [JsonPropertyName("level")]
        public int Level { get; set; }
    }
}
