using System;
using System.Collections.Generic;
using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>
/// Represents a user account in Knights & Kings.
/// Supports both web app and Minecraft server account flows.
/// Handles authentication, account linking, and soft deletion.
/// </summary>
[FormConfigurableEntity("User")]
public class User : PermissionHolder
{
    /// <summary>
    /// Player name; max 256 chars. Unique and immutable.
    /// Mirrors Minecraft server identity or web app input.
    /// </summary>
    public string Username { get; set; } = null!;

    /// <summary>
    /// Minecraft UUID. Nullable at creation (web app first flow).
    /// Once set, becomes immutable and unique.
    /// Null for web app-only accounts until first Minecraft join.
    /// </summary>
    public string? Uuid { get; set; }

    /// <summary>
    /// Email address. Optional; only set for web app accounts.
    /// Unique and immutable. Null for Minecraft-only accounts.
    /// </summary>
    public string? Email { get; set; }

    /// <summary>
    /// Bcrypt hashed password (10-12 rounds). Nullable.
    /// Only populated for web app accounts.
    /// CRITICAL: Never expose this in API responses via DTOs.
    /// </summary>
    public string? PasswordHash { get; set; }

    /// <summary>
    /// Account creation timestamp. Set at record creation or first Minecraft login.
    /// Immutable across both web and Minecraft platforms.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Primary in-game currency. Tied to real-money purchases (premium).
    /// Default: 250 coins on account creation.
    /// Non-negative; mutations atomic and logged to audit trail.
    /// CRITICAL: Update only through service methods, never direct assignment.
    /// </summary>
    public int Coins { get; set; } = 250;

    /// <summary>
    /// Secondary in-game currency. Earned through gameplay (free-to-play).
    /// Default: 50 gems on account creation.
    /// Non-negative; mutations atomic and logged with recoverable metadata.
    /// CRITICAL: Update only through service methods, never direct assignment.
    /// </summary>
    public int Gems { get; set; } = 50;

    /// <summary>
    /// Player progression experience points.
    /// Default: 0 on account creation.
    /// Non-negative; mutations atomic and logged with recoverable metadata.
    /// CRITICAL: Update only through service methods, never direct assignment.
    /// </summary>
    public int ExperiencePoints { get; set; } = 0;

    // ===== AUTHENTICATION & METADATA =====

    /// <summary>
    /// Email verification status. Default: false.
    /// Set to true when email is verified (future feature - TBD).
    /// </summary>
    public bool EmailVerified { get; set; } = false;

    /// <summary>
    /// Indicates whether this is a full account (email + password registered on web app).
    /// Full accounts can log into the web app. Minecraft-only accounts cannot.
    /// 
    /// Determined by presence of email and password hash:
    /// - True: Email is set AND PasswordHash is not null → Web login capable
    /// - False: Either email is null OR PasswordHash is null → Minecraft-only
    /// 
    /// Used to:
    /// 1. Guide registration flow (prevent username conflicts with minecraft-only accounts)
    /// 2. Filter eligible accounts for web login
    /// 3. Determine account linking strategies
    /// </summary>
    public bool IsFullAccount => !string.IsNullOrEmpty(Email) && !string.IsNullOrEmpty(PasswordHash);

    /// <summary>
    /// Indicates how the account was originally created (web app vs Minecraft server).
    /// Used for analytics and account recovery flows.
    /// </summary>
    public AccountCreationMethod AccountCreatedVia { get; set; } = AccountCreationMethod.WebApp;

    /// <summary>
    /// This user's preferred gate pass-through method, used by the Minecraft plugin when the
    /// player right-clicks a closed, pass-through-enabled gate. Player-configurable in-game via
    /// /knk gate passthrough &lt;mode&gt;.
    /// </summary>
    public GatePassThroughMethod GatePassThroughMethodDefault { get; set; } = GatePassThroughMethod.Default;

