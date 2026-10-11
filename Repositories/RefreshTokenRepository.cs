using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class RefreshTokenRepository : IRefreshTokenRepository
    {
        private readonly KnKDbContext _context;

        public RefreshTokenRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(RefreshToken token)
        {
            await _context.RefreshTokens.AddAsync(token);
            await _context.SaveChangesAsync();
        }

        public Task<RefreshToken?> GetByHashAsync(string tokenHash) =>
            _context.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == tokenHash);

        public Task SaveChangesAsync() => _context.SaveChangesAsync();

        // Load-and-save rather than ExecuteUpdate: a family or a user has a handful of rows, and
        // this keeps the repository usable with the EF InMemory provider in tests.
        public async Task<int> RevokeFamilyAsync(string familyId, DateTime revokedAt)
        {
            var active = await _context.RefreshTokens.Where(t => t.FamilyId == familyId && t.RevokedAt == null).ToListAsync();
            active.ForEach(t => t.RevokedAt = revokedAt);
            await _context.SaveChangesAsync();
            return active.Count;
        }

        public async Task<int> RevokeAllForUserAsync(int userId, DateTime revokedAt)
        {
            var active = await _context.RefreshTokens.Where(t => t.UserId == userId && t.RevokedAt == null).ToListAsync();
            active.ForEach(t => t.RevokedAt = revokedAt);
            await _context.SaveChangesAsync();
            return active.Count;
        }

        public Task<int> DeleteExpiredOrRevokedBeforeAsync(DateTime cutoff) =>
            _context.RefreshTokens
                .Where(t => t.ExpiresAt < cutoff || (t.RevokedAt != null && t.RevokedAt < cutoff))
                .ExecuteDeleteAsync();
    }
}
