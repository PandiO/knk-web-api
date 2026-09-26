using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    public interface ILootboxSpecialEntryRepository
    {
        Task<IEnumerable<LootboxSpecialEntry>> GetAllAsync();
        Task<LootboxSpecialEntry?> GetByIdAsync(int id);
        Task AddAsync(LootboxSpecialEntry entity);
        Task UpdateAsync(LootboxSpecialEntry entity);
        Task DeleteAsync(int id);
        Task<PagedResult<LootboxSpecialEntry>> SearchAsync(PagedQuery query);

        Task<bool> TypeExistsAsync(int typeId);
        Task<bool> BlueprintExistsAsync(int blueprintId);

        /// <summary>
        /// Gives the blueprint the <c>Lootbox Special</c> tag (creating the tag if needed) so it drops out of the normal
        /// pools (DESIGN.md §3.2). Saved with the next SaveChanges.
        /// </summary>
        Task EnsureSpecialTagAsync(int blueprintId);
    }
}
