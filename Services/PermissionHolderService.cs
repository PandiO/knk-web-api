using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;

namespace knkwebapi_v2.Services;

public class PermissionHolderService : IPermissionHolderService
{
    private readonly IPermissionHolderRepository _repository;

    public PermissionHolderService(IPermissionHolderRepository repository)
    {
        _repository = repository;
    }

    public Task<PermissionHolderListDto?> GetByIdAsync(int id) => _repository.GetByIdAsync(id);

    public async Task<PagedResultDto<PermissionHolderListDto>> SearchAsync(PagedQueryDto query)
    {
        ArgumentNullException.ThrowIfNull(query);
        var result = await _repository.SearchAsync(new PagedQuery
        {
            PageNumber = query.PageNumber,
            PageSize = query.PageSize,
            SearchTerm = query.SearchTerm,
            SortBy = query.SortBy,
            SortDescending = query.SortDescending,
            Filters = query.Filters
        });
        return new PagedResultDto<PermissionHolderListDto>
        {
            Items = result.Items,
            TotalCount = result.TotalCount,
            PageNumber = result.PageNumber,
            PageSize = result.PageSize
        };
    }
}
