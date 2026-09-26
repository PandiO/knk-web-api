using System.Text.Json.Serialization;
using System.ComponentModel.DataAnnotations;

namespace knkwebapi_v2.Dtos
{
    public class GradeReadDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("stars")]
        public int Stars { get; set; }

        [JsonPropertyName("dropChance")]
        public decimal? DropChance { get; set; }

        [JsonPropertyName("enchantLevelCapDivisor")]
        public int? EnchantLevelCapDivisor { get; set; }
    }

    public class GradeCreateDto
    {
        [Required]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("stars")]
        public int Stars { get; set; }

        /// <summary>Percent 0-100; null = not set.</summary>
        [Range(0, 100)]
        [JsonPropertyName("dropChance")]
        public decimal? DropChance { get; set; }

        /// <summary>Enchant-book level cap divisor (KNG-6); null = uncapped.</summary>
        [Range(1, int.MaxValue)]
        [JsonPropertyName("enchantLevelCapDivisor")]
        public int? EnchantLevelCapDivisor { get; set; }
    }

    public class GradeUpdateDto
    {
        [Required]
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [Required]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("stars")]
        public int Stars { get; set; }

        /// <summary>Percent 0-100; null = not set.</summary>
        [Range(0, 100)]
        [JsonPropertyName("dropChance")]
        public decimal? DropChance { get; set; }

        /// <summary>Enchant-book level cap divisor (KNG-6); null = uncapped.</summary>
        [Range(1, int.MaxValue)]
        [JsonPropertyName("enchantLevelCapDivisor")]
        public int? EnchantLevelCapDivisor { get; set; }
    }

    public class GradeListDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("stars")]
        public int Stars { get; set; }

        [JsonPropertyName("dropChance")]
        public decimal? DropChance { get; set; }

        [JsonPropertyName("enchantLevelCapDivisor")]
        public int? EnchantLevelCapDivisor { get; set; }
    }

    // Navigation DTO for relationships (e.g. ItemBlueprint.Grade)
    public class GradeNavDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("stars")]
        public int Stars { get; set; }

        [JsonPropertyName("dropChance")]
        public decimal? DropChance { get; set; }

        [JsonPropertyName("enchantLevelCapDivisor")]
        public int? EnchantLevelCapDivisor { get; set; }
    }
}
