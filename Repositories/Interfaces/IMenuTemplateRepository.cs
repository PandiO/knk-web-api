using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    public interface IMenuTemplateRepository
    {
        Task<IEnumerable<MenuTemplate>> GetAllAsync();
        Task<MenuTemplate?> GetByIdAsync(int id);
        Task<MenuTemplate?> GetByKeyAsync(string key);
        Task AddAsync(MenuTemplate entity);
        Task UpdateAsync(MenuTemplate entity);
        Task DeleteAsync(int id);
    }
}
