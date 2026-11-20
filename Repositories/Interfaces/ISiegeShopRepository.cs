using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    // Siege shop catalogue (siege-shop DESIGN.md § 6, PandiO/knk-workspace#867): loads and saves only; the rules and
    // the catalogue resolution are in SiegeShopService.
    public interface ISiegeShopRepository
    {
        // Categories
        Task<List<SiegeShopCategory>> GetCategoriesAsync();
        Task<SiegeShopCategory?> GetCategoryByIdAsync(int id);
        Task<SiegeShopCategory?> GetCategoryByKeyAsync(string key);
        Task AddCategoryAsync(SiegeShopCategory category);
        Task UpdateCategoryAsync(SiegeShopCategory category);
        Task DeleteCategoryAsync(SiegeShopCategory category);
        Task<PagedResult<SiegeShopCategory>> SearchCategoriesAsync(PagedQuery query);
        Task<int> CountEntriesInCategoryAsync(int categoryId);

        // Entries
        Task<List<SiegeShopEntry>> GetEntriesAsync();
        Task<SiegeShopEntry?> GetEntryByIdAsync(int id);
        Task AddEntryAsync(SiegeShopEntry entry);
        Task UpdateEntryAsync(SiegeShopEntry entry);
        Task DeleteEntryAsync(SiegeShopEntry entry);
        Task<PagedResult<SiegeShopEntry>> SearchEntriesAsync(PagedQuery query);
        Task<bool> EntryHasOverridesAsync(int entryId);

        // Overrides (owned by a scenario)
        Task<List<SiegeScenarioShopOverride>> GetOverridesAsync(int siegeScenarioId);
        Task<SiegeScenarioShopOverride?> GetOverrideByIdAsync(int id);
        Task<SiegeScenarioShopOverride?> GetOverrideAsync(int siegeScenarioId, int siegeShopEntryId);
        Task AddOverrideAsync(SiegeScenarioShopOverride shopOverride);
        Task UpdateOverrideAsync(SiegeScenarioShopOverride shopOverride);
        Task DeleteOverrideAsync(SiegeScenarioShopOverride shopOverride);

        // References
        Task<bool> ScenarioExistsAsync(int siegeScenarioId);
        Task<bool> ItemBlueprintExistsAsync(int itemBlueprintId);
        Task<bool> WeaponTemplateExistsAsync(int templateId);

        // Effective catalogue: enabled entries of the mode (defaults, plus the scenario's own when given) with their
        // category, blueprint and template; the scenario's overrides.
        Task<List<SiegeShopEntry>> GetEnabledEntriesForCatalogueAsync(SiegeGameMode mode, int? siegeScenarioId);
    }
}
