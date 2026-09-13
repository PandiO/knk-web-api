using System.Collections.Generic;
using System.Threading.Tasks;
using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services
{
    public interface IFormSubmissionProgressService
    {
        Task<IEnumerable<FormSubmissionProgressDto>> GetByEntityTypeNameAsync(string entityTypeName, int? userId);
        Task<IEnumerable<FormSubmissionProgressSummaryDto>> GetSummaryByEntityTypeNameAsync(
            string entityTypeName, int? userId, string? propertyName = null, string? propertyValue = null);
        Task<IEnumerable<FormSubmissionProgressDto>> GetByUserIdAsync(int userId);
        Task<FormSubmissionProgressDto?> GetByIdAsync(int id);
        Task<FormSubmissionProgressDto> SaveProgressAsync(FormSubmissionProgressDto progress);
        Task<FormSubmissionProgressDto> UpdateProgressAsync(int id, FormSubmissionProgressDto progress);
        Task DeleteAsync(int id);
    }
}
