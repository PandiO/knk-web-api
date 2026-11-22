using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services;
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
            _context.Landmarks.Include(l => l.Location).Include(l => l.ParentDomain).Include(l => l.SubRegions);

        public async Task<IReadOnlyList<Landmark>> GetAllAsync(string? world = null)
        {
            var queryable = WithRelations().AsNoTracking();
            if (!string.IsNullOrWhiteSpace(world))
            {
                var worldLower = world.Trim().ToLower();
                queryable = queryable.Where(l => l.Location != null && l.Location.World != null && l.Location.World.ToLower() == worldLower);
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
            // loaded Location and parent as modified.
            if (_context.Entry(entity).State == EntityState.Detached)
            {
                _context.Landmarks.Update(entity);
            }
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(Landmark entity)
        {
            // LD09: the landmarks that have this one as parent become top level (the database's SET NULL does the same
            // for rows this context has not loaded).
            var children = await _context.Landmarks.Where(l => l.ParentDomainId == entity.Id).ToListAsync();
            foreach (var child in children)
            {
                child.ParentDomainId = null;
                if (child.AreaKind == LandmarkAreaKind.LinkedDomain) child.AreaKind = LandmarkAreaKind.None;
            }
            _context.Landmarks.Remove(entity);
            await _context.SaveChangesAsync();
        }

        public async Task<PagedResult<Landmark>> SearchAsync(PagedQuery query)
        {
            var queryable = WithRelations().AsNoTracking();

            if (query.Filters != null)
            {
                var filters = new Dictionary<string, string>(query.Filters, StringComparer.OrdinalIgnoreCase);
                // `domainId` is the #708 name of the parent filter (the web app's domain panel until L4); both work.
                if ((filters.TryGetValue("parentDomainId", out var parentText) || filters.TryGetValue("domainId", out parentText))
                    && int.TryParse(parentText, out var parentId) && parentId > 0)
                {
                    queryable = queryable.Where(l => l.ParentDomainId == parentId);
                }
                if (filters.TryGetValue("world", out var world) && !string.IsNullOrWhiteSpace(world))
                {
                    var worldLower = world.Trim().ToLower();
                    queryable = queryable.Where(l => l.Location != null && l.Location.World != null && l.Location.World.ToLower() == worldLower);
                }
                if (filters.TryGetValue("navigationEnabled", out var navText) && bool.TryParse(navText, out var navigationEnabled))
                {
                    queryable = queryable.Where(l => l.NavigationEnabled == navigationEnabled);
                }
                if (filters.TryGetValue("areaKind", out var areaText)
                    && Enum.TryParse<LandmarkAreaKind>(areaText?.Trim(), true, out var areaKind) && Enum.IsDefined(areaKind))
                {
                    queryable = queryable.Where(l => l.AreaKind == areaKind);
                }
            }

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.Trim().ToLower();
                queryable = queryable.Where(l => l.Name.ToLower().Contains(searchLower)
                    || (l.Location != null && l.Location.World != null && l.Location.World.ToLower().Contains(searchLower))
                    || (l.ParentDomain != null && l.ParentDomain.Name.ToLower().Contains(searchLower)));
            }

            var totalCount = await queryable.CountAsync();

            queryable = (query.SortBy ?? string.Empty).ToLowerInvariant() switch
            {
                "name" => query.SortDescending ? queryable.OrderByDescending(l => l.Name) : queryable.OrderBy(l => l.Name),
                "world" => query.SortDescending
                    ? queryable.OrderByDescending(l => l.Location != null ? l.Location.World : null)
                    : queryable.OrderBy(l => l.Location != null ? l.Location.World : null),
                "domainname" or "parentdomainname" => query.SortDescending
                    ? queryable.OrderByDescending(l => l.ParentDomain != null ? l.ParentDomain.Name : null)
                    : queryable.OrderBy(l => l.ParentDomain != null ? l.ParentDomain.Name : null),
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
                && l.Location != null && l.Location.World != null && l.Location.World.ToLower() == worldLower);
        }

        public Task<Domain?> GetDomainAsync(int id) =>
            _context.Domains.AsNoTracking().FirstOrDefaultAsync(d => d.Id == id);

        public Task<string?> RegionTakenByAsync(string regionId, string? world, int? exceptDomainId) =>
            DomainRegionOwnership.TakenByAsync(_context, regionId, world, exceptDomainId: exceptDomainId);

        public Task<bool> LocationUsedByOtherDomainAsync(int locationId, int? exceptDomainId)
        {
            int self = exceptDomainId ?? 0;
            return _context.Domains.AnyAsync(d => d.LocationId == locationId && d.Id != self);
        }

        public async Task<IReadOnlyList<int>> AncestorIdsAsync(int domainId)
        {
            // LD23: the parents of every domain type, one step at a time, with a cycle guard.
            var parents = await DomainParents.ParentIdMapAsync(_context);
            return DomainParents.AncestorIds(domainId, id => parents.TryGetValue(id, out var parent) ? parent : null);
        }

        public async Task RemoveSubRegionsAsync(int domainId)
        {
            var rows = await _context.DomainSubRegions.Where(s => s.DomainId == domainId).ToListAsync();
            if (rows.Count == 0) return;
            _context.DomainSubRegions.RemoveRange(rows);
            await _context.SaveChangesAsync();
        }
    }
}
