using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    public interface ILootboxSpawnAreaRepository
    {
        Task<IEnumerable<LootboxSpawnArea>> GetAllAsync();
        Task<LootboxSpawnArea?> GetByIdAsync(int id);
        Task AddAsync(LootboxSpawnArea entity);
        Task UpdateAsync(LootboxSpawnArea entity);

        /// <summary>Marks the area's active spawns Removed and deletes the row; the spawns keep their history
        /// (SpawnAreaId set to null). Returns the ids of the spawns it removed.</summary>
        Task<List<int>> DeleteAsync(int id);

        Task<PagedResult<LootboxSpawnArea>> SearchAsync(PagedQuery query);

        /// <summary>True when another area (not <paramref name="excludeAreaId"/>) has the name, ignoring case.</summary>
        Task<bool> NameTakenAsync(string name, int? excludeAreaId = null);

        /// <summary>True when another area (not <paramref name="excludeAreaId"/>) uses the WG region in that world,
        /// ignoring case.</summary>
        Task<bool> RegionTakenAsync(string world, string wgRegionId, int? excludeAreaId = null);

        Task<HashSet<int>> GetExistingTypeIdsAsync(IEnumerable<int> typeIds);
    }
}
