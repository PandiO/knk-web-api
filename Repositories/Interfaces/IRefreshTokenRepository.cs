using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories
{
    /// <summary>Hashed web refresh tokens (closed-alpha hardening WP4).</summary>
    public interface IRefreshTokenRepository
    {
        Task AddAsync(RefreshToken token);

        /// <summary>The token row with this SHA-256 hash, tracked, or null.</summary>
        Task<RefreshToken?> GetByHashAsync(string tokenHash);

        /// <summary>Saves changes made to tracked rows (rotation).</summary>
        Task SaveChangesAsync();

        /// <summary>Revokes every not-yet-revoked token of a family; returns how many.</summary>
        Task<int> RevokeFamilyAsync(string familyId, DateTime revokedAt);

        /// <summary>Revokes every not-yet-revoked token of a user; returns how many.</summary>
        Task<int> RevokeAllForUserAsync(int userId, DateTime revokedAt);

        /// <summary>Deletes tokens that expired or were revoked before <paramref name="cutoff"/>.</summary>
        Task<int> DeleteExpiredOrRevokedBeforeAsync(DateTime cutoff);
    }
}
