using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    // Mostly read side: instances are minted by services inside the caller's own transaction
    // (docs/specs/lootboxes/DESIGN.md §3.2). The one write is AddAndSaveAsync for /knk item more's clones
    // (PandiO/knk-workspace#175), which have no other row to save with. No Update/Delete.
    public class ItemInstanceRepository : IItemInstanceRepository
    {
        private readonly KnKDbContext _context;

        public ItemInstanceRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<ItemInstance?> GetByIdAsync(long id)
        {
            return await _context.ItemInstances
                .Include(i => i.ItemBlueprint)
                .Include(i => i.Grade)
                .Include(i => i.OwnerUser)
                .Include(i => i.Enchantments)
                    .ThenInclude(e => e.EnchantmentDefinition)
                .AsNoTracking()
                .FirstOrDefaultAsync(i => i.Id == id);
        }

        public Task<bool> UserExistsAsync(int userId)
        {
            return _context.Users.AnyAsync(u => u.Id == userId);
        }

        public async Task AddAndSaveAsync(IReadOnlyCollection<ItemInstance> instances)
        {
            _context.ItemInstances.AddRange(instances);
            await _context.SaveChangesAsync();
        }

        public async Task<HashSet<int>> GetExistingEnchantmentDefinitionIdsAsync(IEnumerable<int> definitionIds)
        {
            var ids = definitionIds.Distinct().ToList();
            if (ids.Count == 0) return new HashSet<int>();
            var existing = await _context.EnchantmentDefinitions
                .Where(d => ids.Contains(d.Id))
                .Select(d => d.Id)
                .ToListAsync();
            return existing.ToHashSet();
        }
    }
}
