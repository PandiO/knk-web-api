using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories
{
    public interface IPermissionGrantRepository
    {
        Task<IEnumerable<PermissionGrant>> GetAllAsync();
        Task<PermissionGrant?> GetByIdAsync(int id);
        Task AddAsync(PermissionGrant grant);
        Task UpdateAsync(PermissionGrant grant);
        Task DeleteAsync(int id);
        Task<PagedResult<PermissionGrant>> SearchAsync(PagedQuery query);

        /// <summary>Every non-expired grant directly owned by the given holder. Used by
        /// PermissionResolutionService for the user's own direct-grant level.</summary>
        Task<List<PermissionGrant>> GetActiveGrantsForHolderAsync(int holderId, DateTime asOf);

        Task<bool> HolderExistsAsync(int holderId);
    }
}
