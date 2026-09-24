using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    public interface IAuditLogRepository
    {
        Task AddAsync(AuditLogEntry entry);

        Task<PagedResult<AuditLogEntry>> SearchAsync(int? targetUserId, int? actorUserId, int pageNumber, int pageSize);
    }
}
