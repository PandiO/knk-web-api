using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;

namespace knkwebapi_v2.Services
{
    public class AuditLogService : IAuditLogService
    {
        private readonly IAuditLogRepository _repo;
        private readonly IUserRepository _userRepo;

        public AuditLogService(IAuditLogRepository repo, IUserRepository userRepo)
        {
            _repo = repo;
            _userRepo = userRepo;
        }

        public async Task RecordAsync(int? actorUserId, int targetUserId, AuditAction action, string? details = null)
        {
            if (targetUserId <= 0) throw new ArgumentException("Invalid target user id.", nameof(targetUserId));

            await _repo.AddAsync(new AuditLogEntry
            {
                Timestamp = DateTime.UtcNow,
                ActorUserId = actorUserId,
                TargetUserId = targetUserId,
                Action = action,
                Details = details
            });
        }

        public Task<PagedResultDto<AuditLogEntryDto>> SearchAsync(int? targetUserId, int? actorUserId, int pageNumber, int pageSize)
            => SearchAsync(targetUserId, actorUserId, null, null, pageNumber, pageSize);

        public async Task<PagedResultDto<AuditLogEntryDto>> SearchAsync(int? targetUserId, int? actorUserId, AuditAction? action, string? direction, int pageNumber, int pageSize)
        {
            var result = await _repo.SearchAsync(targetUserId, actorUserId, action, direction, pageNumber, pageSize);

            // Small per-page dataset (pageSize is admin-view sized) — a plain per-id lookup is
            // simpler than adding a batch-get-by-ids method to IUserRepository for this one caller.
            var usernameCache = new Dictionary<int, string?>();
            async Task<string?> ResolveUsernameAsync(int? userId)
            {
                if (!userId.HasValue) return null;
                if (usernameCache.TryGetValue(userId.Value, out var cached)) return cached;
                var user = await _userRepo.GetByIdAsync(userId.Value);
                var username = user?.Username;
                usernameCache[userId.Value] = username;
                return username;
            }

            var items = new List<AuditLogEntryDto>();
            foreach (var entry in result.Items)
            {
                items.Add(new AuditLogEntryDto
                {
                    Id = entry.Id,
                    Timestamp = DateTime.SpecifyKind(entry.Timestamp, DateTimeKind.Utc),
                    ActorUserId = entry.ActorUserId,
                    ActorUsername = await ResolveUsernameAsync(entry.ActorUserId),
                    TargetUserId = entry.TargetUserId,
                    TargetUsername = await ResolveUsernameAsync(entry.TargetUserId),
                    Action = entry.Action.ToString(),
                    Details = entry.Details
                });
            }

            return new PagedResultDto<AuditLogEntryDto>
            {
                Items = items,
                TotalCount = result.TotalCount,
                PageNumber = result.PageNumber,
                PageSize = result.PageSize
            };
        }
    }
}
