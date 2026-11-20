using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    // Siege shop catalogue (knk-workspace docs/specs/siege-shop/DESIGN.md § 6.1, PandiO/knk-workspace#867):
    // categories, entries (mode defaults and scenario-only) and a scenario's overrides.
    public class SiegeShopRepository : ISiegeShopRepository
    {
        private readonly KnKDbContext _context;

        public SiegeShopRepository(KnKDbContext context)
        {
            _context = context;
        }

        // ---- Categories ----

        public async Task<List<SiegeShopCategory>> GetCategoriesAsync() =>
            await _context.SiegeShopCategories.OrderBy(c => c.SortOrder).ThenBy(c => c.Id).ToListAsync();

        public async Task<SiegeShopCategory?> GetCategoryByIdAsync(int id) =>
            await _context.SiegeShopCategories.FirstOrDefaultAsync(c => c.Id == id);

        public async Task<SiegeShopCategory?> GetCategoryByKeyAsync(string key)
        {
            var lower = key.ToLower();
            return await _context.SiegeShopCategories.FirstOrDefaultAsync(c => c.Key.ToLower() == lower);
        }

        public async Task AddCategoryAsync(SiegeShopCategory category)
        {
            await _context.SiegeShopCategories.AddAsync(category);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateCategoryAsync(SiegeShopCategory category)
        {
            if (_context.Entry(category).State == EntityState.Detached) _context.SiegeShopCategories.Update(category);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteCategoryAsync(SiegeShopCategory category)
        {
            _context.SiegeShopCategories.Remove(category);
            await _context.SaveChangesAsync();
        }

        public async Task<PagedResult<SiegeShopCategory>> SearchCategoriesAsync(PagedQuery query)
        {
            var queryable = _context.SiegeShopCategories.AsQueryable();
            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(c => c.Name.ToLower().Contains(searchLower) || c.Key.ToLower().Contains(searchLower));
            }

            var totalCount = await queryable.CountAsync();
            queryable = query.SortBy?.ToLowerInvariant() switch
            {
                "name" => query.SortDescending ? queryable.OrderByDescending(c => c.Name) : queryable.OrderBy(c => c.Name),
                "key" => query.SortDescending ? queryable.OrderByDescending(c => c.Key) : queryable.OrderBy(c => c.Key),
                "id" => query.SortDescending ? queryable.OrderByDescending(c => c.Id) : queryable.OrderBy(c => c.Id),
                _ => query.SortDescending
                    ? queryable.OrderByDescending(c => c.SortOrder).ThenByDescending(c => c.Id)
                    : queryable.OrderBy(c => c.SortOrder).ThenBy(c => c.Id)
            };

            var items = await queryable
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();
            return Page(items, totalCount, query);
        }

        public async Task<int> CountEntriesInCategoryAsync(int categoryId) =>
            await _context.SiegeShopEntries.CountAsync(e => e.CategoryId == categoryId);

        // ---- Entries ----

        private IQueryable<SiegeShopEntry> EntriesWithNames() => _context.SiegeShopEntries
            .Include(e => e.Category)
            .Include(e => e.SiegeScenario)
            .Include(e => e.ItemBlueprint)
            .Include(e => e.SiegeWeaponTemplate);

        public async Task<List<SiegeShopEntry>> GetEntriesAsync() =>
            await EntriesWithNames().OrderBy(e => e.GameMode).ThenBy(e => e.SortOrder).ThenBy(e => e.Id).ToListAsync();

        public async Task<SiegeShopEntry?> GetEntryByIdAsync(int id) =>
            await EntriesWithNames().FirstOrDefaultAsync(e => e.Id == id);

        public async Task AddEntryAsync(SiegeShopEntry entry)
        {
            await _context.SiegeShopEntries.AddAsync(entry);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateEntryAsync(SiegeShopEntry entry)
        {
            // Loaded entities are tracked; Update() would also mark the loaded references Modified.
            if (_context.Entry(entry).State == EntityState.Detached) _context.SiegeShopEntries.Update(entry);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteEntryAsync(SiegeShopEntry entry)
        {
            // Its overrides cascade (ON DELETE CASCADE); loaded so EF removes them itself on every provider.
            await _context.SiegeScenarioShopOverrides.Where(o => o.SiegeShopEntryId == entry.Id).LoadAsync();
            _context.SiegeShopEntries.Remove(entry);
            await _context.SaveChangesAsync();
        }

        public async Task<PagedResult<SiegeShopEntry>> SearchEntriesAsync(PagedQuery query)
        {
            var queryable = EntriesWithNames();
            if (TryGetIntFilter(query, "siegeScenarioId", out var scenarioId))
                queryable = queryable.Where(e => e.SiegeScenarioId == scenarioId);
            if (TryGetIntFilter(query, "categoryId", out var categoryId))
                queryable = queryable.Where(e => e.CategoryId == categoryId);
            if (TryGetFilter(query, "gameMode", out var modeText) && Enum.TryParse<SiegeGameMode>(modeText, true, out var mode))
                queryable = queryable.Where(e => e.GameMode == mode);
            // defaultsOnly=true: the modes' default lists (the override form's entry picker); false: scenario-only entries.
            if (TryGetFilter(query, "defaultsOnly", out var defaultsText) && bool.TryParse(defaultsText, out var defaultsOnly))
                queryable = defaultsOnly ? queryable.Where(e => e.SiegeScenarioId == null) : queryable.Where(e => e.SiegeScenarioId != null);
            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(e =>
                    (e.DisplayName != null && e.DisplayName.ToLower().Contains(searchLower))
                    || (e.ItemBlueprint != null && e.ItemBlueprint.Name != null && e.ItemBlueprint.Name.ToLower().Contains(searchLower))
                    || (e.SiegeWeaponTemplate != null && e.SiegeWeaponTemplate.Name.ToLower().Contains(searchLower)));
            }

            var totalCount = await queryable.CountAsync();
            queryable = query.SortBy?.ToLowerInvariant() switch
            {
                "price" => query.SortDescending ? queryable.OrderByDescending(e => e.Price) : queryable.OrderBy(e => e.Price),
                "id" => query.SortDescending ? queryable.OrderByDescending(e => e.Id) : queryable.OrderBy(e => e.Id),
                _ => query.SortDescending
                    ? queryable.OrderByDescending(e => e.GameMode).ThenByDescending(e => e.SortOrder).ThenByDescending(e => e.Id)
                    : queryable.OrderBy(e => e.GameMode).ThenBy(e => e.SortOrder).ThenBy(e => e.Id)
            };

            var items = await queryable
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();
            return Page(items, totalCount, query);
        }

        public async Task<bool> EntryHasOverridesAsync(int entryId) =>
            await _context.SiegeScenarioShopOverrides.AnyAsync(o => o.SiegeShopEntryId == entryId);

        // ---- Overrides ----

        private IQueryable<SiegeScenarioShopOverride> OverridesWithEntries() => _context.SiegeScenarioShopOverrides
            .Include(o => o.SiegeShopEntry).ThenInclude(e => e.ItemBlueprint)
            .Include(o => o.SiegeShopEntry).ThenInclude(e => e.SiegeWeaponTemplate);

        public async Task<List<SiegeScenarioShopOverride>> GetOverridesAsync(int siegeScenarioId) =>
            await OverridesWithEntries().Where(o => o.SiegeScenarioId == siegeScenarioId).OrderBy(o => o.Id).ToListAsync();

        public async Task<SiegeScenarioShopOverride?> GetOverrideByIdAsync(int id) =>
            await OverridesWithEntries().FirstOrDefaultAsync(o => o.Id == id);

        public async Task<SiegeScenarioShopOverride?> GetOverrideAsync(int siegeScenarioId, int siegeShopEntryId) =>
            await _context.SiegeScenarioShopOverrides.FirstOrDefaultAsync(o =>
                o.SiegeScenarioId == siegeScenarioId && o.SiegeShopEntryId == siegeShopEntryId);

        public async Task AddOverrideAsync(SiegeScenarioShopOverride shopOverride)
        {
            await _context.SiegeScenarioShopOverrides.AddAsync(shopOverride);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateOverrideAsync(SiegeScenarioShopOverride shopOverride)
        {
            if (_context.Entry(shopOverride).State == EntityState.Detached) _context.SiegeScenarioShopOverrides.Update(shopOverride);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteOverrideAsync(SiegeScenarioShopOverride shopOverride)
        {
            _context.SiegeScenarioShopOverrides.Remove(shopOverride);
            await _context.SaveChangesAsync();
        }

        // ---- References ----

        public async Task<bool> ScenarioExistsAsync(int siegeScenarioId) =>
            await _context.SiegeScenarios.AnyAsync(s => s.Id == siegeScenarioId);

        public async Task<bool> ItemBlueprintExistsAsync(int itemBlueprintId) =>
            await _context.ItemBlueprints.AnyAsync(b => b.Id == itemBlueprintId);

        public async Task<bool> WeaponTemplateExistsAsync(int templateId) =>
            await _context.SiegeWeaponTemplates.AnyAsync(t => t.Id == templateId);

        // ---- Effective catalogue ----

        public async Task<List<SiegeShopEntry>> GetEnabledEntriesForCatalogueAsync(SiegeGameMode mode, int? siegeScenarioId) =>
            await _context.SiegeShopEntries.AsNoTracking()
                .Include(e => e.Category)
                .Include(e => e.ItemBlueprint)
                .Include(e => e.SiegeWeaponTemplate)
                .Where(e => e.Enabled && e.GameMode == mode
                    && (e.SiegeScenarioId == null || (siegeScenarioId != null && e.SiegeScenarioId == siegeScenarioId)))
                .ToListAsync();

        // ---- Helpers ----

        private static PagedResult<T> Page<T>(List<T> items, int totalCount, PagedQuery query) => new()
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        };

        private static bool TryGetFilter(PagedQuery query, string key, out string value)
        {
            value = string.Empty;
            if (query.Filters == null) return false;
            var match = query.Filters.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
            if (match.Key == null || string.IsNullOrWhiteSpace(match.Value)) return false;
            value = match.Value;
            return true;
        }

        private static bool TryGetIntFilter(PagedQuery query, string key, out int value)
        {
            value = 0;
            return TryGetFilter(query, key, out var text) && int.TryParse(text, out value) && value > 0;
        }
    }
}
