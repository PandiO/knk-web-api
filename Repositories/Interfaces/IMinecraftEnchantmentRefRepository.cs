using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces;

public interface IMinecraftEnchantmentRefRepository
{
    Task<IEnumerable<MinecraftEnchantmentRef>> GetAllAsync();
    Task<MinecraftEnchantmentRef?> GetByIdAsync(int id);
    Task<MinecraftEnchantmentRef?> GetByNamespaceKeyAsync(string namespaceKey);
    Task AddAsync(MinecraftEnchantmentRef entity);
    Task UpdateAsync(MinecraftEnchantmentRef entity);
    Task DeleteAsync(MinecraftEnchantmentRef entity);
    Task<PagedResult<MinecraftEnchantmentRef>> SearchAsync(PagedQuery query);
}
