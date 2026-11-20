using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Services.Interfaces
{
    // Siege shop catalogue (siege-shop DESIGN.md § 5.3, § 6; PandiO/knk-workspace#867).
    public interface ISiegeShopService
    {
        Task<List<SiegeShopCategoryReadDto>> GetCategoriesAsync();
        Task<SiegeShopCategoryReadDto?> GetCategoryByIdAsync(int id);
        Task<SiegeShopCategoryReadDto> CreateCategoryAsync(SiegeShopCategoryUpsertDto dto);
        Task UpdateCategoryAsync(int id, SiegeShopCategoryUpsertDto dto);
        Task DeleteCategoryAsync(int id);
        Task<PagedResultDto<SiegeShopCategoryReadDto>> SearchCategoriesAsync(PagedQueryDto query);

        Task<List<SiegeShopEntryReadDto>> GetEntriesAsync();
        Task<SiegeShopEntryReadDto?> GetEntryByIdAsync(int id);
        Task<SiegeShopEntryReadDto> CreateEntryAsync(SiegeShopEntryUpsertDto dto);
        Task UpdateEntryAsync(int id, SiegeShopEntryUpsertDto dto);
        Task DeleteEntryAsync(int id);
        Task<PagedResultDto<SiegeShopEntryListDto>> SearchEntriesAsync(PagedQueryDto query);

        Task<List<SiegeScenarioShopOverrideReadDto>> GetOverridesAsync(int siegeScenarioId);
        Task<SiegeScenarioShopOverrideReadDto?> GetOverrideByIdAsync(int id);
        Task<SiegeScenarioShopOverrideReadDto> CreateOverrideAsync(int siegeScenarioId, SiegeScenarioShopOverrideUpsertDto dto);
        Task UpdateOverrideAsync(int id, SiegeScenarioShopOverrideUpsertDto dto);
        Task DeleteOverrideAsync(int id);

        /// <summary>The effective catalogue of a match (§ 5.3); scenarioId null = the mode's defaults only.</summary>
        Task<SiegeShopCatalogueDto> GetCatalogueAsync(SiegeGameMode mode, int? siegeScenarioId);
    }
}
