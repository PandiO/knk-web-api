using knkwebapi_v2.Dtos;
using knkwebapi_v2.Services.Statistics;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// Alpha run 1 (PandiO/knk-workspace#174): the public siege results log - a player's finished matches and one
    /// match's page, with each participant's own numbers filtered by their statistics visibility settings (see
    /// <see cref="SiegeMatchPublicDto"/>).
    /// </summary>
    public interface ISiegeMatchHistoryService
    {
        /// <summary>A finished match (Completed or Aborted) as <paramref name="viewer"/> may see it; null when there is none.</summary>
        Task<SiegeMatchPublicDto?> GetMatchAsync(int matchId, StatisticsViewer viewer);

        /// <summary>
        /// The player's finished matches, newest first (at most <paramref name="limit"/>, 1-50, default 20); null when the
        /// player doesn't exist or is inactive.
        /// </summary>
        Task<List<SiegeMatchPublicSummaryDto>?> GetPlayerMatchesAsync(int userId, StatisticsViewer viewer, int limit);
    }
}
