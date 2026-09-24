using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services
{
    public interface IPermissionGrantService
    {
        Task<IEnumerable<PermissionGrantDto>> GetAllAsync();
        Task<PermissionGrantDto?> GetByIdAsync(int id);
        Task<PermissionGrantDto> CreateAsync(PermissionGrantDto dto, int? actorUserId = null);
        Task UpdateAsync(int id, PermissionGrantDto dto, int? actorUserId = null);
        Task DeleteAsync(int id, int? actorUserId = null);
        Task<PagedResultDto<PermissionGrantListDto>> SearchAsync(PagedQueryDto query);
    }
}
