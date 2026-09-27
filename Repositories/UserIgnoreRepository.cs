using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class UserIgnoreRepository : IUserIgnoreRepository
    {
        private readonly KnKDbContext _context;

        public UserIgnoreRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<List<UserIgnore>> GetByUserAsync(int userId)
        {
            return await _context.UserIgnores
                .Include(i => i.IgnoredUser)
                .Where(i => i.UserId == userId)
                .OrderBy(i => i.CreatedAt)
                .ThenBy(i => i.Id)
                .ToListAsync();
        }

        public async Task<UserIgnore?> GetAsync(int userId, int ignoredUserId)
        {
            return await _context.UserIgnores
                .FirstOrDefaultAsync(i => i.UserId == userId && i.IgnoredUserId == ignoredUserId);
        }

        public async Task<int> CountByUserAsync(int userId)
        {
            return await _context.UserIgnores.CountAsync(i => i.UserId == userId);
        }

        public async Task AddAsync(UserIgnore ignore)
        {
            await _context.UserIgnores.AddAsync(ignore);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(UserIgnore ignore)
        {
            _context.UserIgnores.Remove(ignore);
            await _context.SaveChangesAsync();
        }
    }
}
