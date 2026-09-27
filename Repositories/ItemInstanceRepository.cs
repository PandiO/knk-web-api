using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    // Read side only: instances are minted by services inside the caller's own transaction
    // (docs/specs/lootboxes/DESIGN.md §3.2), so there is no Add/Update/Delete here.
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
