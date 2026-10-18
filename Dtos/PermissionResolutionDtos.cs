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

    /// <summary>
    /// A permission group's effective answer for a node (KNG-116): the group, then its parent chain, with the same
    /// matching as a user's check. Used for the hub's mode-exemption checkboxes.
    /// </summary>
    public class PermissionGroupCheckResponseDto
    {
        [JsonPropertyName("groupId")]
        public int GroupId { get; set; }

        [JsonPropertyName("node")]
        public string Node { get; set; } = null!;

        [JsonPropertyName("result")]
        public PermissionResolutionResult Result { get; set; }

        [JsonPropertyName("allowed")]
        public bool Allowed => Result == PermissionResolutionResult.Granted;

        /// <summary>The group (this one or a parent) whose grant decided; null if Undeclared.</summary>
        [JsonPropertyName("sourceGroupId")]
        public int? SourceGroupId { get; set; }

        [JsonPropertyName("sourceGroupName")]
        public string? SourceGroupName { get; set; }

        [JsonPropertyName("matchedNode")]
        public string? MatchedNode { get; set; }
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

        /// <summary>
        /// The source holder's place in the resolution chain: 0 for the user's own grants, then the groups in
        /// precedence order. Lets a client resolve a node the way CheckAsync does - the lowest rank with any
        /// matching entry decides - from this one response (KNG-107: the plugin's permission checks).
        /// </summary>
        [JsonPropertyName("sourceRank")]
        public int SourceRank { get; set; }
    }

    public class PermissionEffectiveResponseDto
    {
        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        [JsonPropertyName("permissions")]
        public List<EffectivePermissionEntryDto> Permissions { get; set; } = new();
    }
}
