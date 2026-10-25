using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories
{
    /// <summary>Arena duels (KNG-210): arenas, their areas (rev. 6) and their regions, and eligible kits.</summary>
    public interface IArenaRepository
    {
        Task<List<Arena>> GetAllAsync();
        Task<Arena?> GetByIdAsync(int id);
        Task<Arena?> GetByKeyAsync(string key);
        Task<bool> KeyTakenAsync(string key, int? excludeId);
        Task<PagedResult<Arena>> SearchAsync(PagedQuery query);

        Task<List<ArenaArea>> GetAreasAsync(int? arenaId);
        Task<ArenaArea?> GetAreaAsync(int id);
        Task<bool> AreaKeyTakenAsync(int arenaId, string key, int? excludeId);
        Task<bool> OtherLobbyExistsAsync(int arenaId, int? excludeId);
        Task<PagedResult<ArenaArea>> SearchAreasAsync(PagedQuery query);

        Task<List<ArenaAreaRegion>> GetAreaRegionsAsync(int? areaId);
        Task<ArenaAreaRegion?> GetAreaRegionAsync(int id);
        /// <summary>The area region with this WorldGuard id in <paramref name="world"/> (null: any world).</summary>
        Task<ArenaAreaRegion?> FindAreaRegionAsync(string regionId, string? world);
        Task<PagedResult<ArenaAreaRegion>> SearchAreaRegionsAsync(PagedQuery query);

        Task<List<int>> ExistingKitIdsAsync(IEnumerable<int> kitIds);
        Task<Location?> GetLocationAsync(int id);
        Task<bool> TitleBracketExistsAsync(int id);
        Task<bool> StreetExistsAsync(int id);
        Task<bool> DistrictExistsAsync(int id);

        Task AddAsync(object entity);
        Task SaveChangesAsync();
        /// <summary>Deletes the arena with its areas, their regions and its kit rows. The areas' WorldGuard regions become
        /// orphans the game server deletes after the retention period.</summary>
        Task DeleteArenaAsync(Arena arena);
        Task RemoveAsync(object entity);
    }
}
