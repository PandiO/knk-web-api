using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace knkwebapi_v2.Services
{
    /// <summary>What every authenticated request checks about its user (closed-alpha WP4, D5).</summary>
    public sealed record UserSessionState(int TokenVersion, bool IsActive, DateTime? DeletedAt)
    {
        public bool CanUseSessions => IsActive && DeletedAt == null;
    }

    /// <summary>
    /// A 60-second in-memory cache of <see cref="UserSessionState"/> per user, so checking the
    /// "tv" claim on every request costs no database round trip. RevokeAllSessionsAsync
    /// invalidates the entry, so a bump takes effect at once on this instance (and within 60 s on
    /// any other instance).
    /// </summary>
    public interface IUserSessionStateCache
    {
        /// <summary>The user's current state, or null when the user doesn't exist.</summary>
        Task<UserSessionState?> GetAsync(int userId, CancellationToken cancellationToken = default);

        void Invalidate(int userId);
    }

    public class UserSessionStateCache : IUserSessionStateCache
    {
        public static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

        private readonly IMemoryCache _cache;
        private readonly IServiceScopeFactory _scopes;

        public UserSessionStateCache(IMemoryCache cache, IServiceScopeFactory scopes)
        {
            _cache = cache;
            _scopes = scopes;
        }

        private static string Key(int userId) => $"knk:session-state:{userId}";

        public async Task<UserSessionState?> GetAsync(int userId, CancellationToken cancellationToken = default)
        {
            if (_cache.TryGetValue(Key(userId), out UserSessionState? cached))
            {
                return cached;
            }

            using var scope = _scopes.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<KnKDbContext>();
            var state = await db.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new UserSessionState(u.TokenVersion, u.IsActive, u.DeletedAt))
                .FirstOrDefaultAsync(cancellationToken);

            // A missing user is cached too (as null), so a token for a deleted id can't hammer the DB.
            _cache.Set(Key(userId), state, Ttl);
            return state;
        }

        public void Invalidate(int userId) => _cache.Remove(Key(userId));
    }
}
