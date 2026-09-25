using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories
{
    public interface IPermissionGroupRepository
    {
        Task<IEnumerable<PermissionGroup>> GetAllAsync();
        Task<PermissionGroup?> GetByIdAsync(int id);

        /// <summary>Case-sensitive exact name lookup — used to resolve the well-known "Default"
        /// group at account-creation time (developer request, 2026-09-25). Null if not seeded.</summary>
        Task<PermissionGroup?> GetByNameAsync(string name);
        Task AddAsync(PermissionGroup group);
        Task UpdateAsync(PermissionGroup group);
        Task DeleteAsync(int id);
        Task<bool> HasChildrenAsync(int id);
        Task<PagedResult<PermissionGroup>> SearchAsync(PagedQuery query);

        /// <summary>All groups a user currently, non-expired-ly, belongs to, with their full
        /// single-parent inheritance chain eager-loaded, ordered by Weight descending. Used by
        /// PermissionResolutionService.</summary>
        Task<List<PermissionGroup>> GetActiveGroupsForUserAsync(int userId, DateTime asOf);

        /// <summary>
        /// Memberships in this group expiring within the next <paramref name="withinDays"/> days
        /// (docs/specs/user-management/IMPLEMENTATION_PLAN.md Phase 3 "premium expiring soon"
        /// view). Excludes already-expired and permanent (ExpiresAt=null) memberships — a
        /// permanent membership never "expires soon". User is eager-loaded for the username.
        /// </summary>
        Task<List<UserPermissionGroup>> GetExpiringMembershipsAsync(int groupId, int withinDays, DateTime asOf);
    }
}
