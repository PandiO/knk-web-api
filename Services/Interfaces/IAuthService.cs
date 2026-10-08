using System.Threading.Tasks;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Authentication service contract for login, refresh, logout, and current-user retrieval.
    /// </summary>
    public interface IAuthService
    {
        /// <summary>
        /// Authenticate a user with email and password, issuing access and refresh tokens.
        /// </summary>
        /// <param name="login">Email address or Minecraft username (case-insensitive)</param>
        /// <param name="password">Plain text password</param>
        /// <param name="rememberMe">Extended session flag</param>
        /// <returns>Tuple with success flag, response DTO, and optional error message</returns>
        Task<(bool Ok, AuthLoginResponseDto? Result, string? Error)> LoginAsync(string login, string password, bool rememberMe, string? clientIp = null, string? userAgent = null);

        /// <summary>
        /// Refresh an access token using a refresh token, rotating the refresh token.
        /// </summary>
        /// <param name="refreshToken">The opaque refresh token from the cookie</param>
        /// <returns>Tuple with success flag, response DTO, and optional error message. Reusing a
        /// rotated token revokes its whole family (closed-alpha WP4).</returns>
        Task<(bool Ok, AuthRefreshResponseDto? Result, string? Error)> RefreshAsync(string refreshToken, string? clientIp = null, string? userAgent = null);

        /// <summary>
        /// Logout the current session: revokes the presented refresh token's family.
        /// </summary>
        /// <param name="refreshToken">Refresh token to revoke (optional if stateless)</param>
        Task LogoutAsync(string? refreshToken);

        /// <summary>
        /// Ends every session of a user (closed-alpha WP4, D5): bumps TokenVersion, revokes all
        /// refresh tokens and drops the cached session state.
        /// </summary>
        Task RevokeAllSessionsAsync(int userId, string reason);

        /// <summary>
        /// Registers a web login for a Minecraft account with a code from /account link
        /// (closed-alpha WP5, D1): sets email and password and logs the player in.
        /// </summary>
        Task<AuthRegisterOutcome> RegisterAsync(AuthRegisterRequestDto request, string? clientIp = null, string? userAgent = null);

        /// <summary>
        /// Get the current authenticated user by ID.
        /// </summary>
        /// <param name="userId">Authenticated user ID</param>
        /// <returns>UserDto or null if not found/inactive</returns>
        Task<UserDto?> GetCurrentUserAsync(int userId);

        /// <summary>
        /// Update user account (email and/or password).
        /// </summary>
        /// <param name="userId">Authenticated user ID</param>
        /// <param name="request">Update request DTO</param>
        /// <returns>Tuple with success flag, updated user DTO, and optional error message</returns>
        /// <remarks>A password or email change revokes every session and returns a fresh one for this tab.</remarks>
        Task<(bool Ok, AuthUpdateResponseDto? Result, string? Error)> UpdateUserAsync(int userId, AuthUpdateRequestDto request, string? currentRefreshToken = null, string? clientIp = null, string? userAgent = null);

        /// <summary>
        /// Initiate password reset flow for an email address.
        /// Always returns a generic success message to prevent account enumeration.
        /// </summary>
        /// <param name="email">Account email</param>
        /// <param name="clientIp">Caller IP for abuse controls</param>
        /// <param name="userAgent">Caller user-agent for audit logging</param>
        /// <param name="allowDebugPayload">Allow development-only debug payload</param>
        Task<AuthForgotPasswordResponseDto> RequestPasswordResetAsync(string email, string? clientIp, string? userAgent, bool allowDebugPayload);

        /// <summary>
        /// Complete password reset with a one-time reset token.
        /// </summary>
        /// <param name="request">Reset request payload</param>
        /// <returns>Tuple with success flag and optional error</returns>
        Task<(bool Ok, string? Error)> ResetPasswordAsync(AuthResetPasswordRequestDto request);
    }
}
