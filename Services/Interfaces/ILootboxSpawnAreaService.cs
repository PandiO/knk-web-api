using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// Lootbox spawn areas (docs/specs/lootboxes/DESIGN.md §3.2, D17), web-app CRUD. A duplicate name throws
    /// <see cref="knkwebapi_v2.Services.Lootbox.LootboxConflictException"/> <c>NameTaken</c> (409). Deleting an area
    /// marks its active boxes Removed; their history stays.
    /// </summary>
    public interface ILootboxSpawnAreaService
    {
        Task<IEnumerable<LootboxSpawnAreaDto>> GetAllAsync();
        Task<LootboxSpawnAreaDto?> GetByIdAsync(int id);
        Task<LootboxSpawnAreaDto> CreateAsync(LootboxSpawnAreaDto dto);
        Task UpdateAsync(int id, LootboxSpawnAreaDto dto);
        Task DeleteAsync(int id);
        Task<PagedResultDto<LootboxSpawnAreaDto>> SearchAsync(PagedQueryDto query);

        /// <summary>
        /// <c>/knk lootbox area create</c> (DESIGN.md §3.4): an enabled area with the default limits, created by
        /// <paramref name="actorUserId"/>. 409 <c>NameTaken</c> (any case) or <c>RegionInUse</c> (another area already
        /// uses the WG region in that world); 400 for an invalid name. Audited when the staff member is known.
        /// </summary>
        Task<LootboxSpawnAreaDto> CreateInGameAsync(LootboxInGameAreaCreateDto dto, int? actorUserId);

        /// <summary>
        /// <c>/knk lootbox area delete</c>: the area's active boxes become Removed (their rows stay, with SpawnAreaId
        /// null), the area row is deleted, and the WG region id is returned so the plugin can remove a region it made.
        /// </summary>
        Task<LootboxInGameAreaDeleteResultDto> DeleteInGameAsync(int id, int? actorUserId);
    }
}
