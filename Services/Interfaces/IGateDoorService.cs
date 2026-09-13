using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services
{
    public interface IGateDoorService
    {
        Task<IEnumerable<GateDoorDto>> GetByStructureIdAsync(int gateStructureId);
        Task<GateDoorDto?> GetByIdAsync(int id);
        Task<GateDoorDto?> GetByIdWithSnapshotsAsync(int id);
        Task<GateDoorDto> CreateAsync(int gateStructureId, GateDoorDto gateDoorDto);
        Task UpdateAsync(int id, GateDoorDto gateDoorDto);
        Task DeleteAsync(int id);

        // Door-specific operations
        Task UpdateHealthAsync(int id, double newHealth);
        Task UpdateStateAsync(int id, GateDoorOpenState openedState, bool isDestroyed);
        Task UpdateOperationalSettingsAsync(int id, bool isActive, bool isInvincible);
        Task UpdateRegionDataAsync(int id, bool isOpenedRegion, string regionData);

        // Block snapshot operations
        Task<IEnumerable<GateBlockSnapshotDto>> GetBlockSnapshotsAsync(int gateDoorId);
        Task AddBlockSnapshotsAsync(int gateDoorId, IEnumerable<GateBlockSnapshotDto> snapshots);
        Task AddBlockSnapshotsAsync(int gateDoorId, IEnumerable<GateBlockSnapshotCreateDto> snapshots);
        Task ClearBlockSnapshotsAsync(int gateDoorId);

        // Opened-block snapshot operations - mirrors the block snapshot operations above
        // exactly, for the separately-scanned fully-open shape. See ROTATION_GAP_FILL_DESIGN.md.
        Task<IEnumerable<GateOpenedBlockSnapshotDto>> GetOpenedBlockSnapshotsAsync(int gateDoorId);
        Task AddOpenedBlockSnapshotsAsync(int gateDoorId, IEnumerable<GateOpenedBlockSnapshotDto> snapshots);
        Task AddOpenedBlockSnapshotsAsync(int gateDoorId, IEnumerable<GateOpenedBlockSnapshotCreateDto> snapshots);
        Task ClearOpenedBlockSnapshotsAsync(int gateDoorId);
    }
}
