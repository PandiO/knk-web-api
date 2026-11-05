using System.Linq.Expressions;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    // Siege Survival phase 1 (knk-workspace docs/specs/siege-survival/DESIGN.md §10; PandiO/knk-workspace#41):
    // the survival rows of a scenario (settings, difficulty profiles, monster types, special waves, spawnzones,
    // target overrides, purchase points, Mystery Box pools and points) and the survival lobby settings.
    public class SiegeSurvivalRepository : ISiegeSurvivalRepository
    {
        private readonly KnKDbContext _context;

        public SiegeSurvivalRepository(KnKDbContext context)
        {
            _context = context;
        }

        // ---- Scenario settings ----

        private IQueryable<SiegeSurvivalSettings> SettingsWithGraph() => _context.SiegeSurvivalSettings
            .Include(s => s.SiegeScenario)
            .Include(s => s.SelectedKits).ThenInclude(k => k.Kit)
            .AsSplitQuery();

        public async Task<SiegeSurvivalSettings?> GetSettingsAsync(int siegeScenarioId) =>
            await SettingsWithGraph().FirstOrDefaultAsync(s => s.SiegeScenarioId == siegeScenarioId);

        public async Task<List<SiegeSurvivalSettings>> GetAllSettingsAsync() =>
            await _context.SiegeSurvivalSettings.Include(s => s.SiegeScenario).OrderBy(s => s.SiegeScenario.Name).ToListAsync();

        public async Task<Dictionary<int, int>> CountDifficultyProfilesAsync() =>
            await _context.SurvivalDifficultyProfiles
                .GroupBy(p => p.SiegeScenarioId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(g => g.Key, g => g.Count);

        // ---- Lobby settings ----

        public async Task<SurvivalLobbySettings?> GetLobbySettingsAsync(int siegeLobbyId) =>
            await _context.SurvivalLobbySettings.Include(l => l.SiegeLobby).FirstOrDefaultAsync(l => l.SiegeLobbyId == siegeLobbyId);

        public async Task<SiegeLobby?> GetLobbyAsync(int siegeLobbyId) =>
            await _context.SiegeLobbies.FirstOrDefaultAsync(l => l.Id == siegeLobbyId);

        // ---- The whole survival graph of a scenario ----

        public async Task<SurvivalScenarioGraph?> GetGraphAsync(int siegeScenarioId, SiegeScenario scenario)
        {
            var settings = await GetSettingsAsync(siegeScenarioId);
            var profiles = await _context.SurvivalDifficultyProfiles
                .Where(p => p.SiegeScenarioId == siegeScenarioId).OrderBy(p => p.SortOrder).ThenBy(p => p.Id).ToListAsync();
            var monsterTypes = await _context.SurvivalMonsterTypes
                .Where(m => m.SiegeScenarioId == siegeScenarioId).OrderBy(m => m.SortOrder).ThenBy(m => m.Id).ToListAsync();
            var specialProfiles = await _context.SurvivalSpecialWaveProfiles
                .Include(p => p.MonsterTypes)
                .Where(p => p.SiegeScenarioId == siegeScenarioId).OrderBy(p => p.SortOrder).ThenBy(p => p.Id).ToListAsync();
            var specialEntries = await _context.SurvivalSpecialWaveEntries
                .Include(e => e.SurvivalSpecialWaveProfile)
                .Where(e => e.SiegeScenarioId == siegeScenarioId).OrderBy(e => e.SortOrder).ThenBy(e => e.Id).ToListAsync();
            var zones = await SpawnZonesWithGraph()
                .Where(z => z.SiegeScenarioId == siegeScenarioId).OrderBy(z => z.SortOrder).ThenBy(z => z.Id).ToListAsync();
            var overrides = await _context.SurvivalTargetOverrides
                .Include(o => o.SiegeObjective)
                .Include(o => o.GateStructure)
                .Where(o => o.SiegeScenarioId == siegeScenarioId).OrderBy(o => o.Id).ToListAsync();
            var purchasePoints = await PurchasePointsWithGraph()
                .Where(p => p.SiegeScenarioId == siegeScenarioId).OrderBy(p => p.SortOrder).ThenBy(p => p.Id).ToListAsync();
            var pools = await PoolsWithGraph()
                .Where(p => p.SiegeScenarioId == siegeScenarioId).OrderBy(p => p.Id).ToListAsync();
            var boxPoints = await MysteryBoxPointsWithGraph()
                .Where(p => p.SiegeScenarioId == siegeScenarioId).OrderBy(p => p.SortOrder).ThenBy(p => p.Id).ToListAsync();

            var kitIds = new List<int>();
            if (settings != null)
            {
                kitIds.AddRange(settings.SelectedKits.Select(k => k.KitId));
                if (settings.DefaultKitId.HasValue) kitIds.Add(settings.DefaultKitId.Value);
                if (settings.BaselineLoadoutKitId.HasValue) kitIds.Add(settings.BaselineLoadoutKitId.Value);
            }
            var blueprintIds = purchasePoints.SelectMany(p => p.Offers).Select(o => o.ItemBlueprintId)
                .Concat(pools.SelectMany(p => p.Entries).Where(e => e.ItemBlueprintId.HasValue).Select(e => e.ItemBlueprintId!.Value));
            var enchantmentIds = pools.SelectMany(p => p.Entries)
                .SelectMany(e => e.Enchantments.Select(c => c.EnchantmentDefinitionId)
                    .Concat(e.EnchantmentDefinitionId.HasValue ? new[] { e.EnchantmentDefinitionId.Value } : Array.Empty<int>()));

            return new SurvivalScenarioGraph
            {
                Scenario = scenario,
                Settings = settings,
                DifficultyProfiles = profiles,
                MonsterTypes = monsterTypes,
                SpecialWaveProfiles = specialProfiles,
                SpecialWaveEntries = specialEntries,
                SpawnZones = zones,
                TargetOverrides = overrides,
                PurchasePoints = purchasePoints,
                MysteryBoxPools = pools,
                MysteryBoxPoints = boxPoints,
                ExistingKitIds = await ExistingKitIdsAsync(kitIds),
                ExistingItemBlueprintIds = await ExistingItemBlueprintIdsAsync(blueprintIds),
                ExistingEnchantmentDefinitionIds = await ExistingEnchantmentDefinitionIdsAsync(enchantmentIds)
            };
        }

        // ---- Generic child rows ----

        public async Task<T?> FindAsync<T>(int id) where T : class => await _context.Set<T>().FindAsync(id);

        public async Task<List<T>> ListAsync<T>(Expression<Func<T, bool>> where) where T : class =>
            await _context.Set<T>().Where(where).ToListAsync();

        public async Task AddAsync<T>(T entity) where T : class
        {
            await _context.Set<T>().AddAsync(entity);
            await _context.SaveChangesAsync();
        }

        public async Task SaveAsync<T>(T entity) where T : class
        {
            if (_context.Entry(entity).State == EntityState.Detached) _context.Set<T>().Update(entity);
            await _context.SaveChangesAsync();
        }

        public async Task RemoveAsync<T>(T entity) where T : class
        {
            _context.Set<T>().Remove(entity);
            await _context.SaveChangesAsync();
        }

        // ---- Loaded child rows ----

        private IQueryable<SurvivalSpawnZone> SpawnZonesWithGraph() => _context.SurvivalSpawnZones
            .Include(z => z.AnchorLocation)
            .Include(z => z.PrerequisiteObjectives)
            .Include(z => z.Points).ThenInclude(p => p.Location)
            .AsSplitQuery();

        private IQueryable<SurvivalPurchasePoint> PurchasePointsWithGraph() => _context.SurvivalPurchasePoints
            .Include(p => p.Location)
            .Include(p => p.Offers).ThenInclude(o => o.ItemBlueprint)
            .AsSplitQuery();

        private IQueryable<SurvivalMysteryBoxPool> PoolsWithGraph() => _context.SurvivalMysteryBoxPools
            .Include(p => p.Entries).ThenInclude(e => e.ItemBlueprint)
            .Include(p => p.Entries).ThenInclude(e => e.EnchantmentDefinition)
            .Include(p => p.Entries).ThenInclude(e => e.Enchantments).ThenInclude(c => c.EnchantmentDefinition)
            .AsSplitQuery();

        private IQueryable<SurvivalMysteryBoxPoint> MysteryBoxPointsWithGraph() => _context.SurvivalMysteryBoxPoints
            .Include(p => p.Location)
            .Include(p => p.SurvivalMysteryBoxPool);

        public async Task<SurvivalSpecialWaveProfile?> GetSpecialWaveProfileAsync(int id) =>
            await _context.SurvivalSpecialWaveProfiles.Include(p => p.MonsterTypes).FirstOrDefaultAsync(p => p.Id == id);

        public async Task<SurvivalSpecialWaveEntry?> GetSpecialWaveEntryAsync(int id) =>
            await _context.SurvivalSpecialWaveEntries.Include(e => e.SurvivalSpecialWaveProfile).FirstOrDefaultAsync(e => e.Id == id);

        public async Task<SurvivalSpawnZone?> GetSpawnZoneAsync(int id) =>
            await SpawnZonesWithGraph().FirstOrDefaultAsync(z => z.Id == id);

        public async Task<SurvivalSpawnZonePoint?> GetSpawnZonePointAsync(int id) =>
            await _context.SurvivalSpawnZonePoints.Include(p => p.Location).FirstOrDefaultAsync(p => p.Id == id);

        public async Task<SurvivalTargetOverride?> GetTargetOverrideAsync(int id) =>
            await _context.SurvivalTargetOverrides.Include(o => o.SiegeObjective).Include(o => o.GateStructure).FirstOrDefaultAsync(o => o.Id == id);

        public async Task<SurvivalPurchasePoint?> GetPurchasePointAsync(int id) =>
            await PurchasePointsWithGraph().FirstOrDefaultAsync(p => p.Id == id);

        public async Task<SurvivalPurchaseOffer?> GetPurchaseOfferAsync(int id) =>
            await _context.SurvivalPurchaseOffers.Include(o => o.ItemBlueprint).FirstOrDefaultAsync(o => o.Id == id);

        public async Task<SurvivalMysteryBoxPool?> GetMysteryBoxPoolAsync(int id) =>
            await PoolsWithGraph().FirstOrDefaultAsync(p => p.Id == id);

        public async Task<SurvivalMysteryBoxEntry?> GetMysteryBoxEntryAsync(int id) =>
            await _context.SurvivalMysteryBoxEntries
                .Include(e => e.ItemBlueprint)
                .Include(e => e.EnchantmentDefinition)
                .Include(e => e.Enchantments).ThenInclude(c => c.EnchantmentDefinition)
                .FirstOrDefaultAsync(e => e.Id == id);

        public async Task<SurvivalMysteryBoxPoint?> GetMysteryBoxPointAsync(int id) =>
            await MysteryBoxPointsWithGraph().FirstOrDefaultAsync(p => p.Id == id);

        // ---- Lookups ----

        public async Task<HashSet<int>> ExistingKitIdsAsync(IEnumerable<int> ids)
        {
            var list = ids.Distinct().ToList();
            if (list.Count == 0) return new HashSet<int>();
            return (await _context.Kits.Where(k => list.Contains(k.Id)).Select(k => k.Id).ToListAsync()).ToHashSet();
        }

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
            return (await _context.EnchantmentDefinitions.Where(e => list.Contains(e.Id)).Select(e => e.Id).ToListAsync()).ToHashSet();
        }

        public async Task<bool> LocationExistsAsync(int locationId) =>
            await _context.Locations.AnyAsync(l => l.Id == locationId);

        public async Task<bool> MysteryBoxPoolInUseAsync(int poolId) =>
            await _context.SurvivalMysteryBoxPoints.AnyAsync(p => p.SurvivalMysteryBoxPoolId == poolId);
    }
}
