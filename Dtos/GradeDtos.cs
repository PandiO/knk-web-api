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
    }

    public class GradeCreateDto
    {
        [Required]
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("stars")]
        public int Stars { get; set; }
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
    }

    public class GradeListDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("stars")]
        public int Stars { get; set; }
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
    }
}
