using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    /// <summary>
    /// Repository implementation for LinkCode entity operations.
    /// </summary>
    public class LinkCodeRepository : ILinkCodeRepository
    {
        private readonly KnKDbContext _context;

        public LinkCodeRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<LinkCode> CreateAsync(LinkCode linkCode)
        {
            await _context.LinkCodes.AddAsync(linkCode);
            await _context.SaveChangesAsync();
            return linkCode;
        }

        public async Task<LinkCode?> GetByCodeAsync(string code)
        {
            return await _context.LinkCodes
                .Include(lc => lc.User)
                .FirstOrDefaultAsync(lc => lc.Code == code);
        }

        public async Task<LinkCode?> GetLinkCodeByCodeAsync(string code)
        {
            return await GetByCodeAsync(code);
        }

        public async Task<LinkCode?> GetByIdAsync(int id)
        {
            return await _context.LinkCodes
                .Include(lc => lc.User)
                .FirstOrDefaultAsync(lc => lc.Id == id);
        }

        public async Task UpdateAsync(LinkCode linkCode)
        {
            _context.LinkCodes.Update(linkCode);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateLinkCodeStatusAsync(int id, LinkCodeStatus status)
        {
            var linkCode = await GetByIdAsync(id);
            if (linkCode != null)
            {
                linkCode.Status = status;
                if (status == LinkCodeStatus.Used)
                {
                    linkCode.UsedAt = DateTime.UtcNow;
                }
                await UpdateAsync(linkCode);
            }
        }

        public async Task<bool> TryMarkUsedAsync(int id)
        {
            var now = DateTime.UtcNow;
            if (_context.Database.IsRelational())
            {
                // UPDATE ... WHERE Id = @id AND Status = Active: the database decides who wins.
                var affected = await _context.LinkCodes
                    .Where(lc => lc.Id == id && lc.Status == LinkCodeStatus.Active)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(lc => lc.Status, LinkCodeStatus.Used)
                        .SetProperty(lc => lc.UsedAt, now));
                return affected == 1;
            }

            // EF InMemory (tests) has no ExecuteUpdate; it is single-threaded there anyway.
            var linkCode = await GetByIdAsync(id);
            if (linkCode == null || linkCode.Status != LinkCodeStatus.Active)
            {
                return false;
            }
            linkCode.Status = LinkCodeStatus.Used;
            linkCode.UsedAt = now;
            await UpdateAsync(linkCode);
            return true;
        }

        public async Task DeleteAsync(int id)
        {
            var linkCode = await GetByIdAsync(id);
            if (linkCode != null)
            {
                _context.LinkCodes.Remove(linkCode);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<IEnumerable<LinkCode>> GetExpiredAsync()
        {
            var now = DateTime.UtcNow;
            return await _context.LinkCodes
                .Where(lc => lc.ExpiresAt < now && lc.Status != LinkCodeStatus.Used)
                .ToListAsync();
        }

        public async Task<IEnumerable<LinkCode>> GetExpiredLinkCodesAsync()
        {
            return await GetExpiredAsync();
        }

        public async Task<LinkCode?> GetActivePasswordResetTokenAsync(string hashedToken)
        {
            var now = DateTime.UtcNow;
            return await _context.LinkCodes
                .Include(lc => lc.User)
                .FirstOrDefaultAsync(lc =>
                    lc.Code == hashedToken &&
                    lc.Status == LinkCodeStatus.Active &&
                    lc.ExpiresAt > now &&
                    lc.UserId != null &&
                    lc.Code.Length == 64);
        }

        public async Task InvalidateActivePasswordResetTokensAsync(int userId, int? excludeLinkCodeId = null)
        {
            var now = DateTime.UtcNow;
            var query = _context.LinkCodes.Where(lc =>
                lc.UserId == userId &&
                lc.Status == LinkCodeStatus.Active &&
                lc.ExpiresAt > now &&
                lc.Code.Length == 64);

            if (excludeLinkCodeId.HasValue)
            {
                query = query.Where(lc => lc.Id != excludeLinkCodeId.Value);
            }

            var activeTokens = await query.ToListAsync();
            if (activeTokens.Count == 0)
            {
                return;
            }

            foreach (var token in activeTokens)
            {
                token.Status = LinkCodeStatus.Expired;
            }

            _context.LinkCodes.UpdateRange(activeTokens);
            await _context.SaveChangesAsync();
        }
    }
}
