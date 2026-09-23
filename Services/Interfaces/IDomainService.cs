using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services
{
    public interface IDomainService
    {
        Task<IEnumerable<Domain>> GetAllAsync();
        Task<Domain?> GetByIdAsync(int id);
        Task<Domain> CreateAsync(Domain domain);
        Task UpdateAsync(int id, Domain domain);
        Task DeleteAsync(int id);
        Task<knkwebapi_v2.Dtos.DomainRegionDecisionDto?> GetByWgRegionNameAsync(string regionName);
        Task<Dictionary<int, knkwebapi_v2.Dtos.DomainRegionDecisionDto>> SearchDomainRegionDecisionAsync(DomainRegionQueryDto queryDto);
        Task<PagedResultDto<DomainListDto>> SearchAsync(PagedQueryDto query);
    }
}
