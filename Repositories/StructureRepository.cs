using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class StructureRepository : IStructureRepository
    {
        private readonly KnKDbContext _context;

        public StructureRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<Structure>> GetAllAsync()
        {
            return await _context.Structures
                .Include(s => s.Location)
                .Include(s => s.Street)
                .Include(s => s.District)
                .ToListAsync();
        }

        public async Task<Structure?> GetByIdAsync(int id)
        {
            return await _context.Structures
                .Include(s => s.Location)
                .Include(s => s.Street)
                .Include(s => s.District)
                .FirstOrDefaultAsync(s => s.Id == id);
        }

        public async Task AddStructureAsync(Structure structure)
        {
            await _context.Structures.AddAsync(structure);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateStructureAsync(Structure structure)
        {
            _context.Structures.Update(structure);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteStructureAsync(int id)
        {
            var structure = await _context.Structures.FindAsync(id);
            if (structure != null)
            {
                _context.Structures.Remove(structure);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<PagedResult<Structure>> SearchAsync(PagedQuery query)
        {
            var queryable = _context.Structures.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(s => s.Name.ToLower().Contains(searchLower) || 
                                                  s.Description.ToLower().Contains(searchLower));
            }

            if (query.Filters != null)
            {
                if (query.Filters.TryGetValue("streetId", out var streetIdStr) && int.TryParse(streetIdStr, out var streetId))
                {
                    queryable = queryable.Where(s => s.StreetId == streetId);
                }
                if (query.Filters.TryGetValue("districtId", out var districtIdStr) && int.TryParse(districtIdStr, out var districtId))
                {
                    queryable = queryable.Where(s => s.DistrictId == districtId);
                }
            }

            queryable = ApplySorting(queryable, query.SortBy, query.SortDescending);

            var totalCount = await queryable.CountAsync();

            var items = await queryable
                .Include(s => s.Location)
                .Include(s => s.Street)
                .Include(s => s.District)
                    .ThenInclude(d => d.Town) // KNG-166: the list rows name the town
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            return new PagedResult<Structure>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        private IQueryable<Structure> ApplySorting(IQueryable<Structure> queryable, string? sortBy, bool sortDescending)
        {
            if (string.IsNullOrWhiteSpace(sortBy))
                return queryable.OrderBy(s => s.Name);

            return sortBy.ToLower() switch
            {
                "name" => sortDescending ? queryable.OrderByDescending(s => s.Name) : queryable.OrderBy(s => s.Name),
                "id" => sortDescending ? queryable.OrderByDescending(s => s.Id) : queryable.OrderBy(s => s.Id),
                "housenumber" => sortDescending ? queryable.OrderByDescending(s => s.HouseNumber) : queryable.OrderBy(s => s.HouseNumber),
                "createdat" => sortDescending ? queryable.OrderByDescending(s => s.CreatedAt) : queryable.OrderBy(s => s.CreatedAt),
                _ => queryable.OrderBy(s => s.Name)
            };
        }
    }
}
