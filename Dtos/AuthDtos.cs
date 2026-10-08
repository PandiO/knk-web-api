using System;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    /// <summary>
    /// A refresh token just issued (closed-alpha hardening WP4). Never serialized: AuthController
    /// puts it in the HttpOnly cookie and the JSON "refreshToken" field stays null.
    /// </summary>
    public sealed record IssuedRefreshToken(string Token, DateTime ExpiresAt, bool RememberMe);

    /// <summary>
    /// DTO for login request.
    /// </summary>
    public class AuthLoginRequestDto
    {
        /// <summary>Email or Minecraft name (closed-alpha WP5, D3). Takes precedence over email.</summary>
        [JsonPropertyName("login")]
        public string? Login { get; set; }

        /// <summary>Kept for older clients; used when login is empty.</summary>
        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("password")]
        public string Password { get; set; } = null!;

        [JsonPropertyName("rememberMe")]
        public bool RememberMe { get; set; } = false;
    }

    /// <summary>
    /// DTO for login response.
    /// Contains access token, optional refresh token, expiration info, and user data.
    /// CRITICAL: Never includes password hash.
    /// </summary>
    public class AuthLoginResponseDto
    {
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; } = null!;

        /// <summary>Always null in responses since closed-alpha WP4: the refresh token travels only
        /// in the HttpOnly cookie. Kept so existing clients see the same shape.</summary>
        [JsonPropertyName("refreshToken")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expiresIn")]
        public int ExpiresIn { get; set; }

        [JsonPropertyName("user")]
        public UserDto User { get; set; } = null!;

        /// <summary>The refresh token for the cookie; not part of the JSON.</summary>
        [JsonIgnore]
        public IssuedRefreshToken? Session { get; set; }
    }

    /// <summary>
    /// POST api/Auth/register (closed-alpha WP5, D1): the code from /account link plus the email
    /// and password for the web login. The username is the verified Minecraft name.
    /// </summary>
    public class AuthRegisterRequestDto
    {
        [JsonPropertyName("linkCode")]
        public string? LinkCode { get; set; }

        [JsonPropertyName("email")]
        public string? Email { get; set; }

        [JsonPropertyName("password")]
        public string? Password { get; set; }

        [JsonPropertyName("passwordConfirmation")]
        public string? PasswordConfirmation { get; set; }
    }

    /// <summary>The result of AuthService.RegisterAsync: a login response, or an error code.</summary>
    public sealed class AuthRegisterOutcome
    {
        public const string RegistrationNeedsCode = "RegistrationNeedsCode";

        public bool Ok { get; private init; }
        public AuthLoginResponseDto? Result { get; private init; }
        public string? Error { get; private init; }
        public string? Message { get; private init; }

        public static AuthRegisterOutcome Success(AuthLoginResponseDto result) => new() { Ok = true, Result = result };
        public static AuthRegisterOutcome Fail(string error, string message) => new() { Error = error, Message = message };
    }

    /// <summary>
    /// DTO for token refresh request.
    /// RefreshToken can come from body or httpOnly cookie.
    /// </summary>
    public class AuthRefreshRequestDto
    {
        [JsonPropertyName("refreshToken")]
        public string? RefreshToken { get; set; }
    }

    /// <summary>
    /// DTO for token refresh response.
    /// Contains new access token, optional rotated refresh token, and expiration.
    /// </summary>
    public class AuthRefreshResponseDto
    {
        [JsonPropertyName("accessToken")]
        public string AccessToken { get; set; } = null!;

        /// <summary>Always null in responses since closed-alpha WP4: the refresh token travels only
        /// in the HttpOnly cookie. Kept so existing clients see the same shape.</summary>
        [JsonPropertyName("refreshToken")]
        public string? RefreshToken { get; set; }

        [JsonPropertyName("expiresIn")]
        public int ExpiresIn { get; set; }

        /// <summary>The rotated refresh token for the cookie; not part of the JSON.</summary>
        [JsonIgnore]
        public IssuedRefreshToken? Session { get; set; }
    }

    /// <summary>
    /// DTO for token validation request (optional endpoint).
    /// </summary>
    public class AuthValidateTokenRequestDto
    {
        [JsonPropertyName("token")]
        public string Token { get; set; } = null!;
    }

    /// <summary>
    /// DTO for token validation response (optional endpoint).
    /// </summary>
    public class AuthValidateTokenResponseDto
    {
        [JsonPropertyName("valid")]
        public bool Valid { get; set; }

        [JsonPropertyName("expiresAt")]
        public DateTime? ExpiresAt { get; set; }
    }

    /// <summary>
    /// DTO for updating user account (email and/or password).
    /// At least one field must be provided for update.
    /// </summary>
    public class AuthUpdateRequestDto
    {
        /// <summary>
        /// New email address (optional).
        /// </summary>
        [JsonPropertyName("email")]
        public string? Email { get; set; }

        /// <summary>
        /// Current password (required for password change, optional for email change).
        /// </summary>
        [JsonPropertyName("currentPassword")]
        public string? CurrentPassword { get; set; }

        /// <summary>
        /// New password (optional).
        /// </summary>
        [JsonPropertyName("newPassword")]
        public string? NewPassword { get; set; }
    }

    /// <summary>
    /// DTO for update user response.
    /// </summary>
    public class AuthUpdateResponseDto
    {
        [JsonPropertyName("user")]
        public UserDto User { get; set; } = null!;

        [JsonPropertyName("message")]
        public string Message { get; set; } = null!;

        /// <summary>
        /// A password or email change ends every session (closed-alpha WP4); this fresh access
        /// token (with a new refresh cookie) keeps the tab that made the change logged in. Null
        /// when nothing session-relevant changed.
        /// </summary>
        [JsonPropertyName("accessToken")]
        public string? AccessToken { get; set; }

        [JsonPropertyName("expiresIn")]
        public int? ExpiresIn { get; set; }

        [JsonIgnore]
        public IssuedRefreshToken? Session { get; set; }
    }

    /// <summary>
    /// DTO for forgot-password request.
    /// </summary>
    public class AuthForgotPasswordRequestDto
    {
        [JsonPropertyName("email")]
        public string Email { get; set; } = null!;
    }

    /// <summary>
    /// DTO for forgot-password response.
    /// Response is intentionally generic to prevent account enumeration.
    /// Debug fields are only populated in development when explicitly enabled.
    /// </summary>
    public class AuthForgotPasswordResponseDto
    {
        [JsonPropertyName("message")]
        public string Message { get; set; } = null!;

        [JsonPropertyName("debugResetToken")]
        public string? DebugResetToken { get; set; }

        [JsonPropertyName("debugResetUrl")]
        public string? DebugResetUrl { get; set; }
    }

    /// <summary>
    /// DTO for reset-password request.
    /// </summary>
    public class AuthResetPasswordRequestDto
    {
        [JsonPropertyName("token")]
        public string Token { get; set; } = null!;

        [JsonPropertyName("newPassword")]
        public string NewPassword { get; set; } = null!;

        [JsonPropertyName("passwordConfirmation")]
        public string PasswordConfirmation { get; set; } = null!;
    }

    /// <summary>
    /// DTO for reset-password response.
    /// </summary>
    public class AuthResetPasswordResponseDto
    {
        [JsonPropertyName("message")]
        public string Message { get; set; } = null!;
    }
}
