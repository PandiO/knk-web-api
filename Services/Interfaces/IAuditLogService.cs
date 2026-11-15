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

        /// <summary>
        /// Writes one entry about a subject that is not one player (PandiO/knk-workspace#50): subjectType is an
        /// <see cref="AuditSubjectTypes"/> name, subjectId its id; the entry has no target user.
        /// </summary>
        Task RecordForSubjectAsync(int? actorUserId, string subjectType, int subjectId, AuditAction action, string? details = null);

        Task<PagedResultDto<AuditLogEntryDto>> SearchAsync(int? targetUserId, int? actorUserId, int pageNumber, int pageSize);

        /// <summary>Moderation search (IMPLEMENTATION_PLAN.md Phase 3) — e.g. recently demoted players.</summary>
        Task<PagedResultDto<AuditLogEntryDto>> SearchAsync(int? targetUserId, int? actorUserId, AuditAction? action, string? direction, int pageNumber, int pageSize);

        /// <summary>The same search, also filtered on the generic subject (a permission group's History list, #50).</summary>
        Task<PagedResultDto<AuditLogEntryDto>> SearchAsync(int? targetUserId, int? actorUserId, AuditAction? action, string? direction,
            string? subjectType, int? subjectId, int pageNumber, int pageSize);
    }
}
