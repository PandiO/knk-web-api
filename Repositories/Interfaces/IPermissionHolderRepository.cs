using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories;

public interface IPermissionHolderRepository
{
    Task<PermissionHolderListDto?> GetByIdAsync(int id);
    Task<PagedResult<PermissionHolderListDto>> SearchAsync(PagedQuery query);
}
