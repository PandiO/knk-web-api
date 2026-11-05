using System.Linq.Expressions;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Repositories.Interfaces
{
    /// <summary>
    /// Siege Survival phase 1 (PandiO/knk-workspace#41, link S4): the survival rows of a scenario and of a lobby.
    /// The Siege scenario graph itself comes from ISiegeScenarioRepository.
    /// </summary>
    public interface ISiegeSurvivalRepository
    {
        // ---- Scenario settings ----
        Task<SiegeSurvivalSettings?> GetSettingsAsync(int siegeScenarioId);
        Task<List<SiegeSurvivalSettings>> GetAllSettingsAsync();
        Task<Dictionary<int, int>> CountDifficultyProfilesAsync();

        // ---- Lobby settings ----
        Task<SurvivalLobbySettings?> GetLobbySettingsAsync(int siegeLobbyId);
        Task<SiegeLobby?> GetLobbyAsync(int siegeLobbyId);

        /// <summary>Every survival row of the scenario, with the navigations the read DTOs and readiness need; null when the scenario doesn't exist.</summary>
        Task<SurvivalScenarioGraph?> GetGraphAsync(int siegeScenarioId, SiegeScenario scenario);

        // ---- Generic child rows (survival-owned entities only) ----
        Task<T?> FindAsync<T>(int id) where T : class;
        Task<List<T>> ListAsync<T>(Expression<Func<T, bool>> where) where T : class;
        Task AddAsync<T>(T entity) where T : class;
        Task SaveAsync<T>(T entity) where T : class;
        Task RemoveAsync<T>(T entity) where T : class;

        // ---- Loaded child rows (with their own children) ----
        Task<SurvivalSpecialWaveProfile?> GetSpecialWaveProfileAsync(int id);
        Task<SurvivalSpecialWaveEntry?> GetSpecialWaveEntryAsync(int id);
        Task<SurvivalSpawnZone?> GetSpawnZoneAsync(int id);
        Task<SurvivalSpawnZonePoint?> GetSpawnZonePointAsync(int id);
        Task<SurvivalTargetOverride?> GetTargetOverrideAsync(int id);
        Task<SurvivalPurchasePoint?> GetPurchasePointAsync(int id);
        Task<SurvivalPurchaseOffer?> GetPurchaseOfferAsync(int id);
        Task<SurvivalMysteryBoxPool?> GetMysteryBoxPoolAsync(int id);
        Task<SurvivalMysteryBoxEntry?> GetMysteryBoxEntryAsync(int id);
        Task<SurvivalMysteryBoxPoint?> GetMysteryBoxPointAsync(int id);

        // ---- Lookups ----
        Task<HashSet<int>> ExistingKitIdsAsync(IEnumerable<int> ids);
        Task<HashSet<int>> ExistingItemBlueprintIdsAsync(IEnumerable<int> ids);
        Task<HashSet<int>> ExistingEnchantmentDefinitionIdsAsync(IEnumerable<int> ids);
        Task<bool> LocationExistsAsync(int locationId);
        Task<bool> MysteryBoxPoolInUseAsync(int poolId);
    }
}
