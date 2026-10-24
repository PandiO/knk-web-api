using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    public interface IMinecraftMaterialRefRepository
    {
        Task<IEnumerable<MinecraftMaterialRef>> GetAllAsync();
        Task<MinecraftMaterialRef?> GetByIdAsync(int id);
        Task<MinecraftMaterialRef?> GetByNamespaceKeyAsync(string namespaceKey);
        Task AddAsync(MinecraftMaterialRef entity);
        /// <summary>Stops tracking an entity whose insert failed, so the context can be used again.</summary>
        void Detach(MinecraftMaterialRef entity);
        Task UpdateAsync(MinecraftMaterialRef entity);
        Task DeleteAsync(int id);
        Task<PagedResult<MinecraftMaterialRef>> SearchAsync(PagedQuery query);
    }
}
