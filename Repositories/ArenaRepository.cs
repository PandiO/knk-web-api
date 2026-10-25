using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class ArenaRepository : IArenaRepository
    {
        private readonly KnKDbContext _context;

        public ArenaRepository(KnKDbContext context)
        {
            _context = context;
        }

        private IQueryable<Arena> Arenas() => _context.Arenas
            .Include(a => a.Location)
            .Include(a => a.ExitLocation)
            .Include(a => a.Street)
            .Include(a => a.District)
                .ThenInclude(d => d.Town)
            .Include(a => a.EligibleKits)
                .ThenInclude(k => k.Kit)
            .Include(a => a.SubRegions)
            .Include(a => a.Areas)
                .ThenInclude(b => b.SpawnALocation)
            .Include(a => a.Areas)
                .ThenInclude(b => b.SpawnBLocation)
            .Include(a => a.Areas)
                .ThenInclude(b => b.ArrivalLocation)
            .Include(a => a.Areas)
                .ThenInclude(b => b.TeleportMinTitleBracket)
            .Include(a => a.Areas)
                .ThenInclude(b => b.Regions)
            .AsSplitQuery();

        private IQueryable<ArenaArea> Areas() => _context.ArenaAreas
            .Include(a => a.Arena)
            .Include(a => a.SpawnALocation)
            .Include(a => a.SpawnBLocation)
            .Include(a => a.ArrivalLocation)
            .Include(a => a.TeleportMinTitleBracket)
            .Include(a => a.Regions)
            .AsSplitQuery();

        private IQueryable<ArenaAreaRegion> AreaRegions() => _context.ArenaAreaRegions
            .Include(r => r.ArenaArea);

        public Task<List<Arena>> GetAllAsync() => Arenas().OrderBy(a => a.Name).ToListAsync();

        public Task<Arena?> GetByIdAsync(int id) => Arenas().FirstOrDefaultAsync(a => a.Id == id);

        public Task<Arena?> GetByKeyAsync(string key)
        {
            var lowered = key.Trim().ToLower();
            return Arenas().FirstOrDefaultAsync(a => a.Key.ToLower() == lowered);
        }

        public Task<bool> KeyTakenAsync(string key, int? excludeId)
        {
            var lowered = key.Trim().ToLower();
            int self = excludeId ?? 0;
            return _context.Arenas.AnyAsync(a => a.Id != self && a.Key.ToLower() == lowered);
        }

        public async Task<PagedResult<Arena>> SearchAsync(PagedQuery query)
        {
            var queryable = _context.Arenas.AsQueryable();
            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var term = query.SearchTerm.ToLower();
                queryable = queryable.Where(a => a.Name.ToLower().Contains(term) || a.Key.ToLower().Contains(term));
            }
            if (TryFilter(query, "districtId", out int districtId))
            {
                queryable = queryable.Where(a => a.DistrictId == districtId);
            }
            queryable = (query.SortBy ?? "name").ToLower() switch
            {
                "id" => query.SortDescending ? queryable.OrderByDescending(a => a.Id) : queryable.OrderBy(a => a.Id),
                "key" => query.SortDescending ? queryable.OrderByDescending(a => a.Key) : queryable.OrderBy(a => a.Key),
                _ => query.SortDescending ? queryable.OrderByDescending(a => a.Name) : queryable.OrderBy(a => a.Name)
            };
            return await PageAsync(queryable
                .Include(a => a.District).ThenInclude(d => d.Town)
                .Include(a => a.Areas), query);
        }

        public Task<List<ArenaArea>> GetAreasAsync(int? arenaId) => Areas()
            .Where(a => arenaId == null || a.ArenaId == arenaId)
            .OrderBy(a => a.ArenaId).ThenBy(a => a.Kind).ThenBy(a => a.SortOrder).ThenBy(a => a.Id)
            .ToListAsync();

        public Task<ArenaArea?> GetAreaAsync(int id) => Areas().FirstOrDefaultAsync(a => a.Id == id);

        public Task<bool> AreaKeyTakenAsync(int arenaId, string key, int? excludeId)
        {
            var lowered = key.Trim().ToLower();
            int self = excludeId ?? 0;
            return _context.ArenaAreas.AnyAsync(a => a.Id != self && a.ArenaId == arenaId && a.Key.ToLower() == lowered);
        }

        public Task<bool> OtherLobbyExistsAsync(int arenaId, int? excludeId)
        {
            int self = excludeId ?? 0;
            return _context.ArenaAreas.AnyAsync(a => a.Id != self && a.ArenaId == arenaId && a.Kind == ArenaAreaKind.LOBBY);
        }

        public async Task<PagedResult<ArenaArea>> SearchAreasAsync(PagedQuery query)
        {
            var queryable = Areas();
            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var term = query.SearchTerm.ToLower();
                queryable = queryable.Where(a => a.Key.ToLower().Contains(term) || (a.Name != null && a.Name.ToLower().Contains(term)));
            }
            if (TryFilter(query, "arenaId", out int arenaId))
            {
                queryable = queryable.Where(a => a.ArenaId == arenaId);
            }
            if (query.Filters != null && query.Filters.TryGetValue("kind", out var rawKind)
                && Enum.TryParse<ArenaAreaKind>(rawKind, ignoreCase: true, out var kind))
            {
                queryable = queryable.Where(a => a.Kind == kind);
            }
            return await PageAsync(queryable.OrderBy(a => a.ArenaId).ThenBy(a => a.Kind).ThenBy(a => a.SortOrder).ThenBy(a => a.Id), query);
        }

        public Task<List<ArenaAreaRegion>> GetAreaRegionsAsync(int? areaId) => AreaRegions()
            .Where(r => areaId == null || r.ArenaAreaId == areaId)
            .OrderBy(r => r.ArenaAreaId).ThenBy(r => r.Id)
            .ToListAsync();

        public Task<ArenaAreaRegion?> GetAreaRegionAsync(int id) => AreaRegions().FirstOrDefaultAsync(r => r.Id == id);

        public Task<ArenaAreaRegion?> FindAreaRegionAsync(string regionId, string? world)
        {
            var loweredRegion = regionId.Trim().ToLower();
            var loweredWorld = string.IsNullOrWhiteSpace(world) ? null : world.Trim().ToLower();
            return AreaRegions()
                .Where(r => r.WgRegionId.ToLower() == loweredRegion && (loweredWorld == null || r.WorldName.ToLower() == loweredWorld))
                .OrderBy(r => r.Id)
                .FirstOrDefaultAsync();
        }

        public async Task<PagedResult<ArenaAreaRegion>> SearchAreaRegionsAsync(PagedQuery query)
        {
            var queryable = AreaRegions();
            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var term = query.SearchTerm.ToLower();
                queryable = queryable.Where(r => r.WgRegionId.ToLower().Contains(term));
            }
            if (TryFilter(query, "arenaAreaId", out int areaId))
            {
                queryable = queryable.Where(r => r.ArenaAreaId == areaId);
            }
            return await PageAsync(queryable.OrderBy(r => r.ArenaAreaId).ThenBy(r => r.Id), query);
        }

        public Task<List<int>> ExistingKitIdsAsync(IEnumerable<int> kitIds)
        {
            var ids = kitIds.Distinct().ToList();
            return _context.Kits.Where(k => ids.Contains(k.Id)).Select(k => k.Id).ToListAsync();
        }

        public Task<Location?> GetLocationAsync(int id) => _context.Locations.AsNoTracking().FirstOrDefaultAsync(l => l.Id == id);
        public Task<bool> TitleBracketExistsAsync(int id) => _context.TitleBrackets.AnyAsync(t => t.Id == id);
        public Task<bool> StreetExistsAsync(int id) => _context.Streets.AnyAsync(s => s.Id == id);
        public Task<bool> DistrictExistsAsync(int id) => _context.Districts.AnyAsync(d => d.Id == id);

        public async Task AddAsync(object entity)
        {
            await _context.AddAsync(entity);
            await _context.SaveChangesAsync();
        }

        public Task SaveChangesAsync() => _context.SaveChangesAsync();

        public async Task DeleteArenaAsync(Arena arena)
        {
            _context.ArenaKits.RemoveRange(arena.EligibleKits);
            foreach (var area in arena.Areas) _context.ArenaAreaRegions.RemoveRange(area.Regions);
            _context.ArenaAreas.RemoveRange(arena.Areas);
            _context.Arenas.Remove(arena);
            await _context.SaveChangesAsync();
        }

        public async Task RemoveAsync(object entity)
        {
            _context.Remove(entity);
            await _context.SaveChangesAsync();
        }

        private static bool TryFilter(PagedQuery query, string key, out int value)
        {
            value = 0;
            return query.Filters != null && query.Filters.TryGetValue(key, out var raw) && int.TryParse(raw, out value) && value > 0;
        }

        private static async Task<PagedResult<T>> PageAsync<T>(IQueryable<T> queryable, PagedQuery query)
        {
            int page = Math.Max(1, query.PageNumber);
            int size = Math.Clamp(query.PageSize, 1, 200);
            return new PagedResult<T>
            {
                TotalCount = await queryable.CountAsync(),
                Items = await queryable.Skip((page - 1) * size).Take(size).ToListAsync(),
                PageNumber = page,
                PageSize = size
            };
        }
    }
}
