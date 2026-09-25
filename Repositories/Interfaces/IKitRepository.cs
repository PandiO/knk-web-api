using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories
{
    public interface IKitRepository
    {
        Task<IEnumerable<Kit>> GetAllAsync();
        Task<Kit?> GetByIdAsync(int id);
        Task AddAsync(Kit entity);
        Task UpdateAsync(Kit entity);
        Task DeleteAsync(int id);
        Task<PagedResult<Kit>> SearchAsync(PagedQuery query);

        /// <summary>Most recent claim for (kitId, userId), or null if never claimed - the
        /// cooldown check's own source (docs/specs/kits/DESIGN.md §2.3).</summary>
        Task<KitClaim?> GetLastClaimAsync(int kitId, int userId);

        /// <summary>The user's purchase row for a single-purchase-premium kit, or null if they
        /// haven't bought it (DESIGN.md §2.4).</summary>
        Task<KitPurchase?> GetPurchaseAsync(int kitId, int userId);

        /// <summary>Persists a new claim row and, if a balance was deducted for this claim,
        /// the mutated User in the SAME SaveChanges call - EF Core commits one SaveChangesAsync
        /// as a single DB transaction, which is what DESIGN.md §5.1's "deduct-then-record, one
        /// transaction, a failed deduction never produces a claim row" actually requires: this
        /// is the one place both writes must land together or not at all.</summary>
        Task<KitClaim> AddClaimAsync(KitClaim claim, User? userToPersist = null);

        /// <summary>Persists a new purchase row and the Gems-deducted User in one SaveChanges
        /// call, same atomicity reasoning as AddClaimAsync (DESIGN.md §5.2).</summary>
        Task<KitPurchase> AddPurchaseAsync(KitPurchase purchase, User userToPersist);
    }
}
