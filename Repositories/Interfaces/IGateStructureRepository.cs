using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories
{
    public interface IGateStructureRepository
    {
        Task<IEnumerable<GateStructure>> GetAllAsync();
        Task<GateStructure?> GetByIdAsync(int id);
        Task<GateStructure?> GetByIdWithSnapshotsAsync(int id);
        Task AddGateStructureAsync(GateStructure gateStructure);
        Task UpdateGateStructureAsync(GateStructure gateStructure);
        Task DeleteGateStructureAsync(int id);
        Task<PagedResult<GateStructure>> SearchAsync(PagedQuery query);

        // Gate-specific operations
        Task<IEnumerable<GateStructure>> GetGatesByDomainAsync(int domainId);
        Task<bool> IsGateNameUniqueAsync(string name, int domainId, int? excludeId = null);

        // Siege Phase 2: selected by a scenario, used by an objective, or snapshotted by a match.
        Task<bool> IsReferencedBySiegeAsync(int gateStructureId);
    }
}
