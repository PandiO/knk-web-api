using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>The lootbox singleton settings (docs/specs/lootboxes/DESIGN.md §3.2), like ISalaryConfigurationService.</summary>
    public interface ILootboxConfigurationService
    {
        Task<LootboxConfigurationDto> GetAsync();
        Task<LootboxConfigurationDto> UpdateAsync(UpdateLootboxConfigurationDto dto);
    }
}
