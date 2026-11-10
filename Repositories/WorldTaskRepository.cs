using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Repositories
{
    public class WorldTaskRepository : Interfaces.IWorldTaskRepository
    {
        private readonly KnKDbContext _context;

        public WorldTaskRepository(KnKDbContext context)
        {
            _context = context;
        }

        public async Task<WorldTask?> GetByIdAsync(int id)
        {
            return await _context.WorldTasks
                .Include(t => t.WorkflowSession)
                .Include(t => t.TargetUser) // #181: the offer's target username
                .FirstOrDefaultAsync(t => t.Id == id);
        }

        public async Task<WorldTask?> GetByLinkCodeAsync(string linkCode)
        {
            return await _context.WorldTasks
                .Include(t => t.WorkflowSession)
                .FirstOrDefaultAsync(t => t.LinkCode == linkCode);
        }

        public async Task<List<WorldTask>> ListByStatusAsync(string status, string? serverId = null)
        {
            var q = _context.WorldTasks.Where(t => t.Status == status);
            if (!string.IsNullOrWhiteSpace(serverId))
            {
                q = q.Where(t => t.ClaimedByServerId == serverId || t.ClaimedByServerId == null);
            }
            return await q.OrderByDescending(t => t.CreatedAt).ToListAsync();
        }

        public async Task AddAsync(WorldTask task)
        {
            await _context.WorldTasks.AddAsync(task);
            await _context.SaveChangesAsync();
        }

        public async Task UpdateAsync(WorldTask task)
        {
            _context.WorldTasks.Update(task);
            await _context.SaveChangesAsync();
        }

        public async Task DeleteAsync(int id)
        {
            var entity = await _context.WorldTasks.FindAsync(id);
            if (entity != null)
            {
                _context.WorldTasks.Remove(entity);
                await _context.SaveChangesAsync();
            }
        }

        public async Task<PagedResult<WorldTask>> SearchAsync(PagedQuery query)
        {
            var q = _context.WorldTasks.AsQueryable();

            if (!string.IsNullOrWhiteSpace(query.SearchTerm))
            {
                var term = query.SearchTerm.ToLower();
                q = q.Where(t => t.TaskType.ToLower().Contains(term) || (t.Status.ToLower().Contains(term)));
            }

            if (query.Filters != null)
            {
                if (query.Filters.TryGetValue("sessionId", out var sid) && int.TryParse(sid, out var sessionId))
                {
                    q = q.Where(t => t.WorkflowSessionId == sessionId);
                }
                if (query.Filters.TryGetValue("status", out var status) && !string.IsNullOrWhiteSpace(status))
                {
                    q = q.Where(t => t.Status == status);
                }
                if (query.Filters.TryGetValue("assignedUserId", out var auid) && int.TryParse(auid, out var assignedUserId))
                {
                    q = q.Where(t => t.AssignedUserId == assignedUserId);
                }
            }

            q = ApplySorting(q, query.SortBy, query.SortDescending);

            var total = await q.CountAsync();
            var items = await q
                .OrderByDescending(t => t.CreatedAt)
                .Skip((query.PageNumber - 1) * query.PageSize)
                .Take(query.PageSize)
                .ToListAsync();

            return new PagedResult<WorldTask>
            {
                Items = items,
                TotalCount = total,
                PageNumber = query.PageNumber,
                PageSize = query.PageSize
            };
        }

        public async Task<List<WorldTask>> GetBySessionAsync(int sessionId)
        {
            return await _context.WorldTasks
                .Where(t => t.WorkflowSessionId == sessionId)
                .OrderByDescending(t => t.CreatedAt)
                .ToListAsync();
        }

        public async Task<PagedResult<WorldTask>> GetByUserAsync(int userId, PagedQuery query)
        {
            query.Filters ??= new Dictionary<string, string>();
            query.Filters["assignedUserId"] = userId.ToString();
            return await SearchAsync(query);
        }

        public async Task<WorldTask?> GetOpenByLinkCodeAsync(string linkCode)
        {
            return await _context.WorldTasks
                .Include(t => t.WorkflowSession)
                .Where(t => t.LinkCode == linkCode && (t.Status == WorldTaskStatuses.Pending || t.Status == WorldTaskStatuses.InProgress))
                .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
                .FirstOrDefaultAsync();
        }

        public async Task<bool> IsLinkCodeInUseAsync(string linkCode)
        {
            return await _context.WorldTasks.AnyAsync(t => t.LinkCode == linkCode
                && (t.Status == WorldTaskStatuses.Pending || t.Status == WorldTaskStatuses.InProgress));
        }

        public async Task<List<WorldTask>> ListInProgressByServerAsync(string serverId)
        {
            return await _context.WorldTasks
                .Where(t => t.Status == WorldTaskStatuses.InProgress && t.ClaimedByServerId == serverId)
                .OrderBy(t => t.Id)
                .ToListAsync();
        }

        public async Task<List<WorldTask>> ListOpenBySessionAsync(int sessionId)
        {
            return await _context.WorldTasks
                .Where(t => t.WorkflowSessionId == sessionId
                    && (t.Status == WorldTaskStatuses.Pending || t.Status == WorldTaskStatuses.InProgress))
                .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
                .ToListAsync();
        }

        public async Task<List<WorldTask>> ListByProgressAsync(int progressId)
        {
            return await _context.WorldTasks
                .Where(t => t.FormSubmissionProgressId == progressId)
                .OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id)
                .ToListAsync();
        }

        public async Task<List<WorldTask>> ListOpenForFieldAsync(int sessionId, int? progressId, string? stepKey, string fieldName)
        {
            var q = _context.WorldTasks.Where(t => t.FieldName == fieldName
                && (t.Status == WorldTaskStatuses.Pending || t.Status == WorldTaskStatuses.InProgress));
            q = progressId != null
                ? q.Where(t => t.FormSubmissionProgressId == progressId)
                : q.Where(t => t.WorkflowSessionId == sessionId && t.StepKey == stepKey);
            return await q.ToListAsync();
        }

        public async Task<FormSubmissionProgress?> GetProgressAsync(int progressId)
        {
            return await _context.FormSubmissionProgresses.FirstOrDefaultAsync(p => p.Id == progressId);
        }

        public async Task UpdateProgressAsync(FormSubmissionProgress progress)
        {
            _context.FormSubmissionProgresses.Update(progress);
            await _context.SaveChangesAsync();
        }

        public async Task<int?> FindUserIdByUuidAsync(string uuid)
        {
            var normalized = uuid.Trim();
            var compact = normalized.Replace("-", string.Empty);
            return await _context.Users
                .Where(u => u.Uuid != null && (u.Uuid == normalized || u.Uuid == compact))
                .Select(u => (int?)u.Id)
                .FirstOrDefaultAsync();
        }

        public async Task<(string? Uuid, string Username)?> GetUserBriefAsync(int userId)
        {
            var user = await _context.Users.AsNoTracking()
                .Where(u => u.Id == userId)
                .Select(u => new { u.Uuid, u.Username })
                .FirstOrDefaultAsync();
            return user == null ? null : (user.Uuid, user.Username);
        }

        public async Task<List<WorldTask>> ListInProgressClaimedByUserAsync(int userId)
        {
            return await _context.WorldTasks
                .Include(t => t.WorkflowSession)
                .Where(t => t.Status == WorldTaskStatuses.InProgress && t.ClaimedByUserId == userId)
                .OrderBy(t => t.Id)
                .ToListAsync();
        }

        public async Task<List<WorldTask>> ListPendingTargetedAsync(IReadOnlyCollection<int> userIds, IReadOnlyCollection<string> offerStates)
        {
            if (userIds.Count == 0 || offerStates.Count == 0) return new List<WorldTask>();
            var ids = userIds.ToList();
            var states = offerStates.ToList();
            return await _context.WorldTasks
                .Include(t => t.WorkflowSession)
                .Include(t => t.TargetUser)
                .Where(t => t.Status == WorldTaskStatuses.Pending && t.TargetUserId != null
                    && ids.Contains(t.TargetUserId.Value) && states.Contains(t.OfferState))
                .OrderBy(t => t.CreatedAt).ThenBy(t => t.Id)
                .ToListAsync();
        }

        public async Task<string?> GetFormNameAsync(WorldTask task)
        {
            if (task.FormSubmissionProgressId is { } progressId)
            {
                var name = await _context.FormSubmissionProgresses.AsNoTracking()
                    .Where(p => p.Id == progressId)
                    .Select(p => p.FormConfiguration.Name)
                    .FirstOrDefaultAsync();
                if (!string.IsNullOrWhiteSpace(name)) return name;
            }
            var sessionFormId = task.WorkflowSession?.FormConfigurationId
                ?? await _context.WorkflowSessions.AsNoTracking().Where(s => s.Id == task.WorkflowSessionId)
                    .Select(s => s.FormConfigurationId).FirstOrDefaultAsync();
            if (sessionFormId is { } formId)
            {
                return await _context.FormConfigurations.AsNoTracking()
                    .Where(f => f.Id == formId)
                    .Select(f => f.Name)
                    .FirstOrDefaultAsync();
            }
            return null;
        }

        private static IQueryable<WorldTask> ApplySorting(IQueryable<WorldTask> q, string? sortBy, bool desc)
        {
            if (string.IsNullOrWhiteSpace(sortBy)) return desc ? q.OrderByDescending(t => t.CreatedAt) : q.OrderBy(t => t.CreatedAt);
            return sortBy.ToLower() switch
            {
                "status" => desc ? q.OrderByDescending(t => t.Status) : q.OrderBy(t => t.Status),
                "tasktype" => desc ? q.OrderByDescending(t => t.TaskType) : q.OrderBy(t => t.TaskType),
                _ => desc ? q.OrderByDescending(t => t.CreatedAt) : q.OrderBy(t => t.CreatedAt)
            };
        }
    }
}
