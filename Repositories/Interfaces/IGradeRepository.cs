using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;

namespace knkwebapi_v2.Repositories.Interfaces
{
    public interface IGradeRepository
    {
        Task<IEnumerable<Grade>> GetAllAsync();
        Task<Grade?> GetByIdAsync(int id);
        Task AddAsync(Grade entity);
        Task UpdateAsync(Grade entity);
        Task DeleteAsync(int id);
        Task<PagedResult<Grade>> SearchAsync(PagedQuery query);
    }
}
