using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    // Siege weapons phase 1 (SIEGE_WEAPONS.md § 9, PandiO/knk-workspace#782): loads and saves only; the rules are in
    // SiegeWeaponService.
    public interface ISiegeWeaponRepository
    {
        // Templates
        Task<List<SiegeWeaponTemplate>> GetTemplatesAsync();
        Task<SiegeWeaponTemplate?> GetTemplateByIdAsync(int id);
        Task<SiegeWeaponTemplate?> GetTemplateByNameAsync(string name);
        Task AddTemplateAsync(SiegeWeaponTemplate template);
        Task UpdateTemplateAsync(SiegeWeaponTemplate template);
        Task DeleteTemplateAsync(SiegeWeaponTemplate template);
        Task<PagedResult<SiegeWeaponTemplate>> SearchTemplatesAsync(PagedQuery query);
        Task<bool> TemplateHasUnlocksAsync(int templateId);

        // Ammo types
        Task<List<SiegeAmmoType>> GetAmmoTypesAsync();
        Task<SiegeAmmoType?> GetAmmoTypeByIdAsync(int id);
        Task<SiegeAmmoType?> GetAmmoTypeByNameAsync(string name);
        Task<List<int>> GetExistingAmmoTypeIdsAsync(IEnumerable<int> ids);
        Task AddAmmoTypeAsync(SiegeAmmoType ammoType);
        Task UpdateAmmoTypeAsync(SiegeAmmoType ammoType);
        Task DeleteAmmoTypeAsync(SiegeAmmoType ammoType);
        Task<PagedResult<SiegeAmmoType>> SearchAmmoTypesAsync(PagedQuery query);
        Task<List<string>> GetTemplateNamesAllowingAmmoTypeAsync(int ammoTypeId);

        // Weapon spots (owned by a scenario)
        Task<SiegeScenario?> GetScenarioWithTeamsAndSpotsAsync(int siegeScenarioId);
        Task<List<SiegeWeaponSpot>> GetSpotsAsync(int siegeScenarioId);
        Task<SiegeWeaponSpot?> GetSpotByIdAsync(int id);
        Task AddSpotAsync(SiegeWeaponSpot spot);
        Task UpdateSpotAsync(SiegeWeaponSpot spot);
        Task DeleteSpotAsync(SiegeWeaponSpot spot);

        // Unlocks
        Task<List<UserSiegeWeaponUnlock>> GetUnlocksForUserAsync(int userId);
        Task<UserSiegeWeaponUnlock?> GetUnlockByIdAsync(int id);
        Task<UserSiegeWeaponUnlock?> GetUnlockAsync(int userId, int templateId);
        Task<bool> UserExistsAsync(int userId);
        Task AddUnlockAsync(UserSiegeWeaponUnlock unlock);
        Task UpdateUnlockAsync(UserSiegeWeaponUnlock unlock);
        Task DeleteUnlockAsync(UserSiegeWeaponUnlock unlock);
        Task<PagedResult<UserSiegeWeaponUnlock>> SearchUnlocksAsync(PagedQuery query);
    }
}
