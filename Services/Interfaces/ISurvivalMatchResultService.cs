using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// Siege Survival phase 5 (knk-workspace docs/specs/siege-survival/DESIGN.md §9, §10, §11; PandiO/knk-workspace#41,
    /// link S8a): the durable survival match result (stored once, settles the v1 per-kill rewards through the ledger
    /// when the end reason settles), the match's latest checkpoint, and the hooks the Siege match lifecycle calls so
    /// a survival match never gets PvP rewards and an aborted one still has a result row.
    /// </summary>
    public interface ISurvivalMatchResultService
    {
        /// <summary>
        /// Stores the plugin's result and settles. Created = true on the first call; the same body again answers the
        /// stored result (Created = false); a different body throws ResultAlreadyStored.
        /// </summary>
        Task<(SurvivalMatchResultDto Result, bool Created)> PostResultAsync(int siegeMatchId, SurvivalMatchResultDto dto);

        Task<SurvivalMatchResultDto?> GetResultAsync(int siegeMatchId);

        Task<PagedResultDto<SurvivalMatchResultDto>> QueryResultsAsync(int? siegeScenarioId, int? userId, int page, int pageSize);

        /// <summary>Upserts the match's checkpoint (Created = true for the first one); an older capturedAt than the stored one is ignored.</summary>
        Task<(SurvivalMatchCheckpointDto Checkpoint, bool Created)> PutCheckpointAsync(int siegeMatchId, SurvivalMatchCheckpointDto dto);

        Task<SurvivalMatchCheckpointDto?> GetCheckpointAsync(int siegeMatchId);

        /// <summary>True when the scenario has enabled survival settings: its matches settle through the survival result, never through the PvP calculator.</summary>
        Task<bool> IsSurvivalScenarioAsync(int siegeScenarioId);

        /// <summary>
        /// Called by the Siege abort inside its lock, before the participants are closed: forfeits every Mystery Box
        /// roll of the match still Rolled (MatchEnded) and writes a result row with settlesRewards = false and the
        /// abort reason when the plugin never posted one. Nothing happens for a PvP scenario.
        /// </summary>
        Task OnMatchAbortedAsync(SiegeMatch match, SiegeMatchEndReason endReason, DateTime endedAt);
    }
}
