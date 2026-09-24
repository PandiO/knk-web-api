using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    public class PermissionGroupDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("weight")]
        public int Weight { get; set; }

        [JsonPropertyName("isPremiumTier")]
        public bool IsPremiumTier { get; set; }

        [JsonPropertyName("salaryMultiplier")]
        public decimal SalaryMultiplier { get; set; } = 1.0m;

        [JsonPropertyName("chatPrefix")]
        public string? ChatPrefix { get; set; }

        [JsonPropertyName("chatSuffix")]
        public string? ChatSuffix { get; set; }

        [JsonPropertyName("parentGroupId")]
        public int? ParentGroupId { get; set; }
        [JsonPropertyName("parentGroup")]
        public PermissionGroupDto? ParentGroup { get; set; }

        [JsonPropertyName("childGroups")]
        public List<RelatedPermissionGroupDto> ChildGroups { get; set; } = new();
    }

    public class RelatedPermissionGroupDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("weight")]
        public int Weight { get; set; }
    }

    public class PermissionGroupListDto
    {
        [JsonPropertyName("id")]
        public int? id { get; set; }
        [JsonPropertyName("name")]
        public string name { get; set; } = null!;
        [JsonPropertyName("weight")]
        public int weight { get; set; }
        [JsonPropertyName("isPremiumTier")]
        public bool isPremiumTier { get; set; }
        [JsonPropertyName("salaryMultiplier")]
        public decimal salaryMultiplier { get; set; } = 1.0m;
        [JsonPropertyName("parentGroupId")]
        public int? parentGroupId { get; set; }
        [JsonPropertyName("parentGroupName")]
        public string? parentGroupName { get; set; }

        // Convenience: number of direct child groups
        [JsonPropertyName("childrenCount")]
        public int childrenCount { get; set; }
    }
}
