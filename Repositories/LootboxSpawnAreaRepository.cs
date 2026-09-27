using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class LootboxSpawnAreaRepository : ILootboxSpawnAreaRepository
    {
        private readonly KnKDbContext _context;

        public LootboxSpawnAreaRepository(KnKDbContext context)
        {
            _context = context;
        }

        private IQueryable<LootboxSpawnArea> WithIncludes() => _context.LootboxSpawnAreas
            .Include(a => a.AllowedTypes)
                .ThenInclude(t => t.LootboxType);

        public async Task<IEnumerable<LootboxSpawnArea>> GetAllAsync()
        {
            return await WithIncludes().OrderBy(a => a.Name).ToListAsync();
        }

        public async Task<LootboxSpawnArea?> GetByIdAsync(int id)
        {
            return await WithIncludes().FirstOrDefaultAsync(a => a.Id == id);
        }

        public async Task AddAsync(LootboxSpawnArea entity)
        {
            await _context.LootboxSpawnAreas.AddAsync(entity);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(LootboxSpawnArea entity)
        {
            _context.LootboxSpawnAreas.Update(entity);
            await _context.SaveChangesAsync();
        }

        public async Task<List<int>> DeleteAsync(int id)
        {
            var entity = await _context.LootboxSpawnAreas.FindAsync(id);
            if (entity == null) return new List<int>();

            var active = await _context.LootboxSpawns
                .Where(s => s.SpawnAreaId == id && s.Status == LootboxSpawnStatus.Active)
                .ToListAsync();
            foreach (var spawn in active)
            {
                spawn.Status = LootboxSpawnStatus.Removed;
            }

            _context.LootboxSpawnAreas.Remove(entity);
            await _context.SaveChangesAsync();
            return active.Select(s => s.Id).ToList();
        }

        public async Task<PagedResult<LootboxSpawnArea>> SearchAsync(PagedQuery query)
        {
            var queryable = _context.LootboxSpawnAreas.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(a =>
                    a.Name.ToLower().Contains(searchLower) ||
                    a.WgRegionId.ToLower().Contains(searchLower) ||
                    a.World.ToLower().Contains(searchLower));
            }

            var totalCount = await queryable.CountAsync();

            queryable = query.SortBy switch
            {
                "name" => query.SortDescending ? queryable.OrderByDescending(a => a.Name) : queryable.OrderBy(a => a.Name),
                "enabled" => query.SortDescending ? queryable.OrderByDescending(a => a.Enabled) : queryable.OrderBy(a => a.Enabled),
                _ => query.SortDescending ? queryable.OrderByDescending(a => a.Id) : queryable.OrderBy(a => a.Id)
            };

            var items = await queryable
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .Include(a => a.AllowedTypes)
                    .ThenInclude(t => t.LootboxType)
                .ToListAsync();

            return new PagedResult<LootboxSpawnArea>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        public async Task<bool> NameTakenAsync(string name, int? excludeAreaId = null)
        {
            var lower = name.ToLower();
            return await _context.LootboxSpawnAreas.AnyAsync(a => a.Name.ToLower() == lower && (excludeAreaId == null || a.Id != excludeAreaId));
        }

        public async Task<bool> RegionTakenAsync(string world, string wgRegionId, int? excludeAreaId = null)
        {
            var worldLower = world.ToLower();
            var regionLower = wgRegionId.ToLower();
            return await _context.LootboxSpawnAreas.AnyAsync(a =>
                a.World.ToLower() == worldLower && a.WgRegionId.ToLower() == regionLower && (excludeAreaId == null || a.Id != excludeAreaId));
        }

        public async Task<HashSet<int>> GetExistingTypeIdsAsync(IEnumerable<int> typeIds)
        {
            var ids = typeIds.Distinct().ToList();
            if (ids.Count == 0) return new HashSet<int>();
            return (await _context.LootboxTypes.Where(t => ids.Contains(t.Id)).Select(t => t.Id).ToListAsync()).ToHashSet();
        }
    }
}
