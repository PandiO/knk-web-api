using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>Arena duels (KNG-210, docs/specs/arena-duels/DESIGN.md §4.1–4.2; areas rev. 6).</summary>
    public interface IArenaService
    {
        Task<List<ArenaDto>> GetAllAsync();
        Task<ArenaDto?> GetByIdAsync(int id);
        Task<ArenaDto?> GetByKeyAsync(string key);
        Task<ArenaDto> CreateAsync(ArenaDto dto);
        Task UpdateAsync(int id, ArenaDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<ArenaListDto>> SearchAsync(PagedQueryDto query);

        Task<List<ArenaAreaDto>> GetAreasAsync(int? arenaId);
        Task<ArenaAreaDto?> GetAreaAsync(int id);
        Task<ArenaAreaDto> CreateAreaAsync(ArenaAreaDto dto);
        Task<ArenaAreaDto> UpdateAreaAsync(int id, ArenaAreaDto dto);
        Task DeleteAreaAsync(int id);
        Task<PagedResultDto<ArenaAreaDto>> SearchAreasAsync(PagedQueryDto query);

        Task<List<ArenaAreaRegionDto>> GetAreaRegionsAsync(int? areaId);
        Task<ArenaAreaRegionDto?> GetAreaRegionAsync(int id);
        /// <summary>The area region using <paramref name="regionId"/> in <paramref name="world"/> (null: any); the game
        /// server's orphan check.</summary>
        Task<ArenaAreaRegionDto?> FindAreaRegionAsync(string regionId, string? world);
        Task<ArenaAreaRegionDto> CreateAreaRegionAsync(ArenaAreaRegionDto dto);
        Task<ArenaAreaRegionDto> UpdateAreaRegionAsync(int id, ArenaAreaRegionDto dto);
        Task DeleteAreaRegionAsync(int id);
        Task<PagedResultDto<ArenaAreaRegionDto>> SearchAreaRegionsAsync(PagedQueryDto query);
    }
}
