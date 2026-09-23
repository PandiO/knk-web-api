using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    public class PermissionGrantDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("holderId")]
        public int HolderId { get; set; }

        /// <summary>"User" or "PermissionGroup" — the concrete PermissionHolder subtype, for admin UI display.</summary>
        [JsonPropertyName("holderType")]
        public string? HolderType { get; set; }

        [JsonPropertyName("node")]
        public string Node { get; set; } = null!;

        [JsonPropertyName("value")]
        public bool Value { get; set; } = true;

        [JsonPropertyName("expiresAt")]
        public DateTime? ExpiresAt { get; set; }
    }

    public class PermissionGrantListDto
    {
        [JsonPropertyName("id")]
        public int? id { get; set; }
        [JsonPropertyName("holderId")]
        public int holderId { get; set; }
        [JsonPropertyName("holderType")]
        public string? holderType { get; set; }
        [JsonPropertyName("node")]
        public string node { get; set; } = null!;
        [JsonPropertyName("value")]
        public bool value { get; set; }
        [JsonPropertyName("expiresAt")]
        public DateTime? expiresAt { get; set; }
    }
}
