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

        /// <summary>
        /// Rank multipliers on title promotion gem/XP bonuses (KNG-16). Always set when read;
        /// omitted (null) keeps the stored value on update and means 1.0 on create.
        /// </summary>
        [JsonPropertyName("gemBonusMultiplier")]
        public decimal? GemBonusMultiplier { get; set; }

        [JsonPropertyName("expBonusMultiplier")]
        public decimal? ExpBonusMultiplier { get; set; }

        [JsonPropertyName("chatPrefix")]
        public string? ChatPrefix { get; set; }

        [JsonPropertyName("chatSuffix")]
        public string? ChatSuffix { get; set; }

        /// <summary>Minecraft "&amp;" formatting codes (KNG-7) — see PermissionGroup.ChatPrimaryColor.</summary>
        [JsonPropertyName("chatPrimaryColor")]
        public string? ChatPrimaryColor { get; set; }

        [JsonPropertyName("chatSecondaryColor")]
        public string? ChatSecondaryColor { get; set; }

        [JsonPropertyName("nameColor")]
        public string? NameColor { get; set; }

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
        [JsonPropertyName("gemBonusMultiplier")]
        public decimal gemBonusMultiplier { get; set; } = 1.0m;
        [JsonPropertyName("expBonusMultiplier")]
        public decimal expBonusMultiplier { get; set; } = 1.0m;
        [JsonPropertyName("chatPrimaryColor")]
        public string? chatPrimaryColor { get; set; }
        [JsonPropertyName("chatSecondaryColor")]
        public string? chatSecondaryColor { get; set; }
        [JsonPropertyName("nameColor")]
        public string? nameColor { get; set; }
        [JsonPropertyName("parentGroupId")]
        public int? parentGroupId { get; set; }
        [JsonPropertyName("parentGroupName")]
        public string? parentGroupName { get; set; }

        // Convenience: number of direct child groups
        [JsonPropertyName("childrenCount")]
        public int childrenCount { get; set; }
    }
}
