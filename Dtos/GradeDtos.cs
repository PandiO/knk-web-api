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

        // The web app's generic form only submits the fields on the Grade form configuration. A form built
        // before KNG-6 has neither of these, and treating "absent" as null would silently un-cap the grade on
        // every rename. So GradeService only writes a field the request actually contained (explicit null
        // still clears it): System.Text.Json calls a setter only for properties present in the JSON.
        private decimal? _dropChance;
        private int? _enchantLevelCapDivisor;

        /// <summary>Percent 0-100; null = not set.</summary>
        [Range(0, 100)]
        [JsonPropertyName("dropChance")]
        public decimal? DropChance
        {
            get => _dropChance;
            set { _dropChance = value; HasDropChance = true; }
        }

        /// <summary>Enchant-book level cap divisor (KNG-6); null = uncapped.</summary>
        [Range(1, int.MaxValue)]
        [JsonPropertyName("enchantLevelCapDivisor")]
        public int? EnchantLevelCapDivisor
        {
            get => _enchantLevelCapDivisor;
            set { _enchantLevelCapDivisor = value; HasEnchantLevelCapDivisor = true; }
        }

        [JsonIgnore]
        public bool HasDropChance { get; private set; }

        [JsonIgnore]
        public bool HasEnchantLevelCapDivisor { get; private set; }
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
