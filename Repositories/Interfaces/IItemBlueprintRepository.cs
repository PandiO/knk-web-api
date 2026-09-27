using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;

namespace knkwebapi_v2.Repositories.Interfaces
{
    public interface IItemBlueprintRepository
    {
        Task<IEnumerable<ItemBlueprint>> GetAllAsync();
        Task<ItemBlueprint?> GetByIdAsync(int id);
        Task AddAsync(ItemBlueprint entity);
        Task UpdateAsync(ItemBlueprint entity);
        Task DeleteAsync(int id);
        Task<PagedResult<ItemBlueprint>> SearchAsync(PagedQuery query);

        /// <summary>
        /// Why the blueprint can't be deleted, or null if nothing blocks it: minted item instances and lootbox
        /// pool/special entries reference it with no cascade (vision §9.2; docs/specs/lootboxes/DESIGN.md §3.2).
        /// </summary>
        Task<string?> FindDeleteBlockerAsync(int id);
    }
}
