using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;

namespace knkwebapi_v2.Repositories.Interfaces
{
    public interface ITagRepository
    {
        Task<IEnumerable<Tag>> GetAllAsync();
        Task<Tag?> GetByIdAsync(int id);
        Task AddAsync(Tag entity);
        Task UpdateAsync(Tag entity);
        Task DeleteAsync(int id);
        Task<PagedResult<Tag>> SearchAsync(PagedQuery query);
    }
}
