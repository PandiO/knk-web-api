using System;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    public interface IAuditLogRepository
    {
        Task AddAsync(AuditLogEntry entry);

        Task<PagedResult<AuditLogEntry>> SearchAsync(int? targetUserId, int? actorUserId, int pageNumber, int pageSize);

        /// <summary>
        /// Moderation search (IMPLEMENTATION_PLAN.md Phase 3) — e.g. "recently demoted players"
        /// via action=TitleChanged, direction=demotion. direction matches against the freeform
        /// Details JSON (no dedicated column) via a string Contains, since only TitleChanged
        /// entries populate it and this is an admin-view-sized query, not a hot path.
        /// </summary>
        Task<PagedResult<AuditLogEntry>> SearchAsync(int? targetUserId, int? actorUserId, AuditAction? action, string? direction, int pageNumber, int pageSize);

        /// <summary>
        /// Deletes all AuditLogEntry rows with Timestamp older than beforeDate. No FK
        /// relationships exist on this table (deliberately, per AuditLogEntry's own doc comment),
        /// so this is a straight bulk delete. Returns the count of deleted rows.
        /// </summary>
        Task<int> DeleteOlderThanAsync(DateTime beforeDate);
    }
}
