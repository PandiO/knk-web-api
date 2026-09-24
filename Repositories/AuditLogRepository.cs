using System;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class AuditLogRepository : IAuditLogRepository
    {
        private readonly KnKDbContext _context;

        public AuditLogRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task AddAsync(AuditLogEntry entry)
        {
            await _context.AuditLogEntries.AddAsync(entry);
            await _context.SaveChangesAsync();
        }

        public Task<PagedResult<AuditLogEntry>> SearchAsync(int? targetUserId, int? actorUserId, int pageNumber, int pageSize)
            => SearchAsync(targetUserId, actorUserId, null, null, pageNumber, pageSize);

        public async Task<PagedResult<AuditLogEntry>> SearchAsync(int? targetUserId, int? actorUserId, AuditAction? action, string? direction, int pageNumber, int pageSize)
        {
            var queryable = _context.AuditLogEntries.AsQueryable();

            if (targetUserId.HasValue)
            {
                queryable = queryable.Where(e => e.TargetUserId == targetUserId.Value);
            }

            if (actorUserId.HasValue)
            {
                queryable = queryable.Where(e => e.ActorUserId == actorUserId.Value);
            }

            if (action.HasValue)
            {
                queryable = queryable.Where(e => e.Action == action.Value);
            }

            if (!string.IsNullOrWhiteSpace(direction))
            {
                // Details is freeform JSON (only TitleChanged populates "direction"); matching via
                // Contains on the serialized field rather than a dedicated column, per this table's
                // own "append-only, viewed not edited" scope (IMPLEMENTATION_PLAN.md Phase 2).
                var needle = $"\"direction\":\"{direction}\"";
                queryable = queryable.Where(e => e.Details != null && e.Details.Contains(needle));
            }

            queryable = queryable.OrderByDescending(e => e.Timestamp);

            var totalCount = await queryable.CountAsync();

            var items = await queryable
                .Skip((pageNumber - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return new PagedResult<AuditLogEntry>
            {
                Items = items,
                TotalCount = totalCount,
                PageNumber = pageNumber,
                PageSize = pageSize
            };
        }

        public async Task<int> DeleteOlderThanAsync(DateTime beforeDate)
        {
            return await _context.AuditLogEntries
                .Where(e => e.Timestamp < beforeDate)
                .ExecuteDeleteAsync();
        }
    }
}
