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

        public async Task<Town?> GetByIdAsync(int id)
        {
            return await _context.Towns
                .Include(t => t.Location)
                .Include(t => t.Streets)
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
            _context.Towns.Update(town);
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
