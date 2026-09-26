using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// One entry on a player's ignore list - GET api/users/{id}/ignores
    /// (docs/specs/private-messages/DESIGN.md §3.2).
    /// </summary>
    public class UserIgnoreDto
    {
        [JsonPropertyName("ignoredUserId")]
        public int IgnoredUserId { get; set; }

        [JsonPropertyName("ignoredUsername")]
        public string IgnoredUsername { get; set; } = null!;

        /// <summary>
        /// The ignored player's Minecraft UUID, so the plugin can match chat and private messages
        /// without looking each sender's user id up. Null for a web-only account.
        /// </summary>
        [JsonPropertyName("ignoredUuid")]
        public string? IgnoredUuid { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }
    }
}
