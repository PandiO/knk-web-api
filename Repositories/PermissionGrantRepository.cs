using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class PermissionGrantRepository : IPermissionGrantRepository
    {
        private readonly KnKDbContext _context;

        public PermissionGrantRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<IEnumerable<PermissionGrant>> GetAllAsync()
        {
            return await _context.PermissionGrants.Include(g => g.Holder).ToListAsync();
        }

        public async Task<PermissionGrant?> GetByIdAsync(int id)
        {
            return await _context.PermissionGrants.Include(g => g.Holder).FirstOrDefaultAsync(g => g.Id == id);
        }

        public async Task AddAsync(PermissionGrant grant)
        {
            await _context.PermissionGrants.AddAsync(grant);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(PermissionGrant grant)
        {
            _context.PermissionGrants.Update(grant);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var grant = await _context.PermissionGrants.FindAsync(id);
            if (grant != null)
            {
                _context.PermissionGrants.Remove(grant);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<PagedResult<PermissionGrant>> SearchAsync(PagedQuery query)
        {
            var queryable = _context.PermissionGrants.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(g => g.Node.ToLower().Contains(searchLower));
            }

            if (query.Filters != null)
            {
                if (query.Filters.TryGetValue("holderId", out var holderIdStr) && int.TryParse(holderIdStr, out var holderId))
                {
                    queryable = queryable.Where(g => g.HolderId == holderId);
                }
            }

            queryable = ApplySorting(queryable, query.SortBy, query.SortDescending);

            var totalCount = await queryable.CountAsync();

            var items = await queryable
                .Include(g => g.Holder)
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            return new PagedResult<PermissionGrant>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        // The dashboard table sorts by the column it shows (PandiO/knk-workspace#408); node is the default and the
        // tie-breaker, then id, so paging stays stable when many rows share a value.
        private static IQueryable<PermissionGrant> ApplySorting(IQueryable<PermissionGrant> queryable, string? sortBy, bool descending)
        {
            IOrderedQueryable<PermissionGrant> ordered = sortBy?.ToLowerInvariant() switch
            {
                "id" => descending ? queryable.OrderByDescending(g => g.Id) : queryable.OrderBy(g => g.Id),
                "holderid" or "holder" => descending ? queryable.OrderByDescending(g => g.HolderId) : queryable.OrderBy(g => g.HolderId),
                "value" => descending ? queryable.OrderByDescending(g => g.Value) : queryable.OrderBy(g => g.Value),
                "expiresat" => descending ? queryable.OrderByDescending(g => g.ExpiresAt) : queryable.OrderBy(g => g.ExpiresAt),
                _ => descending ? queryable.OrderByDescending(g => g.Node) : queryable.OrderBy(g => g.Node),
            };
            return ordered.ThenBy(g => g.Node).ThenBy(g => g.Id);
        }

        public async Task<List<PermissionGrant>> GetActiveGrantsForHolderAsync(int holderId, DateTime asOf)
        {
            return await _context.PermissionGrants
                .Where(g => g.HolderId == holderId && (g.ExpiresAt == null || g.ExpiresAt > asOf))
                .ToListAsync();
        }

        public async Task<List<PermissionGrant>> GetGrantsForHolderNodeAsync(int holderId, string node)
        {
            return await _context.PermissionGrants
                .Where(g => g.HolderId == holderId && g.Node == node)
                .ToListAsync();
        }

        public async Task<bool> HolderExistsAsync(int holderId)
        {
            return await _context.PermissionHolders.AnyAsync(h => h.Id == holderId);
        }
    }
}
