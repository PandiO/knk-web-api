using System;

namespace knkwebapi_v2.Models;

/// <summary>
/// One web-app refresh token (closed-alpha hardening WP4, decision D4). The token itself is 32
/// random bytes (base64url) that only the browser holds, in an HttpOnly cookie; this row stores
/// its SHA-256. Every refresh rotates it: the old row is revoked with <see cref="ReplacedByHash"/>
/// set and a new row joins the same <see cref="FamilyId"/>. Presenting a revoked token again
/// (reuse, likely theft) revokes the whole family. Rows are deleted by RetentionPolicyService
/// 7 days after they expire or are revoked.
/// </summary>
public class RefreshToken
{
    public long Id { get; set; }

    public int UserId { get; set; }

    public User? User { get; set; }

    /// <summary>Lower-case hex SHA-256 of the token (64 chars). Unique.</summary>
    public string TokenHash { get; set; } = null!;

    /// <summary>One login session: every token rotated from the same login shares it (32 hex chars).
    /// Also carried in the access token as the "sid" claim.</summary>
    public string FamilyId { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAt { get; set; }

    /// <summary>Set on rotation, logout, family revocation or "sign out everywhere".</summary>
    public DateTime? RevokedAt { get; set; }

    /// <summary>The hash of the token this one was rotated into; null when revoked otherwise.</summary>
    public string? ReplacedByHash { get; set; }

    /// <summary>"Remember me" at login: a persistent cookie and the long lifetime
    /// (Security:Jwt:RefreshTokenDays) instead of a session cookie and Security:Jwt:SessionRefreshHours.</summary>
    public bool RememberMe { get; set; }

    public string? CreatedByIp { get; set; }

    public string? UserAgent { get; set; }
}
