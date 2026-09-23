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

            queryable = query.SortDescending ? queryable.OrderByDescending(g => g.Node) : queryable.OrderBy(g => g.Node);

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

        public async Task<List<PermissionGrant>> GetActiveGrantsForHolderAsync(int holderId, DateTime asOf)
        {
            return await _context.PermissionGrants
                .Where(g => g.HolderId == holderId && (g.ExpiresAt == null || g.ExpiresAt > asOf))
                .ToListAsync();
        }

        public async Task<bool> HolderExistsAsync(int holderId)
        {
            return await _context.PermissionHolders.AnyAsync(h => h.Id == holderId);
        }
    }
}
