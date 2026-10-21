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
            .Include(a => a.LobbyLocation)
            .Include(a => a.ExitLocation)
            .Include(a => a.Street)
            .Include(a => a.District)
                .ThenInclude(d => d.Town)
            .Include(a => a.EligibleKits)
                .ThenInclude(k => k.Kit)
            .Include(a => a.Battlegrounds)
                .ThenInclude(b => b.SpawnALocation)
            .Include(a => a.Battlegrounds)
                .ThenInclude(b => b.SpawnBLocation)
            .Include(a => a.SpectatorAreas)
                .ThenInclude(s => s.Location)
            .AsSplitQuery();

        private IQueryable<ArenaBattleground> Battlegrounds() => _context.ArenaBattlegrounds
            .Include(b => b.Arena)
            .Include(b => b.SpawnALocation)
            .Include(b => b.SpawnBLocation);

        private IQueryable<ArenaSpectatorArea> SpectatorAreas() => _context.ArenaSpectatorAreas
            .Include(a => a.Arena)
            .Include(a => a.Location);

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
                .Include(a => a.Battlegrounds), query);
        }

        public Task<List<ArenaBattleground>> GetBattlegroundsAsync(int? arenaId) => Battlegrounds()
            .Where(b => arenaId == null || b.ArenaId == arenaId)
            .OrderBy(b => b.ArenaId).ThenBy(b => b.SortOrder).ThenBy(b => b.Id)
            .ToListAsync();

        public Task<ArenaBattleground?> GetBattlegroundAsync(int id) => Battlegrounds().FirstOrDefaultAsync(b => b.Id == id);

        public Task<bool> BattlegroundKeyTakenAsync(int arenaId, string key, int? excludeId)
        {
            var lowered = key.Trim().ToLower();
            int self = excludeId ?? 0;
            return _context.ArenaBattlegrounds.AnyAsync(b => b.Id != self && b.ArenaId == arenaId && b.Key.ToLower() == lowered);
        }

        public async Task<PagedResult<ArenaBattleground>> SearchBattlegroundsAsync(PagedQuery query)
        {
            var queryable = Battlegrounds();
            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var term = query.SearchTerm.ToLower();
                queryable = queryable.Where(b => b.Name.ToLower().Contains(term) || b.Key.ToLower().Contains(term));
            }
            if (TryFilter(query, "arenaId", out int arenaId))
            {
                queryable = queryable.Where(b => b.ArenaId == arenaId);
            }
            return await PageAsync(queryable.OrderBy(b => b.ArenaId).ThenBy(b => b.SortOrder).ThenBy(b => b.Id), query);
        }

        public Task<List<ArenaSpectatorArea>> GetSpectatorAreasAsync(int? arenaId) => SpectatorAreas()
            .Where(a => arenaId == null || a.ArenaId == arenaId)
            .OrderBy(a => a.ArenaId).ThenBy(a => a.Id)
            .ToListAsync();

        public Task<ArenaSpectatorArea?> GetSpectatorAreaAsync(int id) => SpectatorAreas().FirstOrDefaultAsync(a => a.Id == id);

        public async Task<PagedResult<ArenaSpectatorArea>> SearchSpectatorAreasAsync(PagedQuery query)
        {
            var queryable = SpectatorAreas();
            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var term = query.SearchTerm.ToLower();
                queryable = queryable.Where(a => a.Name.ToLower().Contains(term));
            }
            if (TryFilter(query, "arenaId", out int arenaId))
            {
                queryable = queryable.Where(a => a.ArenaId == arenaId);
            }
            return await PageAsync(queryable.OrderBy(a => a.ArenaId).ThenBy(a => a.Id), query);
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
            _context.ArenaBattlegrounds.RemoveRange(arena.Battlegrounds);
            _context.ArenaSpectatorAreas.RemoveRange(arena.SpectatorAreas);
            _context.Arenas.Remove(arena);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteDomainAsync(Domain domain)
        {
            _context.Domains.Remove(domain);
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
