using System.Security.Claims;
using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// The per-request session check on a validated access token (closed-alpha hardening WP4):
    /// refresh JWTs from before this change are not bearer tokens, the token's "tv" must match the
    /// user's TokenVersion, and the user must still be active. Used by JwtBearer's
    /// OnTokenValidated (Program.cs) and by POST api/Auth/validate-token.
    /// </summary>
    public static class AccessTokenSessionCheck
    {
        public const string TokenVersionClaim = "tv";
        public const string SessionIdClaim = "sid";
        public const string LegacyTokenTypeClaim = "token_type";

        /// <summary>Null when the token may be used, otherwise why not (for the log, never the client).</summary>
        public static async Task<string?> FindProblemAsync(ClaimsPrincipal principal, IUserSessionStateCache sessions, CancellationToken cancellationToken = default)
        {
            if (principal.HasClaim(c => c.Type == LegacyTokenTypeClaim))
            {
                return "a refresh token can't be used as a bearer token";
            }

            var userId = RequirePermissionFilter.UserIdFrom(principal)
                ?? (int.TryParse(principal.FindFirst("uid")?.Value, out var uid) ? uid : null);
            if (userId == null)
            {
                return "no user id claim";
            }

            if (!int.TryParse(principal.FindFirst(TokenVersionClaim)?.Value, out var tokenVersion))
            {
                return "no session version claim (issued before sessions could be revoked)";
            }

            var state = await sessions.GetAsync(userId.Value, cancellationToken);
            if (state == null)
            {
                return "user not found";
            }
            if (!state.CanUseSessions)
            {
                return "user inactive or deleted";
            }
            return state.TokenVersion == tokenVersion ? null : "session revoked (token version changed)";
        }
    }
}
