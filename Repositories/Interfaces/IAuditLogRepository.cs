using System;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    public interface IAuditLogRepository
    {
        Task AddAsync(AuditLogEntry entry);

        Task<PagedResult<AuditLogEntry>> SearchAsync(int? targetUserId, int? actorUserId, int pageNumber, int pageSize);

        /// <summary>
        /// Deletes all AuditLogEntry rows with Timestamp older than beforeDate. No FK
        /// relationships exist on this table (deliberately, per AuditLogEntry's own doc comment),
        /// so this is a straight bulk delete. Returns the count of deleted rows.
        /// </summary>
        Task<int> DeleteOlderThanAsync(DateTime beforeDate);
    }
}
