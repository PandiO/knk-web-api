using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class StreetRepository : IStreetRepository
    {
        private readonly KnKDbContext _context;

        public StreetRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Street>> GetAllAsync()
        {
            return await _context.Streets.ToListAsync();
        }

        public async Task<Street?> GetByIdAsync(int id)
        {
            return await _context.Streets
                .Include(s => s.Districts)
                .ThenInclude(d => d.Town)
                .Include(s => s.Structures)
                .FirstOrDefaultAsync(s => s.Id == id);
        }

        public async Task AddStreetAsync(Street street)
        {
            await _context.Streets.AddAsync(street);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateStreetAsync(Street street)
        {
            _context.Streets.Update(street);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteStreetAsync(int id)
        {
            var street = await _context.Streets.FindAsync(id);
            if (street != null)
            {
                _context.Streets.Remove(street);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<PagedResult<Street>> SearchAsync(PagedQuery query)
        {
            var queryable = _context.Streets.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(s => s.Name.ToLower().Contains(searchLower));
            }

            queryable = ApplySorting(queryable, query.SortBy, query.SortDescending);

            var totalCount = await queryable.CountAsync();

            // KNG-166: the list rows name the street's districts and their towns.
            var items = await queryable
                .Include(s => s.Districts)
                    .ThenInclude(d => d.Town)
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            return new PagedResult<Street>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        private IQueryable<Street> ApplySorting(IQueryable<Street> queryable, string? sortBy, bool sortDescending)
        {
            if (string.IsNullOrWhiteSpace(sortBy))
                return queryable.OrderBy(s => s.Name);

            return sortBy.ToLower() switch
            {
                "name" => sortDescending ? queryable.OrderByDescending(s => s.Name) : queryable.OrderBy(s => s.Name),
                "id" => sortDescending ? queryable.OrderByDescending(s => s.Id) : queryable.OrderBy(s => s.Id),
                _ => queryable.OrderBy(s => s.Name)
            };
        }
    }
}
