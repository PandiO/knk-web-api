using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class TownRepository : ITownRepository
    {
        private readonly KnKDbContext _context;

        public TownRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Town>> GetAllAsync()
        {
            return await _context.Towns
                .Include(t => t.Location)
                .ToListAsync();
        }

        /// <summary>
        /// GET api/Towns/{id} (#409): read only, with the districts' streets so <see cref="Town.Streets"/> is filled.
        /// Split query: sub-regions, districts and streets would multiply rows in one join.
        /// </summary>
        public async Task<Town?> GetDetailAsync(int id)
        {
            return await _context.Towns
                .AsNoTracking()
                .Include(t => t.Location)
                .Include(t => t.SubRegions)
                .Include(t => t.Districts).ThenInclude(d => d.Streets)
                .AsSplitQuery()
                .FirstOrDefaultAsync(t => t.Id == id);
        }

        /// <summary>
        /// Tracked, for update and delete. Without the district streets on purpose: <c>Towns.Update</c> marks every
        /// loaded entity modified and would write each street back on a town save.
        /// </summary>
        public async Task<Town?> GetByIdAsync(int id)
        {
            return await _context.Towns
                .Include(t => t.Location)
                .Include(t => t.Districts)
                .Include(t => t.SubRegions)
                .FirstOrDefaultAsync(t => t.Id == id);
        }

        public async Task AddTownAsync(Town town)
        {
            await _context.Towns.AddAsync(town);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateTownAsync(Town town)
        {
            // #409: a town from GetByIdAsync is tracked already, so only what changed is saved. Update() would mark its
            // districts (and everything else loaded with it) modified and write them back on every town save.
            if (_context.Entry(town).State == EntityState.Detached) _context.Towns.Update(town);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteTownAsync(int id)
        {
            var town = await _context.Towns.FindAsync(id);
            if (town != null)
            {
                _context.Towns.Remove(town);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<PagedResult<Town>> SearchAsync(PagedQuery query)
        {
            var queryable = _context.Towns.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(t => t.Name.ToLower().Contains(searchLower) || 
                                                  t.Description.ToLower().Contains(searchLower));
            }

            queryable = ApplySorting(queryable, query.SortBy, query.SortDescending);

            var totalCount = await queryable.CountAsync();

            var items = await queryable
                .Include(t => t.Location)
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            return new PagedResult<Town>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        private IQueryable<Town> ApplySorting(IQueryable<Town> queryable, string? sortBy, bool sortDescending)
        {
            if (string.IsNullOrWhiteSpace(sortBy))
                return queryable.OrderBy(t => t.Name);

            return sortBy.ToLower() switch
            {
                "name" => sortDescending ? queryable.OrderByDescending(t => t.Name) : queryable.OrderBy(t => t.Name),
                "id" => sortDescending ? queryable.OrderByDescending(t => t.Id) : queryable.OrderBy(t => t.Id),
                "createdat" => sortDescending ? queryable.OrderByDescending(t => t.CreatedAt) : queryable.OrderBy(t => t.CreatedAt),
                _ => queryable.OrderBy(t => t.Name)
            };
        }
    }
}
