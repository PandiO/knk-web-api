using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    // Siege Survival phase 4 (PandiO/knk-workspace#41, link S7a): the rows of the match-scoped Mystery Box roll.
    public class SurvivalMysteryBoxRollRepository : ISurvivalMysteryBoxRollRepository
    {
        private readonly KnKDbContext _context;

        public SurvivalMysteryBoxRollRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<SiegeMatch?> GetMatchAsync(int siegeMatchId) =>
            await _context.SiegeMatches.Include(m => m.Participants).FirstOrDefaultAsync(m => m.Id == siegeMatchId);

        public async Task<SurvivalMysteryBoxPoint?> GetPointWithPoolAsync(int pointId) =>
            await _context.SurvivalMysteryBoxPoints
                .Include(p => p.SurvivalMysteryBoxPool).ThenInclude(pool => pool.Entries).ThenInclude(e => e.ItemBlueprint)
                .Include(p => p.SurvivalMysteryBoxPool).ThenInclude(pool => pool.Entries).ThenInclude(e => e.EnchantmentDefinition)
                .Include(p => p.SurvivalMysteryBoxPool).ThenInclude(pool => pool.Entries).ThenInclude(e => e.Enchantments).ThenInclude(c => c.EnchantmentDefinition)
                .AsSplitQuery()
                .FirstOrDefaultAsync(p => p.Id == pointId);

        public async Task<HashSet<int>> ExistingItemBlueprintIdsAsync(IEnumerable<int> ids)
        {
            var list = ids.Distinct().ToList();
            if (list.Count == 0) return new HashSet<int>();
            return (await _context.ItemBlueprints.Where(b => list.Contains(b.Id)).Select(b => b.Id).ToListAsync()).ToHashSet();
        }

        public async Task<HashSet<int>> ExistingEnchantmentDefinitionIdsAsync(IEnumerable<int> ids)
        {
            var list = ids.Distinct().ToList();
            if (list.Count == 0) return new HashSet<int>();
            return (await _context.EnchantmentDefinitions.Where(d => list.Contains(d.Id)).Select(d => d.Id).ToListAsync()).ToHashSet();
        }

        public async Task<SurvivalMysteryBoxRoll?> GetRollAsync(string rollId) =>
            await _context.SurvivalMysteryBoxRolls.Include(r => r.Delivery).FirstOrDefaultAsync(r => r.RollId == rollId);

        public async Task<List<SurvivalMysteryBoxRoll>> ListRollsAsync(int siegeMatchId) =>
            await _context.SurvivalMysteryBoxRolls.Include(r => r.Delivery)
                .Where(r => r.SiegeMatchId == siegeMatchId)
                .OrderBy(r => r.RolledAt).ThenBy(r => r.Id)
                .ToListAsync();

        public async Task AddRollAsync(SurvivalMysteryBoxRoll roll)
        {
            _context.SurvivalMysteryBoxRolls.Add(roll);
            await _context.SaveChangesAsync();
        }

        public async Task AddDeliveryAsync(SurvivalMysteryBoxDelivery delivery)
        {
            _context.SurvivalMysteryBoxDeliveries.Add(delivery);
            await _context.SaveChangesAsync();
        }

        public Task SaveChangesAsync() => _context.SaveChangesAsync();
    }
}
