using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>Arena duels (KNG-210, docs/specs/arena-duels/DESIGN.md §4.1–4.2).</summary>
    public interface IArenaService
    {
        Task<List<ArenaDto>> GetAllAsync();
        Task<ArenaDto?> GetByIdAsync(int id);
        Task<ArenaDto?> GetByKeyAsync(string key);
        Task<ArenaDto> CreateAsync(ArenaDto dto);
        Task UpdateAsync(int id, ArenaDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<ArenaListDto>> SearchAsync(PagedQueryDto query);

        Task<List<ArenaBattlegroundDto>> GetBattlegroundsAsync(int? arenaId);
        Task<ArenaBattlegroundDto?> GetBattlegroundAsync(int id);
        Task<ArenaBattlegroundDto> CreateBattlegroundAsync(ArenaBattlegroundDto dto);
        Task UpdateBattlegroundAsync(int id, ArenaBattlegroundDto dto);
        Task DeleteBattlegroundAsync(int id);
        Task<PagedResultDto<ArenaBattlegroundDto>> SearchBattlegroundsAsync(PagedQueryDto query);

        Task<List<ArenaSpectatorAreaDto>> GetSpectatorAreasAsync(int? arenaId);
        Task<ArenaSpectatorAreaDto?> GetSpectatorAreaAsync(int id);
        Task<ArenaSpectatorAreaDto> CreateSpectatorAreaAsync(ArenaSpectatorAreaDto dto);
        Task UpdateSpectatorAreaAsync(int id, ArenaSpectatorAreaDto dto);
        Task DeleteSpectatorAreaAsync(int id);
        Task<PagedResultDto<ArenaSpectatorAreaDto>> SearchSpectatorAreasAsync(PagedQueryDto query);

        /// <summary>Creates (or completes) the v1 arena and its flagged battlegrounds, disabled until staff set the
        /// lobby and spawn locations.</summary>
        Task<ArenaLegacySeedResultDto> SeedLegacyArenaAsync(ArenaLegacySeedDto dto);
    }
}
