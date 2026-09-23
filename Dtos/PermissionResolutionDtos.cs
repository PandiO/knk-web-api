using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>Result of resolving a single permission node against a User's grants/group chain.</summary>
    public enum PermissionResolutionResult
    {
        Granted = 0,
        Denied = 1,
        /// <summary>No grant/deny found anywhere in the resolution chain — fail-closed treats this as "no".</summary>
        Undeclared = 2
    }

    public class PermissionCheckResponseDto
    {
        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        [JsonPropertyName("node")]
        public string Node { get; set; } = null!;

        [JsonPropertyName("result")]
        public PermissionResolutionResult Result { get; set; }

        /// <summary>Convenience boolean the plugin can branch on directly (Granted -> true, everything else -> false).</summary>
        [JsonPropertyName("allowed")]
        public bool Allowed => Result == PermissionResolutionResult.Granted;

        /// <summary>Id of the PermissionHolder (User or PermissionGroup) whose grant produced this result. Null if Undeclared.</summary>
        [JsonPropertyName("sourceHolderId")]
        public int? SourceHolderId { get; set; }

        [JsonPropertyName("sourceHolderType")]
        public string? SourceHolderType { get; set; }

        /// <summary>The actual grant node that matched (may differ from the queried node if it was a wildcard).</summary>
        [JsonPropertyName("matchedNode")]
        public string? MatchedNode { get; set; }
    }

    public class EffectivePermissionEntryDto
    {
        [JsonPropertyName("node")]
        public string Node { get; set; } = null!;

        [JsonPropertyName("value")]
        public bool Value { get; set; }

        [JsonPropertyName("sourceHolderId")]
        public int SourceHolderId { get; set; }

        [JsonPropertyName("sourceHolderType")]
        public string SourceHolderType { get; set; } = null!;

        [JsonPropertyName("sourceHolderName")]
        public string? SourceHolderName { get; set; }
    }

    public class PermissionEffectiveResponseDto
    {
        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        [JsonPropertyName("permissions")]
        public List<EffectivePermissionEntryDto> Permissions { get; set; } = new();
    }
}
