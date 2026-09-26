using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// Lootbox special (jackpot) entries (docs/specs/lootboxes/DESIGN.md §3.1 step 1, §3.5). Saving an entry also tags
    /// its blueprint <c>Lootbox Special</c>, which keeps it out of the normal pools.
    /// </summary>
    public interface ILootboxSpecialEntryService
    {
        Task<IEnumerable<LootboxSpecialEntryDto>> GetAllAsync();
        Task<LootboxSpecialEntryDto?> GetByIdAsync(int id);
        Task<LootboxSpecialEntryDto> CreateAsync(LootboxSpecialEntryDto dto);
        Task UpdateAsync(int id, LootboxSpecialEntryDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<LootboxSpecialEntryDto>> SearchAsync(PagedQueryDto query);
    }
}
