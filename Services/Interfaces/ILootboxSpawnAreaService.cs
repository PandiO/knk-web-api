using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// Lootbox spawn areas (docs/specs/lootboxes/DESIGN.md §3.2, D17), web-app CRUD. A duplicate name throws
    /// <see cref="knkwebapi_v2.Services.Lootbox.LootboxConflictException"/> <c>NameTaken</c> (409). Deleting an area
    /// marks its active boxes Removed; their history stays. The in-game create/delete endpoints come in Phase 2.
    /// </summary>
    public interface ILootboxSpawnAreaService
    {
        Task<IEnumerable<LootboxSpawnAreaDto>> GetAllAsync();
        Task<LootboxSpawnAreaDto?> GetByIdAsync(int id);
        Task<LootboxSpawnAreaDto> CreateAsync(LootboxSpawnAreaDto dto);
        Task UpdateAsync(int id, LootboxSpawnAreaDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<LootboxSpawnAreaDto>> SearchAsync(PagedQueryDto query);
    }
}
