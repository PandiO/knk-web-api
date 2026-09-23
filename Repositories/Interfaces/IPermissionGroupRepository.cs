using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories
{
    public interface IPermissionGroupRepository
    {
        Task<IEnumerable<PermissionGroup>> GetAllAsync();
        Task<PermissionGroup?> GetByIdAsync(int id);
        Task AddAsync(PermissionGroup group);
        Task UpdateAsync(PermissionGroup group);
        Task DeleteAsync(int id);
        Task<bool> HasChildrenAsync(int id);
        Task<PagedResult<PermissionGroup>> SearchAsync(PagedQuery query);

        /// <summary>All groups a user currently, non-expired-ly, belongs to, with their full
        /// single-parent inheritance chain eager-loaded, ordered by Weight descending. Used by
        /// PermissionResolutionService.</summary>
        Task<List<PermissionGroup>> GetActiveGroupsForUserAsync(int userId, DateTime asOf);
    }
}
