using System;
using System.Security.Claims;
using System.Threading.Tasks;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Access tokens (JWT, HS256) and opaque refresh tokens (closed-alpha hardening WP4, D4).
    /// </summary>
    public interface ITokenService
    {
        /// <summary>
        /// A signed access token (Security:Jwt:AccessTokenMinutes, default 30) for <paramref name="user"/>,
        /// carrying the user's TokenVersion as "tv" and the refresh-token family as "sid".
        /// </summary>
        Task<string> GenerateAccessTokenAsync(User user, string? sessionFamilyId = null);

        /// <summary>A new opaque refresh token: 32 random bytes, base64url. Only its
        /// <see cref="HashRefreshToken"/> is stored.</summary>
        string GenerateRefreshToken();

        /// <summary>Lower-case hex SHA-256 of a refresh token.</summary>
        string HashRefreshToken(string refreshToken);

        /// <summary>Validates an access token's signature, issuer, audience and lifetime; null if invalid.
        /// The per-request session check (AccessTokenSessionCheck) is separate.</summary>
        Task<ClaimsPrincipal?> ValidateAccessTokenAsync(string token);

        /// <summary>The user id claim of a validated principal, or null.</summary>
        Task<int?> ExtractUserIdFromPrincipalAsync(ClaimsPrincipal principal);

        /// <summary>The "exp" of a JWT without validating it, or null when it isn't a JWT.</summary>
        Task<DateTime?> ExtractExpirationAsync(string token);

        /// <summary>True when the JWT is expired or can't be read.</summary>
        Task<bool> IsTokenExpiredAsync(string token);
    }
}
