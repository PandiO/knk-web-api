using System;
using System.Text.Json.Serialization;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// DTO for creating a new user account.
    /// Supports both web app first and Minecraft server first flows.
    /// </summary>
    public class UserCreateDto
    {
        [JsonPropertyName("username")]
        public string Username { get; set; } = null!;

        /// <summary>
        /// Optional - nullable for web app first flow (set on first Minecraft join).
        /// </summary>
        [JsonPropertyName("uuid")]
        public string? Uuid { get; set; }

        /// <summary>
        /// Optional - nullable for Minecraft-only accounts.
        /// </summary>
        [JsonPropertyName("email")]
        public string? Email { get; set; }

        /// <summary>
        /// Password for web app accounts. Nullable for Minecraft-only accounts.
        /// Will be hashed before storage.
        /// </summary>
        [JsonPropertyName("password")]
        public string? Password { get; set; }

        /// <summary>
        /// Password confirmation (must match Password).
        /// Required when Password is provided.
        /// </summary>
        [JsonPropertyName("passwordConfirmation")]
        public string? PasswordConfirmation { get; set; }

        /// <summary>
        /// Link code for account linking flows (optional).
        /// Used when linking existing Minecraft account to new web app account.
        /// </summary>
        [JsonPropertyName("linkCode")]
        public string? LinkCode { get; set; }

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }

    /// <summary>
    /// Full user DTO for API responses.
    /// CRITICAL: Never includes PasswordHash.
    /// </summary>
    public class UserDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("username")]
        public string Username { get; set; } = null!;

        [JsonPropertyName("uuid")]
        public string? Uuid { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("coins")]
        public int Coins { get; set; }

        [JsonPropertyName("gems")]
        public int Gems { get; set; }

        [JsonPropertyName("experiencePoints")]
        public int ExperiencePoints { get; set; }

        [JsonPropertyName("emailVerified")]
        public bool EmailVerified { get; set; }

        [JsonPropertyName("accountCreatedVia")]
        public AccountCreationMethod AccountCreatedVia { get; set; }

        [JsonPropertyName("gatePassThroughMethodDefault")]
        public GatePassThroughMethod GatePassThroughMethodDefault { get; set; }

        [JsonPropertyName("activeMode")]
        public ActiveMode ActiveMode { get; set; }

        /// <summary>
        /// Resolved from ExperiencePoints by TitleService, not stored on the user row
        /// (docs/specs/user-features/IMPLEMENTATION_PLAN.md §4). Null only if no title brackets
        /// are seeded.
        /// </summary>
        [JsonPropertyName("titleBracketId")]
        public int? TitleBracketId { get; set; }

        [JsonPropertyName("titleName")]
        public string? TitleName { get; set; }

        [JsonPropertyName("prestigeExperience")]
        public int PrestigeExperience { get; set; }

        /// <summary>
        /// The user's current premium tier — their highest-Weight active membership in a
        /// PermissionGroup flagged IsPremiumTier (IMPLEMENTATION_PLAN.md §5). Null when they hold
        /// none. PremiumTierExpiresAt is null for a permanent tier.
        /// </summary>
        [JsonPropertyName("premiumTierGroupId")]
        public int? PremiumTierGroupId { get; set; }

        [JsonPropertyName("premiumTierName")]
        public string? PremiumTierName { get; set; }

        [JsonPropertyName("premiumTierExpiresAt")]
        public DateTime? PremiumTierExpiresAt { get; set; }

        /// <summary>
        /// Salary formula's personal override (IMPLEMENTATION_PLAN.md §6). Admin-editable via the
        /// generic user CRUD, default 1.0 (neutral).
        /// </summary>
        [JsonPropertyName("personalSalaryMultiplier")]
        public decimal PersonalSalaryMultiplier { get; set; } = 1.0m;

        /// <summary>
        /// Read-only: last time SalaryService paid this user out. Not writable via this DTO — see
        /// POST /api/users/{id}/salary/payout.
        /// </summary>
        [JsonPropertyName("lastSalaryPayoutAt")]
        public DateTime LastSalaryPayoutAt { get; set; }

        /// <summary>
        /// Read-only presence state (IMPLEMENTATION_PLAN.md Phase 3) — see
        /// PUT /api/users/{id}/presence. Not writable via this DTO.
        /// </summary>
        [JsonPropertyName("isOnline")]
        public bool IsOnline { get; set; }

        [JsonPropertyName("lastSeenAt")]
        public DateTime? LastSeenAt { get; set; }

        [JsonPropertyName("gender")]
        public Gender? Gender { get; set; }

        [JsonPropertyName("isFrozen")]
        public bool IsFrozen { get; set; }

        [JsonPropertyName("frozenReason")]
        public string? FrozenReason { get; set; }

        [JsonPropertyName("isFullAccount")]
        public bool IsFullAccount { get; set; }

        [JsonPropertyName("createdAt")]
        public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }
    }

    /// <summary>
    /// Lightweight User DTO for embedding in other payloads (no sensitive info).
    /// </summary>
    public class UserSummaryDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("username")]
        public string Username { get; set; } = null!;

        [JsonPropertyName("uuid")]
        public string? Uuid { get; set; }

        [JsonPropertyName("coins")]
        public int Coins { get; set; }

        [JsonPropertyName("gems")]
        public int Gems { get; set; }

        [JsonPropertyName("experiencePoints")]
        public int ExperiencePoints { get; set; }

        [JsonPropertyName("isFullAccount")]
        public bool IsFullAccount { get; set; }

        [JsonPropertyName("gatePassThroughMethodDefault")]
        public GatePassThroughMethod GatePassThroughMethodDefault { get; set; }

        [JsonPropertyName("activeMode")]
        public ActiveMode ActiveMode { get; set; }

        /// <summary>Resolved from ExperiencePoints by TitleService — see UserDto's field.</summary>
        [JsonPropertyName("titleBracketId")]
        public int? TitleBracketId { get; set; }

        [JsonPropertyName("titleName")]
        public string? TitleName { get; set; }

        [JsonPropertyName("prestigeExperience")]
        public int PrestigeExperience { get; set; }

        /// <summary>
        /// The user's current premium tier — their highest-Weight active membership in a
        /// PermissionGroup flagged IsPremiumTier (IMPLEMENTATION_PLAN.md §5). Null when they hold
        /// none. PremiumTierExpiresAt is null for a permanent tier.
        /// </summary>
        [JsonPropertyName("premiumTierGroupId")]
        public int? PremiumTierGroupId { get; set; }

        [JsonPropertyName("premiumTierName")]
        public string? PremiumTierName { get; set; }

        [JsonPropertyName("premiumTierExpiresAt")]
        public DateTime? PremiumTierExpiresAt { get; set; }

        /// <summary>Admin-freeze state (PUT /api/users/{id}/freeze|unfreeze) — read here so the
        /// plugin can restore/enforce it on the player's next join without a separate call.</summary>
        [JsonPropertyName("isFrozen")]
        public bool IsFrozen { get; set; }

        [JsonPropertyName("frozenReason")]
        public string? FrozenReason { get; set; }
    }

    /// <summary>
    /// DTO for updating a user's preferred gate pass-through method (set via /knk gate passthrough
    /// in-game). Mirrors the single-field update pattern used for coins.
    /// </summary>
    public class UpdateGatePassThroughMethodDto
    {
        [JsonPropertyName("gatePassThroughMethodDefault")]
        public GatePassThroughMethod GatePassThroughMethodDefault { get; set; }
    }

    /// <summary>
    /// DTO for updating a user's owner/staff mode (set via /ownermode or /staffmode in-game).
    /// Mirrors the single-field update pattern used for the gate pass-through method.
    /// </summary>
    public class UpdateActiveModeDto
    {
        [JsonPropertyName("activeMode")]
        public ActiveMode ActiveMode { get; set; }
    }

    /// <summary>Body for PUT /api/users/{id}/presence — knk-plugin's PlayerListener join/quit hooks.</summary>
    public class UpdatePresenceDto
    {
        [JsonPropertyName("isOnline")]
        public bool IsOnline { get; set; }
    }

    /// <summary>Body for PUT /api/users/{id}/freeze — required. /unfreeze takes no body.</summary>
    public class FreezePlayerDto
    {
        [JsonPropertyName("reason")]
        public string Reason { get; set; } = null!;
    }

    /// <summary>
    /// DTO for adjusting a user's coins/gems/experience by a signed delta with an audit reason
    /// (docs/specs/user-features/IMPLEMENTATION_PLAN.md §4's demotion/deduction hook — also usable
    /// for any coins/gems economy adjustment). Wraps UserService.AdjustBalancesAsync, which
    /// already rejects underflow on any of the three balances.
    /// </summary>
    public class AdjustBalancesDto
    {
        [JsonPropertyName("coinsDelta")]
        public int CoinsDelta { get; set; }

        [JsonPropertyName("gemsDelta")]
        public int GemsDelta { get; set; }

        [JsonPropertyName("experienceDelta")]
        public int ExperienceDelta { get; set; }

        [JsonPropertyName("reason")]
        public string Reason { get; set; } = null!;

        [JsonPropertyName("metadata")]
        public string? Metadata { get; set; }
    }

    /// <summary>
    /// DTO for listing users (admin/search views).
    /// </summary>
    public class UserListDto
    {
        [JsonPropertyName("id")]
        public int? id { get; set; }

        [JsonPropertyName("username")]
        public string username { get; set; } = null!;

        [JsonPropertyName("uuid")]
        public string? uuid { get; set; }

        [JsonPropertyName("email")]
        public string? email { get; set; }

        [JsonPropertyName("coins")]
        public int Coins { get; set; }

        [JsonPropertyName("gems")]
        public int Gems { get; set; }

        [JsonPropertyName("experiencePoints")]
        public int ExperiencePoints { get; set; }

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }

        [JsonPropertyName("isOnline")]
        public bool IsOnline { get; set; }

        [JsonPropertyName("lastSeenAt")]
        public DateTime? LastSeenAt { get; set; }
    }

    /// <summary>
    /// DTO for updating user account settings.
    /// </summary>
    public class UserUpdateDto
    {
        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("currentPassword")]
        public string? CurrentPassword { get; set; }
    }

    /// <summary>
    /// DTO for changing user password.
    /// </summary>
    public class ChangePasswordDto
    {
        [JsonPropertyName("currentPassword")]
        public string CurrentPassword { get; set; } = null!;

        [JsonPropertyName("newPassword")]
        public string NewPassword { get; set; } = null!;

        [JsonPropertyName("passwordConfirmation")]
        public string PasswordConfirmation { get; set; } = null!;
    }

    /// <summary>
    /// DTO for updating user email.
    /// </summary>
    public class UpdateEmailDto
    {
        [JsonPropertyName("newEmail")]
        public string NewEmail { get; set; } = null!;

        /// <summary>
        /// Current password for security verification.
        /// Optional if user doesn't have a password set yet.
        /// </summary>
        [JsonPropertyName("currentPassword")]
        public string? CurrentPassword { get; set; }
    }

    /// <summary>
    /// DTO for account merge result.
    /// Shows the final merged account with metadata about the merge.
    /// </summary>
    public class AccountMergeResultDto
    {
        [JsonPropertyName("user")]
        public UserDto User { get; set; } = null!;

        [JsonPropertyName("mergedFromUserId")]
        public int MergedFromUserId { get; set; }

        [JsonPropertyName("message")]
        public string Message { get; set; } = null!;
    }
}