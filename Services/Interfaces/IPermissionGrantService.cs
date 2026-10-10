using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services
{
    public interface IPermissionGrantService
    {
        Task<IEnumerable<PermissionGrantDto>> GetAllAsync();
        Task<PermissionGrantDto?> GetByIdAsync(int id);
        /// <summary>Upserts by (holder, node) - see UpsertByNodeAsync (KNG-59).</summary>
        Task<PermissionGrantDto> CreateAsync(PermissionGrantDto dto, int? actorUserId = null);

        /// <summary>Throws PermissionGrantConflictException when the row would move onto a node its
        /// (new) holder already has a grant for.</summary>
        Task UpdateAsync(int id, PermissionGrantDto dto, int? actorUserId = null);
        Task DeleteAsync(int id, int? actorUserId = null);
        Task<PagedResultDto<PermissionGrantListDto>> SearchAsync(PagedQueryDto query);

        /// <summary>Create-or-update the one active grant for (holderId, node) — used by callers
        /// (e.g. the plugin's rank/staff commands) that think in terms of "this holder has this
        /// node" rather than a specific grant row id.</summary>
        Task<PermissionGrantDto> UpsertByNodeAsync(int holderId, string node, bool value, DateTime? expiresAt, int? actorUserId = null);

        /// <summary>Revoke every active grant for (holderId, node) - normally one, but the generic
        /// create can leave a grant and a deny on the same node. Throws KeyNotFoundException
        /// if the holder has no active grant for that node.</summary>
        Task RevokeByNodeAsync(int holderId, string node, int? actorUserId = null);
    }
}
