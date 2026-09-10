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
        Task<IEnumerable<GateStructure>> GetActiveGatesAsync();
        Task<bool> IsGateNameUniqueAsync(string name, int domainId, int? excludeId = null);
        Task<GateStructure?> FindGateByRegionAsync(string regionId);
        Task UpdateGateHealthAsync(int id, double newHealth);
        Task UpdateGateStateAsync(int id, bool isOpened, bool isDestroyed, bool isJammed);
        Task UpdateGateOperationalSettingsAsync(int id, bool isActive, bool isInvincible);
        
        // Block snapshot operations
        Task<IEnumerable<GateBlockSnapshot>> GetBlockSnapshotsByGateIdAsync(int gateId);
        Task AddBlockSnapshotAsync(GateBlockSnapshot snapshot);
        Task AddBlockSnapshotsAsync(IEnumerable<GateBlockSnapshot> snapshots);
        Task DeleteBlockSnapshotsByGateIdAsync(int gateId);

        // Opened-block snapshot operations - mirrors the block snapshot operations above
        // exactly, for the separately-scanned fully-open shape. See ROTATION_GAP_FILL_DESIGN.md.
        Task<IEnumerable<GateOpenedBlockSnapshot>> GetOpenedBlockSnapshotsByGateIdAsync(int gateId);
        Task AddOpenedBlockSnapshotAsync(GateOpenedBlockSnapshot snapshot);
        Task AddOpenedBlockSnapshotsAsync(IEnumerable<GateOpenedBlockSnapshot> snapshots);
        Task DeleteOpenedBlockSnapshotsByGateIdAsync(int gateId);
    }
}
