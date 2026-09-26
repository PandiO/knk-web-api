using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    // Siege Phase 2 (docs/specs/siege-minigame/DESIGN.md §3.8): lobbies and their rotation join rows.
    public class SiegeLobbyRepository : ISiegeLobbyRepository
    {
        private readonly KnKDbContext _context;

        public SiegeLobbyRepository(KnKDbContext context)
        {
            _context = context;
        }

        private IQueryable<SiegeLobby> WithRotation() => _context.SiegeLobbies
            .Include(l => l.Rotation).ThenInclude(r => r.SiegeScenario);

        public async Task<IEnumerable<SiegeLobby>> GetAllAsync()
        {
            return await WithRotation().OrderBy(l => l.Name).ToListAsync();
        }

        public async Task<List<SiegeLobby>> GetEnabledAsync()
        {
            return await _context.SiegeLobbies
                .Include(l => l.Rotation)
                .Where(l => l.IsEnabled)
                .OrderBy(l => l.Id)
                .ToListAsync();
        }

        public async Task<SiegeLobby?> GetByIdAsync(int id)
        {
            return await WithRotation().FirstOrDefaultAsync(l => l.Id == id);
        }

        public async Task<SiegeLobby?> GetByKeyAsync(string key)
        {
            return await _context.SiegeLobbies.FirstOrDefaultAsync(l => l.Key == key);
        }

        public async Task AddAsync(SiegeLobby entity)
        {
            await _context.SiegeLobbies.AddAsync(entity);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(SiegeLobby entity)
        {
            // Loaded entities are already tracked; Update() would also mark the loaded scenarios Modified.
            if (_context.Entry(entity).State == EntityState.Detached) _context.SiegeLobbies.Update(entity);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(SiegeLobby entity)
        {
            // Rotation rows cascade; the scenarios stay.
            _context.SiegeLobbies.Remove(entity);
            await _context.SaveChangesAsync();
        }

        public async Task<PagedResult<SiegeLobby>> SearchAsync(PagedQuery query)
        {
            var queryable = _context.SiegeLobbies.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(l => l.Name.ToLower().Contains(searchLower) || l.Key.Contains(searchLower));
            }

            var totalCount = await queryable.CountAsync();

            queryable = query.SortBy switch
            {
                "name" => query.SortDescending ? queryable.OrderByDescending(l => l.Name) : queryable.OrderBy(l => l.Name),
                "key" => query.SortDescending ? queryable.OrderByDescending(l => l.Key) : queryable.OrderBy(l => l.Key),
                _ => query.SortDescending ? queryable.OrderByDescending(l => l.Id) : queryable.OrderBy(l => l.Id)
            };

            var items = await queryable
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .Include(l => l.Rotation)
                .ToListAsync();

            return new PagedResult<SiegeLobby>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        public async Task<bool> HasMatchHistoryAsync(int siegeLobbyId)
        {
            return await _context.SiegeMatches.AnyAsync(m => m.SiegeLobbyId == siegeLobbyId);
        }

        public async Task<List<int>> GetExistingScenarioIdsAsync(IEnumerable<int> siegeScenarioIds)
        {
            var ids = siegeScenarioIds.Distinct().ToList();
            return await _context.SiegeScenarios.Where(s => ids.Contains(s.Id)).Select(s => s.Id).ToListAsync();
        }
    }
}
