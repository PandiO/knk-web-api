namespace knkwebapi_v2.Configuration;

/// <summary>
/// Security configuration options for authentication and authorization.
/// </summary>
public class SecuritySettings
{
    /// <summary>
    /// Number of bcrypt rounds for password hashing. Default: 10
    /// </summary>
    public int BcryptRounds { get; set; } = 10;

    /// <summary>
    /// Link code expiration time in minutes. Default: 20
    /// </summary>
    public int LinkCodeExpirationMinutes { get; set; } = 20;

    /// <summary>
    /// Retention period for soft-deleted records in days. Default: 90
    /// </summary>
    public int SoftDeleteRetentionDays { get; set; } = 90;

    /// <summary>
    /// Password reset token lifetime in minutes. Default: 30.
    /// </summary>
    public int PasswordResetTokenExpirationMinutes { get; set; } = 30;

    /// <summary>
    /// Cooldown per email/IP pair for requesting password reset in seconds. Default: 60.
    /// </summary>
    public int PasswordResetRequestCooldownSeconds { get; set; } = 60;

    /// <summary>
    /// Frontend base URL used to compose password reset links.
    /// </summary>
    public string PasswordResetFrontendBaseUrl { get; set; } = "http://localhost:3000";

    /// <summary>
    /// When true in development, forgot-password response includes debug token/link payload.
    /// Must remain false in production.
    /// </summary>
    public bool PasswordResetExposeTokenInDevelopment { get; set; } = false;

    /// <summary>Security:Jwt - token signing and lifetimes.</summary>
    public JwtSettings Jwt { get; set; } = new();

    /// <summary>Security:RefreshCookie - the refresh-token cookie (closed-alpha WP4).</summary>
    public RefreshCookieSettings RefreshCookie { get; set; } = new();

    /// <summary>Security:Registration (closed-alpha WP5).</summary>
    public RegistrationSettings Registration { get; set; } = new();

    /// <summary>Security:Lockout - per-account login lockout (closed-alpha WP6).</summary>
    public LockoutSettings Lockout { get; set; } = new();
}

public class JwtSettings
{
    public string Issuer { get; set; } = "knk-api";
    public string Audience { get; set; } = "knk-app";

    /// <summary>HS256 key, at least 32 characters. Never committed: user-secrets in Development,
    /// the env file in production (closed-alpha D9).</summary>
    public string? Secret { get; set; }

    public int AccessTokenMinutes { get; set; } = 30;

    /// <summary>Refresh-token lifetime with "remember me" (persistent cookie).</summary>
    public int RefreshTokenDays { get; set; } = 30;

    /// <summary>Server-side refresh-token lifetime without "remember me" (the cookie itself is a
    /// session cookie).</summary>
    public int SessionRefreshHours { get; set; } = 12;

    /// <summary>A just-rotated refresh token presented again within this many seconds (two tabs
    /// refreshing at once) is refused without revoking its family; later reuse revokes the family.</summary>
    public int RefreshReuseGraceSeconds { get; set; } = 30;
}

public class RefreshCookieSettings
{
    /// <summary>Lax (default; the web app and API share one origin), Strict or None.</summary>
    public string SameSite { get; set; } = "Lax";
}

public class RegistrationSettings
{
    /// <summary>Web-first sign-up without a code from the game server (closed-alpha D2). Off.</summary>
    public bool AllowWebFirst { get; set; } = false;
}

public class LockoutSettings
{
    public int MaxFailures { get; set; } = 5;
    public int WindowMinutes { get; set; } = 15;
    public int LockMinutes { get; set; } = 15;
}
