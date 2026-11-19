using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    // Siege weapons phase 1 (SIEGE_WEAPONS.md § 9, PandiO/knk-workspace#782).
    public interface ISiegeWeaponService
    {
        Task<List<SiegeWeaponTemplateReadDto>> GetTemplatesAsync();
        Task<SiegeWeaponTemplateReadDto?> GetTemplateByIdAsync(int id);
        Task<SiegeWeaponTemplateReadDto> CreateTemplateAsync(SiegeWeaponTemplateUpsertDto dto);
        Task UpdateTemplateAsync(int id, SiegeWeaponTemplateUpsertDto dto);
        Task DeleteTemplateAsync(int id);
        Task<PagedResultDto<SiegeWeaponTemplateListDto>> SearchTemplatesAsync(PagedQueryDto query);

        Task<List<SiegeAmmoTypeReadDto>> GetAmmoTypesAsync();
        Task<SiegeAmmoTypeReadDto?> GetAmmoTypeByIdAsync(int id);
        Task<SiegeAmmoTypeReadDto> CreateAmmoTypeAsync(SiegeAmmoTypeUpsertDto dto);
        Task UpdateAmmoTypeAsync(int id, SiegeAmmoTypeUpsertDto dto);
        Task DeleteAmmoTypeAsync(int id);
        Task<PagedResultDto<SiegeAmmoTypeListDto>> SearchAmmoTypesAsync(PagedQueryDto query);

        Task<List<SiegeWeaponSpotReadDto>> GetSpotsAsync(int siegeScenarioId);
        Task<SiegeWeaponSpotReadDto?> GetSpotByIdAsync(int id);
        Task<SiegeWeaponSpotReadDto> CreateSpotAsync(int siegeScenarioId, SiegeWeaponSpotUpsertDto dto);
        Task UpdateSpotAsync(int id, SiegeWeaponSpotUpsertDto dto);
        Task DeleteSpotAsync(int id);

        Task<List<UserSiegeWeaponUnlockReadDto>> GetUnlocksForUserAsync(int userId);
        Task<UserSiegeWeaponUnlockReadDto?> GetUnlockByIdAsync(int id);
        Task<UserSiegeWeaponUnlockReadDto> CreateUnlockAsync(UserSiegeWeaponUnlockUpsertDto dto);
        Task UpdateUnlockAsync(int id, UserSiegeWeaponUnlockUpsertDto dto);
        Task DeleteUnlockAsync(int id);
        Task<PagedResultDto<UserSiegeWeaponUnlockReadDto>> SearchUnlocksAsync(PagedQueryDto query);
    }
}
