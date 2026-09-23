using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class PermissionGroupRepository : IPermissionGroupRepository
    {
        private readonly KnKDbContext _context;

        public PermissionGroupRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<PermissionGroup>> GetAllAsync()
        {
            return await _context.PermissionGroups
                .Include(g => g.ParentGroup)
                .ToListAsync();
        }

        public async Task<PermissionGroup?> GetByIdAsync(int id)
        {
            return await _context.PermissionGroups
                .Include(g => g.ParentGroup)
                .Include(g => g.ChildGroups)
                .Include(g => g.Grants)
                .FirstOrDefaultAsync(g => g.Id == id);
        }

        public async Task AddAsync(PermissionGroup group)
        {
            await _context.PermissionGroups.AddAsync(group);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(PermissionGroup group)
        {
            _context.PermissionGroups.Update(group);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var group = await _context.PermissionGroups.FindAsync(id);
            if (group != null)
            {
                _context.PermissionGroups.Remove(group);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<bool> HasChildrenAsync(int id)
        {
            return await _context.PermissionGroups.AnyAsync(g => g.ParentGroupId == id);
        }

        public async Task<PagedResult<PermissionGroup>> SearchAsync(PagedQuery query)
        {
            var queryable = _context.PermissionGroups.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(g => g.Name.ToLower().Contains(searchLower));
            }

            if (query.Filters != null && query.Filters.TryGetValue("parentGroupId", out var parentGroupIdStr)
                && int.TryParse(parentGroupIdStr, out var parentGroupId))
            {
                queryable = queryable.Where(g => g.ParentGroupId == parentGroupId);
            }

            queryable = string.Equals(query.SortBy, "weight", StringComparison.OrdinalIgnoreCase)
                ? (query.SortDescending ? queryable.OrderByDescending(g => g.Weight) : queryable.OrderBy(g => g.Weight))
                : (query.SortDescending ? queryable.OrderByDescending(g => g.Name) : queryable.OrderBy(g => g.Name));

            var totalCount = await queryable.CountAsync();

            var items = await queryable
                .Include(g => g.ParentGroup)
                .Include(g => g.ChildGroups)
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            return new PagedResult<PermissionGroup>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        public async Task<List<PermissionGroup>> GetActiveGroupsForUserAsync(int userId, DateTime asOf)
        {
            var directGroupIds = await _context.UserPermissionGroups
                .Where(m => m.UserId == userId && (m.ExpiresAt == null || m.ExpiresAt > asOf))
                .Select(m => m.PermissionGroupId)
                .ToListAsync();

            if (directGroupIds.Count == 0) return new List<PermissionGroup>();

            // Load the direct groups plus their full single-parent inheritance chain.
            // EF Core has no native arbitrary-depth recursive Include, so walk level by level;
            // group chains are expected to be shallow (a handful of levels at most).
            var byId = new Dictionary<int, PermissionGroup>();
            var frontier = await _context.PermissionGroups
                .Where(g => directGroupIds.Contains(g.Id))
                .ToListAsync();
            foreach (var g in frontier) byId[g.Id] = g;

            while (frontier.Count > 0)
            {
                var parentIds = frontier
                    .Where(g => g.ParentGroupId.HasValue && !byId.ContainsKey(g.ParentGroupId.Value))
                    .Select(g => g.ParentGroupId!.Value)
                    .Distinct()
                    .ToList();

                if (parentIds.Count == 0) break;

                var parents = await _context.PermissionGroups
                    .Where(g => parentIds.Contains(g.Id))
                    .ToListAsync();

                foreach (var p in parents) byId[p.Id] = p;
                frontier = parents;
            }

            // Wire up in-memory ParentGroup references from the loaded set so the resolution
            // engine can walk the chain without extra round-trips.
            foreach (var g in byId.Values)
            {
                if (g.ParentGroupId.HasValue && byId.TryGetValue(g.ParentGroupId.Value, out var parent))
                {
                    g.ParentGroup = parent;
                }
            }

            // Load every grant for every holder in the chain in one query.
            var allHolderIds = byId.Keys.ToList();
            var grants = await _context.PermissionGrants
                .Where(pg => allHolderIds.Contains(pg.HolderId))
                .ToListAsync();
            var grantsByHolder = grants.GroupBy(g => g.HolderId).ToDictionary(x => x.Key, x => x.ToList());
            foreach (var g in byId.Values)
            {
                g.Grants = grantsByHolder.TryGetValue(g.Id, out var list) ? list : new List<PermissionGrant>();
            }

            // Return only the directly-membered groups (ordered by weight desc); each carries its
            // resolved ParentGroup chain in memory for the resolution engine to walk.
            return directGroupIds
                .Where(byId.ContainsKey)
                .Select(id => byId[id])
                .OrderByDescending(g => g.Weight)
                .ThenBy(g => g.Id)
                .ToList();
        }
    }
}
