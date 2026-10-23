using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class DistrictRepository : IDistrictRepository
    {
        private readonly KnKDbContext _context;

        public DistrictRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<District>> GetAllAsync()
        {
            return await _context.Districts
                .Include(d => d.Location)
                .Include(d => d.Town)
                .ToListAsync();
        }

        public async Task<District?> GetByIdAsync(int id)
        {
            return await _context.Districts
                .Include(d => d.Location)
                .Include(d => d.Town)
                .Include(d => d.Streets)
                .Include(d => d.Structures)
                .Include(d => d.SubRegions)
                .FirstOrDefaultAsync(d => d.Id == id);
        }

        public async Task AddDistrictAsync(District district)
        {
            await _context.Districts.AddAsync(district);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateDistrictAsync(District district)
        {
            _context.Districts.Update(district);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteDistrictAsync(int id)
        {
            var district = await _context.Districts.FindAsync(id);
            if (district != null)
            {
                _context.Districts.Remove(district);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<PagedResult<District>> SearchAsync(PagedQuery query)
        {
            var queryable = _context.Districts.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(d => d.Name.ToLower().Contains(searchLower) || 
                                                  d.Description.ToLower().Contains(searchLower));
            }

            if (query.Filters != null && query.Filters.TryGetValue("townId", out var townIdStr))
            {
                if (int.TryParse(townIdStr, out var townId))
                {
                    queryable = queryable.Where(d => d.TownId == townId);
                }
            }

            queryable = ApplySorting(queryable, query.SortBy, query.SortDescending);

            var totalCount = await queryable.CountAsync();

            var items = await queryable
                .Include(d => d.Location)
                .Include(d => d.Town)
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            return new PagedResult<District>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        private IQueryable<District> ApplySorting(IQueryable<District> queryable, string? sortBy, bool sortDescending)
        {
            if (string.IsNullOrWhiteSpace(sortBy))
                return queryable.OrderBy(d => d.Name);

            return sortBy.ToLower() switch
            {
                "name" => sortDescending ? queryable.OrderByDescending(d => d.Name) : queryable.OrderBy(d => d.Name),
                "id" => sortDescending ? queryable.OrderByDescending(d => d.Id) : queryable.OrderBy(d => d.Id),
                "createdat" => sortDescending ? queryable.OrderByDescending(d => d.CreatedAt) : queryable.OrderBy(d => d.CreatedAt),
                "townid" => sortDescending ? queryable.OrderByDescending(d => d.TownId) : queryable.OrderBy(d => d.TownId),
                _ => queryable.OrderBy(d => d.Name)
            };
        }
    }
}
