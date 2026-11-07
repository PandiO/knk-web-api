using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    /// <summary>The rows a match-scoped Mystery Box roll reads and writes (PandiO/knk-workspace#41, link S7a).</summary>
    public interface ISurvivalMysteryBoxRollRepository
    {
        /// <summary>The match with its participants, or null.</summary>
        Task<SiegeMatch?> GetMatchAsync(int siegeMatchId);
        /// <summary>The box point with its pool, entries, enchantment chances and catalog names, or null.</summary>
        Task<SurvivalMysteryBoxPoint?> GetPointWithPoolAsync(int pointId);
        Task<HashSet<int>> ExistingItemBlueprintIdsAsync(IEnumerable<int> ids);
        Task<HashSet<int>> ExistingEnchantmentDefinitionIdsAsync(IEnumerable<int> ids);

        /// <summary>The roll with its delivery by the plugin's roll id (unique), or null.</summary>
        Task<SurvivalMysteryBoxRoll?> GetRollAsync(string rollId);
        Task<List<SurvivalMysteryBoxRoll>> ListRollsAsync(int siegeMatchId);
        Task AddRollAsync(SurvivalMysteryBoxRoll roll);
        Task AddDeliveryAsync(SurvivalMysteryBoxDelivery delivery);
        Task SaveChangesAsync();
    }
}
