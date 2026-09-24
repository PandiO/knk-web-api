using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class UserPermissionGroupRepository : IUserPermissionGroupRepository
    {
        private readonly KnKDbContext _context;

        public UserPermissionGroupRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<List<UserPermissionGroup>> GetByUserAsync(int userId)
        {
            return await _context.UserPermissionGroups
                .Include(m => m.PermissionGroup)
                .Where(m => m.UserId == userId)
                .ToListAsync();
        }

        public async Task<List<UserPermissionGroup>> GetByGroupAsync(int permissionGroupId)
        {
            return await _context.UserPermissionGroups
                .Include(m => m.PermissionGroup)
                .Where(m => m.PermissionGroupId == permissionGroupId)
                .ToListAsync();
        }

        public async Task<UserPermissionGroup?> GetAsync(int userId, int permissionGroupId)
        {
            return await _context.UserPermissionGroups
                .Include(m => m.PermissionGroup)
                .FirstOrDefaultAsync(m => m.UserId == userId && m.PermissionGroupId == permissionGroupId);
        }

        public async Task AddAsync(UserPermissionGroup membership)
        {
            await _context.UserPermissionGroups.AddAsync(membership);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(UserPermissionGroup membership)
        {
            _context.UserPermissionGroups.Update(membership);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(UserPermissionGroup membership)
        {
            _context.UserPermissionGroups.Remove(membership);
            await _context.SaveChangesAsync();
        }
    }
}
