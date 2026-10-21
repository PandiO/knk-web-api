using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    public interface IMinecraftBlockRefRepository
    {
        Task<IEnumerable<MinecraftBlockRef>> GetAllAsync();
        Task<MinecraftBlockRef?> GetByIdAsync(int id);
        Task<MinecraftBlockRef?> GetByStateKeyAsync(string stateKey);
        Task AddAsync(MinecraftBlockRef entity);
        Task UpdateAsync(MinecraftBlockRef entity);
        Task DeleteAsync(int id);
        Task<PagedResult<MinecraftBlockRef>> SearchAsync(PagedQuery query);
    }
}
