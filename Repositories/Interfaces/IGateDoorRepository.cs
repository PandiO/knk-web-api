using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories
{
    public interface IGateDoorRepository
    {
        Task<IEnumerable<GateDoor>> GetByStructureIdAsync(int gateStructureId);
        Task<GateDoor?> GetByIdAsync(int id);
        Task<GateDoor?> GetByIdWithSnapshotsAsync(int id);
        Task AddAsync(GateDoor gateDoor);
        Task UpdateAsync(GateDoor gateDoor);
        Task DeleteAsync(int id);

        // Door-specific operations
        Task<IEnumerable<GateDoor>> GetActiveDoorsAsync();
        Task<bool> IsDoorNameUniqueAsync(int gateStructureId, string name, int? excludeId = null);
        Task<GateDoor?> FindDoorByRegionAsync(string regionId);
        Task UpdateHealthAsync(int id, double newHealth);
        Task UpdateStateAsync(int id, GateDoorOpenState openedState, bool isDestroyed);
        Task UpdateOperationalSettingsAsync(int id, bool isActive, bool isInvincible);

        // Block snapshot operations
        Task<IEnumerable<GateBlockSnapshot>> GetBlockSnapshotsByDoorIdAsync(int gateDoorId);
        Task AddBlockSnapshotAsync(GateBlockSnapshot snapshot);
        Task AddBlockSnapshotsAsync(IEnumerable<GateBlockSnapshot> snapshots);
        Task DeleteBlockSnapshotsByDoorIdAsync(int gateDoorId);

        // Opened-block snapshot operations - mirrors the block snapshot operations above
        // exactly, for the separately-scanned fully-open shape. See ROTATION_GAP_FILL_DESIGN.md.
        Task<IEnumerable<GateOpenedBlockSnapshot>> GetOpenedBlockSnapshotsByDoorIdAsync(int gateDoorId);
        Task AddOpenedBlockSnapshotAsync(GateOpenedBlockSnapshot snapshot);
        Task AddOpenedBlockSnapshotsAsync(IEnumerable<GateOpenedBlockSnapshot> snapshots);
        Task DeleteOpenedBlockSnapshotsByDoorIdAsync(int gateDoorId);
    }
}