    /// <summary>
    /// This user's active owner/staff mode (docs/specs/user-features/DESIGN.md §6.1), toggled
    /// in-game via /ownermode and /staffmode. Any value other than <see cref="ActiveMode.None"/>
    /// means the player is vanished (hidden from players without staff/owner visibility).
    /// Persisted so the plugin can restore it on the player's next login instead of defaulting
    /// everyone visible after a server restart, as v1's in-memory-only maps did. Writable only
    /// through PUT /api/users/{id}/active-mode, never through the generic user update.
    /// </summary>
    public ActiveMode ActiveMode { get; set; } = ActiveMode.None;

    /// <summary>
    /// Per-player override for the salary formula (docs/specs/user-features/DESIGN.md §5) — a
    /// plain numeric field, not a permission grant, confirmed DESIGN.md §7 item 9. Default 1.0
    /// (neutral). Multiplied together with the global and rank-based multipliers by SalaryService.
    /// </summary>
    public decimal PersonalSalaryMultiplier { get; set; } = 1.0m;

    /// <summary>
    /// UTC timestamp of this user's last salary payout, advanced by SalaryService on each payout.
    /// Backfilled to the migration's apply time for pre-existing users (not their CreatedAt) so
    /// rollout doesn't trigger one giant retroactive payout. Service-managed only — ignored by the
    /// generic UserDto update path, same convention as ActiveMode.
    /// </summary>
    public DateTime LastSalaryPayoutAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Online-presence tracking for the moderation view's "currently online" filter
    /// (docs/specs/user-management/DESIGN.md §5/§7 item 2, IMPLEMENTATION_PLAN.md Phase 3).
    /// Set by knk-plugin's PlayerListener via PUT /api/users/{id}/presence on
    /// PlayerJoinEvent/PlayerQuitEvent — there is no periodic sync loop for users to piggyback
    /// on (confirmed: UsersDataAccess only refreshes on-demand on a stale cache hit), so this is
    /// real-time-ish rather than lagged behind a sync interval. Defaults to false/null so a
    /// server that never reports presence (e.g. this session's live test) doesn't show everyone
    /// as perpetually online. Service-managed only — ignored by the generic UserDto update path,
    /// same convention as ActiveMode.
    /// </summary>
    public bool IsOnline { get; set; } = false;

    /// <summary>
    /// UTC timestamp of the last presence report (join or quit) for this user. Null for a user
    /// who has never triggered a presence update (pre-existing rows at migration time, or a
    /// web-only account that has never joined the Minecraft server).
    /// </summary>
    public DateTime? LastSeenAt { get; set; }

    // ===== AUDIT TRAIL (MINIMAL - MVP) =====

    /// <summary>
    /// Timestamp of last password change. Nullable if password never set.
    /// Used for security audits and password age tracking.
    /// </summary>
    public DateTime? LastPasswordChangeAt { get; set; }

    /// <summary>
    /// Timestamp of last email change. Nullable if email never changed.
    /// Used for security audits and email change history.
    /// </summary>
    public DateTime? LastEmailChangeAt { get; set; }

    // ===== SOFT DELETION =====

    /// <summary>
    /// Active status. Default: true.
    /// Set to false for soft deletion (account deactivation).
    /// Allows recovery within 90-day grace period.
    /// </summary>
    public bool IsActive { get; set; } = true;

    /// <summary>
    /// Soft delete timestamp. Null if account is active.
    /// Set when account is marked for deletion.
    /// Used to calculate ArchiveUntil deadline.
    /// </summary>
    public DateTime? DeletedAt { get; set; }

    /// <summary>
    /// Reason for soft deletion. Examples:
    /// - "Merged with user {id}"
    /// - "User requested deletion"
    /// - "Admin deactivation"
    /// Null if account is active.
    /// </summary>
    public string? DeletedReason { get; set; }

    /// <summary>
    /// Time-to-live for soft-deleted record.
    /// Set to DeletedAt + 90 days when account is deleted.
    /// After this date, account is eligible for permanent hard delete.
    /// Provides audit trail recovery window.
    /// </summary>
    public DateTime? ArchiveUntil { get; set; }

