using knkwebapi_v2.Models;

namespace knkwebapi_v2.Repositories.Interfaces
{
    public interface IWorldTaskRepository
    {
        Task<WorldTask?> GetByIdAsync(int id);
        Task<WorldTask?> GetByLinkCodeAsync(string linkCode);
        Task<List<WorldTask>> ListByStatusAsync(string status, string? serverId = null);
        Task AddAsync(WorldTask task);
        Task UpdateAsync(WorldTask task);
        Task DeleteAsync(int id);

        Task<PagedResult<WorldTask>> SearchAsync(PagedQuery query);
        Task<List<WorldTask>> GetBySessionAsync(int sessionId);
        Task<PagedResult<WorldTask>> GetByUserAsync(int userId, PagedQuery query);

        // PandiO/knk-workspace#180
        /// <summary>The open (Pending/InProgress) task with this task code, newest first; closed tasks are ignored.</summary>
        Task<WorldTask?> GetOpenByLinkCodeAsync(string linkCode);
        Task<bool> IsLinkCodeInUseAsync(string linkCode);
        /// <summary>InProgress tasks claimed by this game server.</summary>
        Task<List<WorldTask>> ListInProgressByServerAsync(string serverId);
        /// <summary>Open tasks of a workflow session (web reattach).</summary>
        Task<List<WorldTask>> ListOpenBySessionAsync(int sessionId);
        /// <summary>Every task of a draft (newest first).</summary>
        Task<List<WorldTask>> ListByProgressAsync(int progressId);
        /// <summary>Open tasks for the same field: of the draft when there is one, otherwise of the session (step + field).</summary>
        Task<List<WorldTask>> ListOpenForFieldAsync(int sessionId, int? progressId, string? stepKey, string fieldName);
        Task<FormSubmissionProgress?> GetProgressAsync(int progressId);
        Task UpdateProgressAsync(FormSubmissionProgress progress);
        Task<int?> FindUserIdByUuidAsync(string uuid);

        // PandiO/knk-workspace#181 (WT2)
        /// <summary>A user's Minecraft UUID and username, or null when the user doesn't exist.</summary>
        Task<(string? Uuid, string Username)?> GetUserBriefAsync(int userId);
        /// <summary>InProgress tasks claimed by this user (the busy check).</summary>
        Task<List<WorldTask>> ListInProgressClaimedByUserAsync(int userId);
        /// <summary>Pending tasks targeted at these users in one of these offer states, oldest first.</summary>
        Task<List<WorldTask>> ListPendingTargetedAsync(IReadOnlyCollection<int> userIds, IReadOnlyCollection<string> offerStates);
        /// <summary>The name of the form a task belongs to (its draft's, else its session's form configuration).</summary>
        Task<string?> GetFormNameAsync(WorldTask task);
    }
}
