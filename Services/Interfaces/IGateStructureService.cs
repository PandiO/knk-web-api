using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services
{
    public interface IGateStructureService
    {
        Task<IEnumerable<GateStructureDto>> GetAllAsync();
        Task<GateStructureDto?> GetByIdAsync(int id);
        Task<GateStructureDto?> GetByIdWithSnapshotsAsync(int id);
        Task<IEnumerable<GateStructureDto>> GetGatesByDomainAsync(int domainId);
        Task<GateStructureDto> CreateAsync(GateStructureDto gateStructureDto);
        Task UpdateAsync(int id, GateStructureDto gateStructureDto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<GateStructureListDto>> SearchAsync(PagedQueryDto query);

        // Structure-level cascading overrides (decision 5.0-B)
        Task UpdateOverridesAsync(int id, GateStructureOverridesUpdateDto overridesDto);
    }
}
