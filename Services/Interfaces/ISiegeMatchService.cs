using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Services.Interfaces
{
    // Siege Phase 6 (docs/specs/siege-minigame/DESIGN.md §3.10, §7.6, §11.2). Errors:
    // KeyNotFoundException -> 404, ArgumentException -> 400, InvalidOperationException -> 409.
    public interface ISiegeMatchService
    {
        Task<SiegeMatchDto?> GetByIdAsync(int id);
        Task<List<SiegeMatchSummaryDto>> QueryAsync(int? userId, int? siegeLobbyId, SiegeMatchStatus? status, int limit);

        /// <summary>A Created match row (at the draw).</summary>
        Task<SiegeMatchDto> CreateAsync(SiegeMatchCreateDto dto);

        /// <summary>Created -> InProgress with its participants; repeating it on an InProgress match changes nothing.</summary>
        Task<SiegeMatchDto> StartAsync(int id, SiegeMatchStartDto dto);

        /// <summary>Marks a participant as left (the first LeftAt wins); they get no rewards.</summary>
        Task ParticipantLeftAsync(int id, int userId, SiegeMatchParticipantLeftDto? dto);

        /// <summary>
        /// Records the result, computes and grants the rewards in one transaction and marks the match
        /// Completed. A repeat call returns the stored result and grants nothing.
        /// </summary>
        Task<SiegeMatchResultDto> CompleteAsync(int id, SiegeMatchCompleteDto dto);

        /// <summary>Created/InProgress -> Aborted, no rewards. Repeating it on an Aborted match changes nothing.</summary>
        Task<SiegeMatchDto> AbortAsync(int id, SiegeMatchAbortDto dto);

        /// <summary>Startup recovery: aborts every Created/InProgress match (plugin restart, DESIGN §5.1).</summary>
        Task<SiegeMatchAbortUnfinishedResultDto> AbortUnfinishedAsync(SiegeMatchAbortUnfinishedDto dto);
    }
}
