using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    // Token grant rules (docs/specs/lootboxes/IMPLEMENTATION_PLAN.md Phase 5). The rules live in LootboxTokenGrantService.
    public class LootboxTokenGrantRepository : ILootboxTokenGrantRepository
    {
        private readonly KnKDbContext _context;

        public LootboxTokenGrantRepository(KnKDbContext context)
        {
            _context = context;
        }

        private IQueryable<LootboxTokenGrant> WithIncludes() => _context.LootboxTokenGrants
            .Include(g => g.LootboxType)
            .Include(g => g.PermissionGroup)
            .Include(g => g.Kit);

        public async Task<List<LootboxTokenGrant>> GetAllAsync()
        {
            return await WithIncludes().AsNoTracking().OrderBy(g => g.Id).ToListAsync();
        }

        public async Task<LootboxTokenGrant?> GetByIdAsync(int id)
        {
            return await WithIncludes().FirstOrDefaultAsync(g => g.Id == id);
        }

        public async Task<List<LootboxTokenGrant>> GetEnabledForGroupAsync(int permissionGroupId)
        {
            return await _context.LootboxTokenGrants.AsNoTracking()
                .Where(g => g.Enabled && g.PermissionGroupId == permissionGroupId)
                .OrderBy(g => g.Id)
                .ToListAsync();
        }

        public async Task<List<LootboxTokenGrant>> GetEnabledForKitAsync(int kitId)
        {
            return await _context.LootboxTokenGrants.AsNoTracking()
                .Where(g => g.Enabled && g.KitId == kitId)
                .OrderBy(g => g.Id)
                .ToListAsync();
        }

        public async Task AddAsync(LootboxTokenGrant grant)
        {
            await _context.LootboxTokenGrants.AddAsync(grant);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(LootboxTokenGrant grant)
        {
            _context.LootboxTokenGrants.Update(grant);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(LootboxTokenGrant grant)
        {
            _context.LootboxTokenGrants.Remove(grant);
            await _context.SaveChangesAsync();
        }

        public async Task<bool> TypeExistsAsync(int lootboxTypeId)
        {
            return await _context.LootboxTypes.AnyAsync(t => t.Id == lootboxTypeId);
        }

        public async Task<PermissionGroup?> GetPermissionGroupAsync(int permissionGroupId)
        {
            return await _context.PermissionGroups.AsNoTracking().FirstOrDefaultAsync(g => g.Id == permissionGroupId);
        }

        public async Task<bool> KitExistsAsync(int kitId)
        {
            return await _context.Kits.AnyAsync(k => k.Id == kitId);
        }
    }
}
