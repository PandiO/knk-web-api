using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services
{
    public interface IPermissionGroupService
    {
        Task<IEnumerable<PermissionGroupDto>> GetAllAsync();
        Task<PermissionGroupDto?> GetByIdAsync(int id);
        Task<PermissionGroupDto> CreateAsync(PermissionGroupDto dto);
        Task UpdateAsync(int id, PermissionGroupDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<PermissionGroupListDto>> SearchAsync(PagedQueryDto query);

        /// <summary>Moderation view: memberships in this group expiring within N days (docs/specs/user-management/IMPLEMENTATION_PLAN.md Phase 3).</summary>
        Task<IEnumerable<ExpiringMembershipDto>> GetExpiringMembershipsAsync(int groupId, int withinDays);
    }
}
