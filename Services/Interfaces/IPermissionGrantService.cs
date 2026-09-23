using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services
{
    public interface IPermissionGrantService
    {
        Task<IEnumerable<PermissionGrantDto>> GetAllAsync();
        Task<PermissionGrantDto?> GetByIdAsync(int id);
        Task<PermissionGrantDto> CreateAsync(PermissionGrantDto dto);
        Task UpdateAsync(int id, PermissionGrantDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<PermissionGrantListDto>> SearchAsync(PagedQueryDto query);
    }
}
