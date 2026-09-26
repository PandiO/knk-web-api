using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories
{
    public interface IUserPermissionGroupRepository
    {
        /// <summary>All of a user's memberships (active and expired), with PermissionGroup loaded.</summary>
        Task<List<UserPermissionGroup>> GetByUserAsync(int userId);

        /// <summary>All memberships of a group (active and expired), with PermissionGroup loaded.</summary>
        Task<List<UserPermissionGroup>> GetByGroupAsync(int permissionGroupId);

        Task<UserPermissionGroup?> GetAsync(int userId, int permissionGroupId);

        /// <summary>
        /// Rank memberships (a premium tier, or the group named <paramref name="defaultGroupName"/>)
        /// whose ExpiresAt falls in (<paramref name="after"/>, <paramref name="asOf"/>], with User and
        /// PermissionGroup loaded.
        /// </summary>
        Task<List<UserPermissionGroup>> GetRanksExpiredBetweenAsync(DateTime after, DateTime asOf, string defaultGroupName);

        /// <summary>
        /// Users who held a rank that has expired and now hold no active rank at all - they need
        /// putting back on Default.
        /// </summary>
        Task<List<User>> GetUsersLeftWithoutRankAsync(DateTime asOf, string defaultGroupName);

        Task AddAsync(UserPermissionGroup membership);
        Task UpdateAsync(UserPermissionGroup membership);
        Task DeleteAsync(UserPermissionGroup membership);
    }
}
