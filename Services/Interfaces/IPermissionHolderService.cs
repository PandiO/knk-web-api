using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services;

public interface IPermissionHolderService
{
    Task<PermissionHolderListDto?> GetByIdAsync(int id);
    Task<PagedResultDto<PermissionHolderListDto>> SearchAsync(PagedQueryDto query);
}
