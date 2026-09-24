using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Services.Interfaces
{
    /// <summary>
    /// Append-only write/read path for AuditLogEntry (docs/specs/user-management/DESIGN.md §4).
    /// Named RecordAsync rather than the plan's shorthand "Record" to match this codebase's own
    /// Async-suffix convention (PayOutAsync, AdjustBalancesAsync, ...).
    /// </summary>
    public interface IAuditLogService
    {
        /// <summary>
        /// Writes one entry. actorUserId is null for system-initiated mutations (e.g. an
        /// automatic salary payout on player join) — never throws on a missing actor, since a
        /// null actor is an expected, valid case, not an error.
        /// </summary>
        Task RecordAsync(int? actorUserId, int targetUserId, AuditAction action, string? details = null);

        Task<PagedResultDto<AuditLogEntryDto>> SearchAsync(int? targetUserId, int? actorUserId, int pageNumber, int pageSize);
    }
}
