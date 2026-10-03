using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services.Leaderboards;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>Leaderboard reads (from snapshots only) and owner exclusions (IMPLEMENTATION_PLAN.md §3.2).</summary>
    public interface ILeaderboardQueryService
    {
        IReadOnlyList<LeaderboardBoardDto> GetBoards();

        /// <summary>Top <paramref name="top"/> of the board's current snapshot plus the viewer's own row.</summary>
        Task<LeaderboardViewDto> GetBoardAsync(LeaderboardBoardDefinition board, LeaderboardPeriod period, int top, int? viewerUserId,
            CancellationToken ct = default);

        Task<List<LeaderboardExclusionDto>> GetExclusionsAsync(CancellationToken ct = default);

        /// <summary>Excludes a player from every board from the next refresh; false when the user doesn't exist.</summary>
        Task<bool> ExcludeAsync(int userId, string? reason, int byUserId, CancellationToken ct = default);

        /// <summary>Lifts an exclusion; false when the user doesn't exist.</summary>
        Task<bool> IncludeAsync(int userId, CancellationToken ct = default);
    }
}
