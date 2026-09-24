using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// The title bracket resolved from a user's current ExperiencePoints
    /// (docs/specs/user-features/IMPLEMENTATION_PLAN.md §4). Null fields mean no title brackets
    /// are seeded yet.
    /// </summary>
    public class TitleResolutionDto
    {
        [JsonPropertyName("titleBracketId")]
        public int? TitleBracketId { get; set; }

        [JsonPropertyName("titleName")]
        public string? TitleName { get; set; }

        /// <summary>
        /// XP earned past the highest bracket's threshold — a pure prestige signal once a user
        /// has reached the final title (DESIGN.md §3), 0 otherwise.
        /// </summary>
        [JsonPropertyName("prestigeExperience")]
        public int PrestigeExperience { get; set; }
    }
}
