using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class LandmarkRepository : ILandmarkRepository
    {
        private readonly KnKDbContext _context;

        public LandmarkRepository(KnKDbContext context)
        {
            _context = context;
        }

        private IQueryable<Landmark> WithRelations() =>
            _context.Landmarks.Include(l => l.Location).Include(l => l.Domain);

        public async Task<IReadOnlyList<Landmark>> GetAllAsync(string? world = null)
        {
            var queryable = WithRelations().AsNoTracking();
            if (!string.IsNullOrWhiteSpace(world))
            {
                var worldLower = world.Trim().ToLower();
                queryable = queryable.Where(l => l.Location.World != null && l.Location.World.ToLower() == worldLower);
            }
            return await queryable.OrderBy(l => l.Name).ThenBy(l => l.Id).ToListAsync();
        }

        public Task<Landmark?> GetByIdAsync(int id) =>
            WithRelations().FirstOrDefaultAsync(l => l.Id == id);

        public async Task AddAsync(Landmark entity)
        {
            await _context.Landmarks.AddAsync(entity);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(Landmark entity)
        {
            // A landmark loaded by GetByIdAsync is tracked: save only its own changes. Update() would also mark the
            // loaded Location and Domain as modified.
            if (_context.Entry(entity).State == EntityState.Detached)
            {
                _context.Landmarks.Update(entity);
            }
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(Landmark entity)
        {
            _context.Landmarks.Remove(entity);
            await _context.SaveChangesAsync();
        }

        public async Task<PagedResult<Landmark>> SearchAsync(PagedQuery query)
        {
            var queryable = WithRelations().AsNoTracking();

            if (query.Filters != null)
            {
                var filters = new Dictionary<string, string>(query.Filters, StringComparer.OrdinalIgnoreCase);
                if (filters.TryGetValue("domainId", out var domainText) && int.TryParse(domainText, out var domainId) && domainId > 0)
                {
                    queryable = queryable.Where(l => l.DomainId == domainId);
                }
                if (filters.TryGetValue("world", out var world) && !string.IsNullOrWhiteSpace(world))
                {
                    var worldLower = world.Trim().ToLower();
                    queryable = queryable.Where(l => l.Location.World != null && l.Location.World.ToLower() == worldLower);
                }
                if (filters.TryGetValue("navigationEnabled", out var navText) && bool.TryParse(navText, out var navigationEnabled))
                {
                    queryable = queryable.Where(l => l.NavigationEnabled == navigationEnabled);
                }
            }

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.Trim().ToLower();
                queryable = queryable.Where(l => l.Name.ToLower().Contains(searchLower)
                    || (l.Location.World != null && l.Location.World.ToLower().Contains(searchLower))
                    || (l.Domain != null && l.Domain.Name.ToLower().Contains(searchLower)));
            }

            var totalCount = await queryable.CountAsync();

            queryable = (query.SortBy ?? string.Empty).ToLowerInvariant() switch
            {
                "name" => query.SortDescending ? queryable.OrderByDescending(l => l.Name) : queryable.OrderBy(l => l.Name),
                "world" => query.SortDescending ? queryable.OrderByDescending(l => l.Location.World) : queryable.OrderBy(l => l.Location.World),
                "domainname" => query.SortDescending
                    ? queryable.OrderByDescending(l => l.Domain != null ? l.Domain.Name : null)
                    : queryable.OrderBy(l => l.Domain != null ? l.Domain.Name : null),
                _ => query.SortDescending ? queryable.OrderByDescending(l => l.Id) : queryable.OrderBy(l => l.Id)
            };

            var pageNumber = Math.Max(1, query.PageNumber);
            var pageSize = Math.Max(1, query.PageSize);
            var items = await queryable
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<Landmark>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public Task<bool> NameTakenAsync(string name, string world, int? exceptId)
        {
            var nameLower = name.Trim().ToLower();
            var worldLower = world.Trim().ToLower();
            return _context.Landmarks.AnyAsync(l => (exceptId == null || l.Id != exceptId)
                && l.Name.ToLower() == nameLower
                && l.Location.World != null && l.Location.World.ToLower() == worldLower);
        }

        public Task<Domain?> GetDomainAsync(int id) =>
            _context.Domains.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);
    }
}
