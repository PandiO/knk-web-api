using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// A user's membership in a PermissionGroup (IMPLEMENTATION_PLAN.md §5). Returned by the
    /// UserPermissionGroups endpoints; also used for a user's resolved premium tier.
    /// </summary>
    public class UserPermissionGroupDto
    {
        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        [JsonPropertyName("permissionGroupId")]
        public int PermissionGroupId { get; set; }

        [JsonPropertyName("permissionGroupName")]
        public string? PermissionGroupName { get; set; }

        [JsonPropertyName("weight")]
        public int Weight { get; set; }

        [JsonPropertyName("isPremiumTier")]
        public bool IsPremiumTier { get; set; }

        /// <summary>The group's display colors (KNG-7) — see PermissionGroup.ChatPrimaryColor.</summary>
        [JsonPropertyName("chatPrimaryColor")]
        public string? ChatPrimaryColor { get; set; }

        [JsonPropertyName("chatSecondaryColor")]
        public string? ChatSecondaryColor { get; set; }

        [JsonPropertyName("nameColor")]
        public string? NameColor { get; set; }

        /// <summary>Null = never expires.</summary>
        [JsonPropertyName("expiresAt")]
        public DateTime? ExpiresAt { get; set; }

        /// <summary>False once ExpiresAt has passed. Expired rows are kept (they're the history
        /// of past temporary tiers), but the resolution engine ignores them.</summary>
        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// Body for PUT /api/UserPermissionGroups — creates the membership, or updates its expiry if
    /// the user already holds that group (one row per user+group pair).
    /// </summary>
    public class UpsertUserPermissionGroupDto
    {
        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        [JsonPropertyName("permissionGroupId")]
        public int PermissionGroupId { get; set; }

        /// <summary>Null = permanent. Must be in the future when set.</summary>
        [JsonPropertyName("expiresAt")]
        public DateTime? ExpiresAt { get; set; }
    }

    /// <summary>
    /// One row of GET /api/PermissionGroups/{id}/expiring-memberships
    /// (docs/specs/user-management/IMPLEMENTATION_PLAN.md Phase 3 "premium expiring soon" view).
    /// </summary>
    public class ExpiringMembershipDto
    {
        [JsonPropertyName("userId")]
        public int UserId { get; set; }

        [JsonPropertyName("username")]
        public string Username { get; set; } = null!;

        [JsonPropertyName("permissionGroupId")]
        public int PermissionGroupId { get; set; }

        [JsonPropertyName("expiresAt")]
        public DateTime ExpiresAt { get; set; }
    }

    /// <summary>
    /// GET /api/PermissionGroups/{id}/member-count (PandiO/knk-workspace#50): how many players a change to the group's
    /// own grants reaches, for the web app's "affects N players" confirmation. Active memberships only.
    /// </summary>
    public class PermissionGroupMemberCountDto
    {
        [JsonPropertyName("groupId")]
        public int GroupId { get; set; }

        /// <summary>Players with an active membership in this group itself.</summary>
        [JsonPropertyName("directMembers")]
        public int DirectMembers { get; set; }

        /// <summary>Distinct players reached: members of this group and of every group that inherits from it (any
        /// depth), each player counted once.</summary>
        [JsonPropertyName("totalMembers")]
        public int TotalMembers { get; set; }

        /// <summary>How many groups inherit from this one (children, grandchildren, ...).</summary>
        [JsonPropertyName("inheritingGroups")]
        public int InheritingGroups { get; set; }
    }
}
