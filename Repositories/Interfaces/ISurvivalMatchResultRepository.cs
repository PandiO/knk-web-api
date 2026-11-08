using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    /// <summary>The rows of a survival match's durable result and checkpoint (PandiO/knk-workspace#41, link S8a).</summary>
    public interface ISurvivalMatchResultRepository
    {
        /// <summary>The scenario's survival settings, or null when the scenario is an ordinary (PvP) Siege scenario.</summary>
        Task<SiegeSurvivalSettings?> GetSurvivalSettingsAsync(int siegeScenarioId);

        /// <summary>The match's result with its player rows, or null.</summary>
        Task<SurvivalMatchResult?> GetResultAsync(int siegeMatchId);

        /// <summary>Results newest first, optionally of one scenario and/or with one user among the players.</summary>
        Task<(List<SurvivalMatchResult> Items, int TotalCount)> QueryResultsAsync(int? siegeScenarioId, int? userId, int page, int pageSize);

        Task AddResultAsync(SurvivalMatchResult result);

        Task<SurvivalMatchCheckpoint?> GetCheckpointAsync(int siegeMatchId);

        Task AddCheckpointAsync(SurvivalMatchCheckpoint checkpoint);

        Task SaveChangesAsync();
    }
}
