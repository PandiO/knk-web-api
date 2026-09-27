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

        /// <summary>Persists a new claim row. KitService calls it inside the user's locked
        /// transaction, after the claim's cost posting (ICurrencyService), so both commit together
        /// or not at all (DESIGN.md §5.1: a refused payment never produces a claim row).</summary>
        Task<KitClaim> AddClaimAsync(KitClaim claim);

        /// <summary>Persists a new purchase row; same transaction as its KIT_PURCHASE posting
        /// (DESIGN.md §5.2).</summary>
        Task<KitPurchase> AddPurchaseAsync(KitPurchase purchase);
    }
}
