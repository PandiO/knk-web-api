using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories
{
    public interface IFormSubmissionProgressRepository
    {
        Task<IEnumerable<FormSubmissionProgress>> GetByEntityTypeNameAsync(
            string entityTypeName, int? userId, string? propertyName = null, string? propertyValue = null);
        Task<IEnumerable<FormSubmissionProgress>> GetByUserIdAsync(int userId);
        Task<FormSubmissionProgress?> GetByIdAsync(int id);
        Task AddAsync(FormSubmissionProgress progress);
        Task UpdateAsync(FormSubmissionProgress progress);
        Task DeleteAsync(int id);
        Task<IEnumerable<FormSubmissionProgress>> GetCompletedOlderThanAsync(System.DateTime beforeDate);
        Task<int> DeleteCompletedOlderThanAsync(System.DateTime beforeDate);
    }
}
