using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    // Siege weapons phase 1 (knk-workspace docs/specs/siege-minigame/SIEGE_WEAPONS.md § 9, PandiO/knk-workspace#782):
    // templates with their ammo join rows, ammo types, a scenario's weapon spots and player unlocks.
    public class SiegeWeaponRepository : ISiegeWeaponRepository
    {
        private readonly KnKDbContext _context;

        public SiegeWeaponRepository(KnKDbContext context)
        {
            _context = context;
        }

        // ---- Templates ----

        private IQueryable<SiegeWeaponTemplate> TemplatesWithAmmo() => _context.SiegeWeaponTemplates
            .Include(t => t.AllowedAmmoTypes).ThenInclude(a => a.SiegeAmmoType);

        public async Task<List<SiegeWeaponTemplate>> GetTemplatesAsync() =>
            await TemplatesWithAmmo().OrderBy(t => t.Name).ToListAsync();

        public async Task<SiegeWeaponTemplate?> GetTemplateByIdAsync(int id) =>
            await TemplatesWithAmmo().FirstOrDefaultAsync(t => t.Id == id);

        public async Task<SiegeWeaponTemplate?> GetTemplateByNameAsync(string name)
        {
            var lower = name.ToLower();
            return await _context.SiegeWeaponTemplates.FirstOrDefaultAsync(t => t.Name.ToLower() == lower);
        }

        public async Task AddTemplateAsync(SiegeWeaponTemplate template)
        {
            await _context.SiegeWeaponTemplates.AddAsync(template);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateTemplateAsync(SiegeWeaponTemplate template)
        {
            // Loaded entities are tracked; Update() would also mark the loaded ammo types Modified.
            if (_context.Entry(template).State == EntityState.Detached) _context.SiegeWeaponTemplates.Update(template);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteTemplateAsync(SiegeWeaponTemplate template)
        {
            // Ammo join rows cascade; the ammo types stay.
            _context.SiegeWeaponTemplates.Remove(template);
            await _context.SaveChangesAsync();
        }

        public async Task<PagedResult<SiegeWeaponTemplate>> SearchTemplatesAsync(PagedQuery query)
        {
            var queryable = _context.SiegeWeaponTemplates.AsQueryable();
            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(t => t.Name.ToLower().Contains(searchLower));
            }

            var totalCount = await queryable.CountAsync();
            queryable = query.SortBy?.ToLowerInvariant() switch
            {
                "name" => query.SortDescending ? queryable.OrderByDescending(t => t.Name) : queryable.OrderBy(t => t.Name),
                "matchprice" => query.SortDescending ? queryable.OrderByDescending(t => t.MatchPrice) : queryable.OrderBy(t => t.MatchPrice),
                _ => query.SortDescending ? queryable.OrderByDescending(t => t.Id) : queryable.OrderBy(t => t.Id)
            };

            var items = await queryable
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .Include(t => t.AllowedAmmoTypes)
                .ToListAsync();
            return Page(items, totalCount, query);
        }

        public async Task<bool> TemplateHasUnlocksAsync(int templateId) =>
            await _context.UserSiegeWeaponUnlocks.AnyAsync(u => u.SiegeWeaponTemplateId == templateId);

        // ---- Ammo types ----

        public async Task<List<SiegeAmmoType>> GetAmmoTypesAsync() =>
            await _context.SiegeAmmoTypes.OrderBy(a => a.Name).ToListAsync();

        public async Task<SiegeAmmoType?> GetAmmoTypeByIdAsync(int id) =>
            await _context.SiegeAmmoTypes.FirstOrDefaultAsync(a => a.Id == id);

        public async Task<SiegeAmmoType?> GetAmmoTypeByNameAsync(string name)
        {
            var lower = name.ToLower();
            return await _context.SiegeAmmoTypes.FirstOrDefaultAsync(a => a.Name.ToLower() == lower);
        }

        public async Task<List<int>> GetExistingAmmoTypeIdsAsync(IEnumerable<int> ids)
        {
            var list = ids.Distinct().ToList();
            return await _context.SiegeAmmoTypes.Where(a => list.Contains(a.Id)).Select(a => a.Id).ToListAsync();
        }

        public async Task AddAmmoTypeAsync(SiegeAmmoType ammoType)
        {
            await _context.SiegeAmmoTypes.AddAsync(ammoType);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAmmoTypeAsync(SiegeAmmoType ammoType)
        {
            if (_context.Entry(ammoType).State == EntityState.Detached) _context.SiegeAmmoTypes.Update(ammoType);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAmmoTypeAsync(SiegeAmmoType ammoType)
        {
            _context.SiegeAmmoTypes.Remove(ammoType);
            await _context.SaveChangesAsync();
        }

        public async Task<PagedResult<SiegeAmmoType>> SearchAmmoTypesAsync(PagedQuery query)
        {
            var queryable = _context.SiegeAmmoTypes.AsQueryable();
            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(a => a.Name.ToLower().Contains(searchLower));
            }

            var totalCount = await queryable.CountAsync();
            queryable = query.SortBy?.ToLowerInvariant() switch
            {
                "name" => query.SortDescending ? queryable.OrderByDescending(a => a.Name) : queryable.OrderBy(a => a.Name),
                _ => query.SortDescending ? queryable.OrderByDescending(a => a.Id) : queryable.OrderBy(a => a.Id)
            };

            var items = await queryable
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();
            return Page(items, totalCount, query);
        }

        public async Task<List<string>> GetTemplateNamesAllowingAmmoTypeAsync(int ammoTypeId) =>
            await _context.SiegeWeaponTemplateAmmoTypes
                .Where(j => j.SiegeAmmoTypeId == ammoTypeId)
                .Select(j => j.SiegeWeaponTemplate.Name)
                .OrderBy(n => n)
                .ToListAsync();

        // ---- Weapon spots ----

        public async Task<SiegeScenario?> GetScenarioWithTeamsAndSpotsAsync(int siegeScenarioId) =>
            await _context.SiegeScenarios
                .Include(s => s.Teams)
                .Include(s => s.WeaponSpots)
                .AsSplitQuery()
                .FirstOrDefaultAsync(s => s.Id == siegeScenarioId);

        public async Task<List<SiegeWeaponSpot>> GetSpotsAsync(int siegeScenarioId) =>
            await _context.SiegeWeaponSpots
                .Include(w => w.SiegeTeam).ThenInclude(t => t.Clan)
                .Where(w => w.SiegeScenarioId == siegeScenarioId)
                .OrderBy(w => w.SortOrder).ThenBy(w => w.Id)
                .ToListAsync();

        public async Task<SiegeWeaponSpot?> GetSpotByIdAsync(int id) =>
            await _context.SiegeWeaponSpots
                .Include(w => w.SiegeTeam).ThenInclude(t => t.Clan)
                .FirstOrDefaultAsync(w => w.Id == id);

        public async Task AddSpotAsync(SiegeWeaponSpot spot)
        {
            await _context.SiegeWeaponSpots.AddAsync(spot);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateSpotAsync(SiegeWeaponSpot spot)
        {
            if (_context.Entry(spot).State == EntityState.Detached) _context.SiegeWeaponSpots.Update(spot);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteSpotAsync(SiegeWeaponSpot spot)
        {
            _context.SiegeWeaponSpots.Remove(spot);
            await _context.SaveChangesAsync();
        }

        // ---- Unlocks ----

        private IQueryable<UserSiegeWeaponUnlock> UnlocksWithNames() => _context.UserSiegeWeaponUnlocks
            .Include(u => u.User)
            .Include(u => u.SiegeWeaponTemplate);

        public async Task<List<UserSiegeWeaponUnlock>> GetUnlocksForUserAsync(int userId) =>
            await UnlocksWithNames().Where(u => u.UserId == userId).OrderBy(u => u.SiegeWeaponTemplate.Name).ToListAsync();

        public async Task<UserSiegeWeaponUnlock?> GetUnlockByIdAsync(int id) =>
            await UnlocksWithNames().FirstOrDefaultAsync(u => u.Id == id);

        public async Task<UserSiegeWeaponUnlock?> GetUnlockAsync(int userId, int templateId) =>
            await _context.UserSiegeWeaponUnlocks.FirstOrDefaultAsync(u => u.UserId == userId && u.SiegeWeaponTemplateId == templateId);

        public async Task<bool> UserExistsAsync(int userId) =>
            await _context.Users.AnyAsync(u => u.Id == userId && u.DeletedAt == null);

        public async Task AddUnlockAsync(UserSiegeWeaponUnlock unlock)
        {
            await _context.UserSiegeWeaponUnlocks.AddAsync(unlock);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateUnlockAsync(UserSiegeWeaponUnlock unlock)
        {
            if (_context.Entry(unlock).State == EntityState.Detached) _context.UserSiegeWeaponUnlocks.Update(unlock);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteUnlockAsync(UserSiegeWeaponUnlock unlock)
        {
            _context.UserSiegeWeaponUnlocks.Remove(unlock);
            await _context.SaveChangesAsync();
        }

        public async Task<PagedResult<UserSiegeWeaponUnlock>> SearchUnlocksAsync(PagedQuery query)
        {
            var queryable = UnlocksWithNames();
            if (TryGetIntFilter(query, "userId", out var userId))
                queryable = queryable.Where(u => u.UserId == userId);
            if (TryGetIntFilter(query, "siegeWeaponTemplateId", out var templateId))
                queryable = queryable.Where(u => u.SiegeWeaponTemplateId == templateId);
            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(u =>
                    u.User.Username.ToLower().Contains(searchLower) || u.SiegeWeaponTemplate.Name.ToLower().Contains(searchLower));
            }

            var totalCount = await queryable.CountAsync();
            var items = await (query.SortDescending
                    ? queryable.OrderByDescending(u => u.UnlockedAt).ThenByDescending(u => u.Id)
                    : queryable.OrderBy(u => u.UnlockedAt).ThenBy(u => u.Id))
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();
            return Page(items, totalCount, query);
        }

        // ---- Helpers ----

        private static PagedResult<T> Page<T>(List<T> items, int totalCount, PagedQuery query) => new()
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = query.PageNumber,
            PageSize = query.PageSize
        };

        private static bool TryGetIntFilter(PagedQuery query, string key, out int value)
        {
            value = 0;
            if (query.Filters == null) return false;
            var match = query.Filters.FirstOrDefault(f => string.Equals(f.Key, key, StringComparison.OrdinalIgnoreCase));
            return match.Key != null && int.TryParse(match.Value, out value) && value > 0;
        }
    }
}
