using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace knkwebapi_v2.Dtos
{
    // Join entity DTO for ItemBlueprint.Origins (see docs/specs/items/IMPLEMENTATION_PLAN.md §3.2)
    public class ItemBlueprintOriginDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("itemBlueprintId")]
        public int ItemBlueprintId { get; set; }

        [JsonPropertyName("domainId")]
        public int DomainId { get; set; }

        [JsonPropertyName("domain")]
        public DomainNavDto? Domain { get; set; }

        [JsonPropertyName("sequenceNumber")]
        public int SequenceNumber { get; set; }
    }

    public class ItemBlueprintOriginCreateDto
    {
        [Required]
        [JsonPropertyName("domainId")]
        public int DomainId { get; set; }

        [JsonPropertyName("sequenceNumber")]
        public int SequenceNumber { get; set; }
    }

    // Navigation DTO for Domain (id/name/subtype, matching DomainRegionDecisionDto's DomainType convention)
    public class DomainNavDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("domainType")]
        public string DomainType { get; set; } = string.Empty;
    }
}
