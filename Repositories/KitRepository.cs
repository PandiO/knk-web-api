using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    // Thin EF Core wrapper, same CRUD + paged-search shape as ItemBlueprintRepository
    // (docs/specs/kits/IMPLEMENTATION_PLAN.md §2).
    public class KitRepository : IKitRepository
    {
        private readonly KnKDbContext _context;

        public KitRepository(KnKDbContext context)
        {
            _context = context;
        }

        private IQueryable<Kit> WithIncludes() => _context.Kits
            .Include(k => k.Helmet)
            .Include(k => k.Chestplate)
            .Include(k => k.Leggings)
            .Include(k => k.Boots)
            .Include(k => k.Shield)
            .Include(k => k.Hand)
            .Include(k => k.Contents)
                .ThenInclude(c => c.ItemBlueprint)
            .Include(k => k.MinTitleBracket)
            .Include(k => k.RequiredPermissionGroup);

        public async Task<IEnumerable<Kit>> GetAllAsync()
        {
            return await WithIncludes().ToListAsync();
        }

        public async Task<Kit?> GetByIdAsync(int id)
        {
            return await WithIncludes().FirstOrDefaultAsync(k => k.Id == id);
        }

        public async Task AddAsync(Kit entity)
        {
            await _context.Kits.AddAsync(entity);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(Kit entity)
        {
            _context.Kits.Update(entity);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _context.Kits.FindAsync(id);
            if (entity != null)
            {
                _context.Kits.Remove(entity);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<PagedResult<Kit>> SearchAsync(PagedQuery query)
        {
            var queryable = _context.Kits.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var searchLower = query.SearchTerm.ToLower();
                queryable = queryable.Where(k =>
                    k.Name.ToLower().Contains(searchLower) ||
                    (k.Description != null && k.Description.ToLower().Contains(searchLower)));
            }

            var totalCount = await queryable.CountAsync();

            queryable = query.SortBy switch
            {
                "name" => query.SortDescending ? queryable.OrderByDescending(k => k.Name) : queryable.OrderBy(k => k.Name),
                _ => query.SortDescending ? queryable.OrderByDescending(k => k.Id) : queryable.OrderBy(k => k.Id)
            };

            var items = await queryable
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .Include(k => k.Contents)
                .ToListAsync();

            return new PagedResult<Kit>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        public async Task<KitClaim?> GetLastClaimAsync(int kitId, int userId)
        {
            return await _context.KitClaims
                .Where(c => c.KitId == kitId && c.UserId == userId)
                .OrderByDescending(c => c.ClaimedAt)
                .FirstOrDefaultAsync();
        }

        public async Task<KitPurchase?> GetPurchaseAsync(int kitId, int userId)
        {
            return await _context.KitPurchases
                .FirstOrDefaultAsync(p => p.KitId == kitId && p.UserId == userId);
        }

        public async Task<KitClaim> AddClaimAsync(KitClaim claim, User? userToPersist = null)
        {
            AttachIfDetached(userToPersist);
            await _context.KitClaims.AddAsync(claim);
            await _context.SaveChangesAsync();
            return claim;
        }

        public async Task<KitPurchase> AddPurchaseAsync(KitPurchase purchase, User userToPersist)
        {
            AttachIfDetached(userToPersist);
            await _context.KitPurchases.AddAsync(purchase);
            await _context.SaveChangesAsync();
            return purchase;
        }

        /// <summary>
        /// A user loaded through this context is tracked, so SaveChanges writes just the balance
        /// that changed. Users.Update() would rewrite the whole row from this request's copy
        /// (currency DESIGN.md §1.4 A2), so it is only used for a detached user.
        /// </summary>
        private void AttachIfDetached(User? user)
        {
            if (user != null && _context.Entry(user).State == EntityState.Detached)
            {
                _context.Users.Update(user);
            }
        }
    }
}
