using knkwebapi_v2.Repositories;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// Ends every session of one user at once (closed-alpha hardening WP4, D5): bumps
    /// users.TokenVersion (so every access token already issued fails its "tv" check), revokes
    /// all of the user's refresh tokens and drops the cached session state. Called on a password
    /// change or reset, an email change, deactivation or deletion, a merge, and "sign out
    /// everywhere".
    /// </summary>
    public interface ISessionRevocationService
    {
        Task RevokeAllSessionsAsync(int userId, string reason);
    }

    public class SessionRevocationService : ISessionRevocationService
    {
        private readonly IUserRepository _users;
        private readonly IRefreshTokenRepository _refreshTokens;
        private readonly IUserSessionStateCache _sessionState;
        private readonly ILogger<SessionRevocationService> _logger;

        public SessionRevocationService(
            IUserRepository users,
            IRefreshTokenRepository refreshTokens,
            IUserSessionStateCache sessionState,
            ILogger<SessionRevocationService> logger)
        {
            _users = users;
            _refreshTokens = refreshTokens;
            _sessionState = sessionState;
            _logger = logger;
        }

        public async Task RevokeAllSessionsAsync(int userId, string reason)
        {
            var version = await _users.IncrementTokenVersionAsync(userId);
            var revoked = await _refreshTokens.RevokeAllForUserAsync(userId, DateTime.UtcNow);
            _sessionState.Invalidate(userId);
            _logger.LogInformation(
                "Revoked all sessions of user {UserId} ({Reason}): token version now {TokenVersion}, {Count} refresh tokens revoked",
                userId, reason, version, revoked);
        }
    }
}