    // ===== RELATIONSHIPS =====

    /// <summary>
    /// Navigation property to link codes associated with this user.
    /// One user can have multiple link codes (old codes expire, new ones generated).
    /// </summary>
    public ICollection<LinkCode> LinkCodes { get; set; } = new List<LinkCode>();

    /// <summary>
    /// This user's PermissionGroup memberships. A user can hold multiple groups at once
    /// (e.g. staff + premium simultaneously) — see docs/specs/user-features/DESIGN.md §2.1.
    /// </summary>
    [RelatedEntityField(typeof(UserPermissionGroup))]
    public ICollection<UserPermissionGroup> PermissionGroupMemberships { get; set; } = new List<UserPermissionGroup>();

    /// <summary>
    /// Optional gender, ported from v1's Gender table (Male/Female + Mylord/Mylady prefix) to
    /// resolve a title's MaleName/FemaleName display form. Null = unset; title display falls
    /// back to MaleName. Never required at account creation.
    /// </summary>
    public Gender? Gender { get; set; }

    // ===== ADMIN FREEZE (rebuilt v1 FreezeCommands — v1's version was a dead no-op stub) =====

    /// <summary>
    /// Whether this player is currently admin-frozen (movement/chat/commands/damage locked
    /// in-game). Persisted so /freeze applied to an offline player takes effect on next join,
    /// and so state survives a server restart. Set only via PUT /api/users/{id}/freeze and
    /// .../unfreeze — ignored by the generic UserDto update path, same convention as ActiveMode.
    /// </summary>
    public bool IsFrozen { get; set; } = false;

    public string? FrozenReason { get; set; }

    public int? FrozenByUserId { get; set; }

    public DateTime? FrozenAt { get; set; }
}

/// <summary>
/// Enum indicating the platform through which the account was created.
/// Used for analytics, account recovery, and feature flags.
/// </summary>
public enum AccountCreationMethod
{
    /// <summary>
    /// Account created via web application (email + password signup).
    /// </summary>
    WebApp = 0,

    /// <summary>
    /// Account created via Minecraft server (first join triggers auto-creation).
    /// Minimal data: UUID + Username only.
    /// </summary>
    MinecraftServer = 1
}

/// <summary>
/// A player's owner/staff mode (docs/specs/user-features/DESIGN.md §6.1). Mutually exclusive: a
/// player is in at most one mode at a time, and vanish is implied by any mode other than None
/// rather than tracked as a separate flag, since v1 never vanished a player outside a mode.
/// </summary>
public enum ActiveMode
{
    /// <summary>
    /// Not in any mode; visible to everyone.
    /// </summary>
    None = 0,

    /// <summary>
    /// Staff mode (/staffmode, gated by knk.mode.staff in-game).
    /// </summary>
    Staff = 1,

    /// <summary>
    /// Owner mode (/ownermode, gated by knk.mode.owner in-game).
    /// </summary>
    Owner = 2
}

/// <summary>
/// A player's preferred method for passing through a closed, pass-through-enabled gate.
/// See GateStructure.AllowPassThrough / PassThroughDurationSeconds.
/// </summary>
public enum GatePassThroughMethod
{
    /// <summary>
    /// The gate opens, stays open for GateStructure.PassThroughDurationSeconds, then auto-closes.
    /// </summary>
    Default = 0,

    /// <summary>
    /// Only the door blocks in the player's path are instantly removed, then restored once the
    /// player has passed through. Requires the knk.gate.passthrough.instant permission in-game.
    /// </summary>
    InstantOpen = 1,

    /// <summary>
    /// The player is teleported directly to the other side of the gate. The gate never animates.
    /// </summary>
    Teleport = 2
}

/// <summary>
/// Ported from v1's 2-row Gender table (Male: "Mylord", Female: "Mylady" prefix) — kept as an
/// enum rather than a DB table since it will never have more than these two values.
/// </summary>
public enum Gender
{
    Male = 0,
    Female = 1
}
