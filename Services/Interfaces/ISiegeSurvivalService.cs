using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>List/get/create/update/delete of one survival-owned child collection (parentId = the owning row's id).</summary>
    public interface ISurvivalChildOperations<TRead, TUpsert>
    {
        Task<List<TRead>> ListAsync(int parentId);
        Task<TRead?> GetAsync(int id);
        Task<TRead> CreateAsync(TUpsert dto);
        Task UpdateAsync(int id, TUpsert dto);
        Task DeleteAsync(int id);
    }

    /// <summary>
    /// Siege Survival phase 1 (knk-workspace docs/specs/siege-survival/DESIGN.md §10; PandiO/knk-workspace#41, link
    /// S4): survival settings per scenario and per lobby, the survival child collections, the frozen match
    /// configuration, readiness and the editor's per-round preview. Error convention as the Siege services:
    /// ArgumentException -> 400, KeyNotFoundException -> 404, InvalidOperationException -> 409.
    /// </summary>
    public interface ISiegeSurvivalService
    {
        Task<List<SurvivalScenarioSummaryDto>> GetScenarioSummariesAsync();
        Task<SiegeSurvivalSettingsReadDto?> GetSettingsAsync(int siegeScenarioId);
        Task<SiegeSurvivalSettingsReadDto> UpsertSettingsAsync(int siegeScenarioId, SiegeSurvivalSettingsUpsertDto dto);
        Task DeleteSettingsAsync(int siegeScenarioId);

        Task<SurvivalLobbySettingsReadDto?> GetLobbySettingsAsync(int siegeLobbyId);
        Task<SurvivalLobbySettingsReadDto> UpsertLobbySettingsAsync(int siegeLobbyId, SurvivalLobbySettingsUpsertDto dto);
        Task DeleteLobbySettingsAsync(int siegeLobbyId);

        /// <summary>The frozen configuration of a survival scenario; 404 (KeyNotFound) for a scenario without survival settings.</summary>
        Task<SurvivalConfigurationDto> GetConfigurationAsync(int siegeScenarioId, int? siegeLobbyId, int? difficultyProfileId);
        Task<SurvivalReadinessDto> GetReadinessAsync(int siegeScenarioId, int? siegeLobbyId, int? difficultyProfileId);
        Task<SurvivalPreviewDto> PreviewAsync(int siegeScenarioId, int? difficultyProfileId, int fromRound, int toRound, int players);

        ISurvivalChildOperations<SurvivalDifficultyProfileDto, SurvivalDifficultyProfileUpsertDto> DifficultyProfiles { get; }
        ISurvivalChildOperations<SurvivalMonsterTypeDto, SurvivalMonsterTypeUpsertDto> MonsterTypes { get; }
        ISurvivalChildOperations<SurvivalSpecialWaveProfileDto, SurvivalSpecialWaveProfileUpsertDto> SpecialWaveProfiles { get; }
        ISurvivalChildOperations<SurvivalSpecialWaveMonsterTypeDto, SurvivalSpecialWaveMonsterTypeUpsertDto> SpecialWaveMonsterTypes { get; }
        ISurvivalChildOperations<SurvivalSpecialWaveEntryDto, SurvivalSpecialWaveEntryUpsertDto> SpecialWaveEntries { get; }
        ISurvivalChildOperations<SurvivalSpawnZoneDto, SurvivalSpawnZoneUpsertDto> SpawnZones { get; }
        ISurvivalChildOperations<SurvivalSpawnZonePointDto, SurvivalSpawnZonePointUpsertDto> SpawnZonePoints { get; }
        ISurvivalChildOperations<SurvivalTargetOverrideDto, SurvivalTargetOverrideUpsertDto> TargetOverrides { get; }
        ISurvivalChildOperations<SurvivalPurchasePointDto, SurvivalPurchasePointUpsertDto> PurchasePoints { get; }
        ISurvivalChildOperations<SurvivalPurchaseOfferDto, SurvivalPurchaseOfferUpsertDto> PurchaseOffers { get; }
        ISurvivalChildOperations<SurvivalMysteryBoxPoolDto, SurvivalMysteryBoxPoolUpsertDto> MysteryBoxPools { get; }
        ISurvivalChildOperations<SurvivalMysteryBoxEntryDto, SurvivalMysteryBoxEntryUpsertDto> MysteryBoxEntries { get; }
        ISurvivalChildOperations<SurvivalMysteryBoxPointDto, SurvivalMysteryBoxPointUpsertDto> MysteryBoxPoints { get; }
    }
}
