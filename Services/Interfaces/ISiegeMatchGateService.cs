using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    // Siege Phase 7a (docs/specs/siege-minigame/DESIGN.md §8.2, §8.4). Errors:
    // KeyNotFoundException -> 404, ArgumentException -> 400, InvalidOperationException -> 409.
    public interface ISiegeMatchGateService
    {
        Task<List<SiegeGateSnapshotDto>> GetSnapshotsAsync(int siegeMatchId);

        /// <summary>Snapshot → CurrentSiegeId → overrides for every listed gate, in one SaveChanges.</summary>
        Task<List<SiegeGateSnapshotDto>> LockdownAsync(int siegeMatchId, SiegeGateLockdownDto dto);

        /// <summary>Re-applies and deletes the match's snapshots; returns them for the plugin's runtime restore.</summary>
        Task<SiegeGateRestoreResultDto> RestoreAsync(int siegeMatchId);

        /// <summary>Startup recovery: restores every snapshot left behind and clears stale CurrentSiegeId markers.</summary>
        Task<SiegeGateRestoreResultDto> RestoreStaleAsync();
    }
}
