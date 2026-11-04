using knkwebapi_v2.Dtos;

namespace knkwebapi_v2.Services.Interfaces
{
    public interface IWorldTaskService
    {
        Task<WorldTaskReadDto> CreateAsync(WorldTaskCreateDto dto);
        /// <summary>#180: callerUserId is the logged-in creator (JWT); null for the game server.</summary>
        Task<WorldTaskReadDto> CreateAsync(WorldTaskCreateDto dto, int? callerUserId);
            Task<WorldTaskReadDto> CreateFromFormFieldAsync(int workflowSessionId, int stepNumber, string stepKey, string fieldName, string taskType, string? inputJson = null, int? assignedUserId = null);
        Task<WorldTaskReadDto?> GetByIdAsync(int id);
        Task<WorldTaskReadDto?> GetByLinkCodeAsync(string linkCode);
        Task<List<WorldTaskReadDto>> ListByStatusAsync(string status, string? serverId = null);
        Task<PagedResultDto<WorldTaskReadDto>> SearchAsync(PagedQueryDto query);
        Task<List<WorldTaskReadDto>> GetBySessionAsync(int sessionId);
        Task<PagedResultDto<WorldTaskReadDto>> GetByUserAsync(int userId, PagedQueryDto query);
        Task<WorldTaskReadDto> ClaimAsync(int id, ClaimTaskDto dto);
        Task<WorldTaskReadDto> CompleteAsync(int id, CompleteTaskDto dto);
        Task<WorldTaskReadDto> FailAsync(int id, FailTaskDto dto);
        Task<WorldTaskReadDto> UpdateStatusAsync(int id, string status, string? payloadJson = null);
        Task DeleteAsync(int id);

        // PandiO/knk-workspace#180 (DESIGN §8.2, §8.7)
        Task<WorldTaskReadDto> CancelAsync(int id, CancelTaskDto? dto);
        Task<WorldTaskReadDto> ReleaseAsync(int id, ReleaseTaskDto? dto);
        Task<WorldTaskReadDto> HeartbeatAsync(int id, HeartbeatTaskDto? dto);
        Task<List<WorldTaskReadDto>> ListOpenForServerAsync(string serverId);
        Task<List<WorldTaskReadDto>> ListOpenBySessionAsync(int sessionId);
        Task<List<WorldTaskReadDto>> ListLatestForProgressAsync(int progressId);
        Task<WorldTaskReadDto> MarkAppliedAsync(int id);
    }
}
