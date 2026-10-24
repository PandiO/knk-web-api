using System;

namespace knkwebapi_v2.Models;

/// <summary>
/// Represents a temporary link code used to connect web app and Minecraft server accounts.
/// 
/// One row per code, told apart by <see cref="Purpose"/> (KNG-208):
/// 1. Link: /account link in game → the player enters the code on the website (register, or the
///    Account page of an existing web login). 8 characters from A-Z0-9, 20 minutes.
/// 2. PasswordReset: the SHA-256 hash of a reset token (64 hex characters), AuthService.
/// 3. Claim: the admission gate's claim code shown on the disconnect screen (KNG-214): 8 Crockford
///    base32 characters bound to a Mojang-verified <see cref="MinecraftUuid"/>, no user yet.
/// 
/// CRITICAL PROPERTIES:
/// - ExpiresAt: from creation plus the purpose's lifetime
/// - Status: Active, Used, or Expired
/// - Uniqueness: Code is globally unique and must not be reused
/// </summary>
public class LinkCode
{
    /// <summary>
    /// Primary key - auto-generated.
    /// </summary>
    public int Id { get; set; }

    /// <summary>
    /// Foreign key to User. Nullable for web app-first flow.
    /// Set when:
    /// 1. Generated via /account link on Minecraft server
    /// 2. After user consumes code (consumed → user populated)
    /// Null initially for web app-first flow until consumption.
    /// </summary>
    public int? UserId { get; set; }

    /// <summary>
    /// The actual link code string.
    /// Format: 8 alphanumeric characters (e.g., ABC12XYZ)
    /// Display format (with hyphen): ABC-12XYZ
    /// 
    /// CRITICAL: Must be globally unique. No code can be reused.
    /// Entropy: 36^8, about 2.8 trillion combinations for a link code (32^8, about 1.1 trillion,
    /// for a claim code); 256 bits for a reset-token hash.
    /// Generated with RandomNumberGenerator.GetInt32, so no character is favoured (KNG-208).
    /// </summary>
    public string Code { get; set; } = null!;

    /// <summary>What the code is for (KNG-208). Decides which lookups see it and how it is used.</summary>
    public LinkCodePurpose Purpose { get; set; } = LinkCodePurpose.Link;

    /// <summary>
    /// Claim codes only (KNG-214): the Mojang-verified UUID of the player the admission gate
    /// stopped, written by the plugin's pre-login check. Null for link codes and reset tokens.
    /// </summary>
    public string? MinecraftUuid { get; set; }

    /// <summary>Claim codes only: the player's name at the time, for "Verified: Minecraft account Steve".</summary>
    public string? MinecraftName { get; set; }

    /// <summary>
    /// Timestamp when the code was created.
    /// Immutable once set.
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Timestamp when the code expires.
    /// Set to CreatedAt + 20 minutes.
    /// After this time, code cannot be used.
    /// </summary>
    public DateTime ExpiresAt { get; set; }

    /// <summary>
    /// Current status of the link code.
    /// - Active: Code is valid and can be used
    /// - Used: Code has been consumed/linked
    /// - Expired: ExpiresAt has passed
    /// </summary>
    public LinkCodeStatus Status { get; set; } = LinkCodeStatus.Active;

    /// <summary>
    /// Timestamp when the code was consumed/used.
    /// Null if code has not been used.
    /// Set when code transitions to Used status.
    /// </summary>
    public DateTime? UsedAt { get; set; }

    // ===== RELATIONSHIPS =====

    /// <summary>
    /// Navigation property to the associated User.
    /// Null for web app-first flow until consumption.
    /// </summary>
    public User? User { get; set; }
}

/// <summary>What a <see cref="LinkCode"/> row is for (KNG-208). Stored as an int; don't renumber.</summary>
public enum LinkCodePurpose
{
    /// <summary>/account link in game, entered on the website. 8 characters, A-Z0-9.</summary>
    Link = 0,

    /// <summary>A password-reset token's SHA-256 hash (64 hex characters), AuthService.</summary>
    PasswordReset = 1,

    /// <summary>An admission claim code bound to a Minecraft UUID (KNG-214). 8 Crockford base32 characters.</summary>
    Claim = 2
}

/// <summary>
/// Enum representing the lifecycle status of a link code.
/// </summary>
public enum LinkCodeStatus
{
    /// <summary>
    /// Code is valid and ready to be used.
    /// Has not expired and has not been consumed yet.
    /// </summary>
    Active = 0,

    /// <summary>
    /// Code has been successfully consumed/used.
    /// UsedAt timestamp is set.
    /// Cannot be reused.
    /// </summary>
    Used = 1,

    /// <summary>
    /// Code has expired (ExpiresAt < UtcNow).
    /// No longer valid for use.
    /// User must request a new code.
    /// </summary>
    Expired = 2
}
