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

        Task AddAsync(UserPermissionGroup membership);
        Task UpdateAsync(UserPermissionGroup membership);
        Task DeleteAsync(UserPermissionGroup membership);
    }
}
