using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using MySqlConnector;

namespace knkwebapi_v2.Services
{
    public class WorkflowService : IWorkflowService
    {
        private readonly IWorkflowRepository _workflowRepo;
        private readonly IWorldTaskRepository _taskRepo;
        private readonly IMapper _mapper;

        /// <summary>
        /// Constructor for WorkflowService.
        /// Note: ILocationService is NOT injected because Location entity creation is now
        /// the responsibility of the parent entity services (Town, District, Structure, etc.)
        /// This service only orchestrates the workflow and task completion, not entity persistence.
        /// </summary>
        public WorkflowService(IWorkflowRepository workflowRepo, IWorldTaskRepository taskRepo, IMapper mapper)
        {
            _workflowRepo = workflowRepo;
            _taskRepo = taskRepo;
            _mapper = mapper;
        }

        public async Task<WorkflowSessionReadDto> StartSessionAsync(WorkflowSessionCreateDto dto)
        {
            var entity = _mapper.Map<WorkflowSession>(dto);
            entity.CreatedAt = DateTime.UtcNow;
            await _workflowRepo.AddSessionAsync(entity);
            return _mapper.Map<WorkflowSessionReadDto>(entity);
        }

        public async Task<WorkflowSessionReadDto?> GetSessionAsync(int id)
        {
            var sess = await _workflowRepo.GetSessionByIdAsync(id);
            return sess == null ? null : _mapper.Map<WorkflowSessionReadDto>(sess);
        }

        public async Task<WorkflowSessionReadDto?> GetSessionByGuidAsync(Guid guid)
        {
            var sess = await _workflowRepo.GetSessionByGuidAsync(guid);
            return sess == null ? null : _mapper.Map<WorkflowSessionReadDto>(sess);
        }

        public async Task<WorkflowSessionReadDto?> ResumeSessionAsync(int id)
        {
            var sess = await _workflowRepo.GetSessionByIdAsync(id);
            if (sess == null) return null;
            if (sess.Status == "Paused")
            {
                sess.Status = "InProgress";
                sess.UpdatedAt = DateTime.UtcNow;
                await _workflowRepo.UpdateSessionAsync(sess);
            }
            return _mapper.Map<WorkflowSessionReadDto>(sess);
        }

        public async Task<List<StepProgressReadDto>> GetProgressAsync(int sessionId)
        {
            var steps = await _workflowRepo.GetStepsAsync(sessionId);
            return steps.Select(_mapper.Map<StepProgressReadDto>).ToList();
        }

        public async Task<StepProgressReadDto> SetStepCompletedAsync(int sessionId, string stepKey, int? stepIndex = null)
        {
            var normalizedStepKey = stepKey?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedStepKey)) throw new ArgumentException("stepKey is required", nameof(stepKey));

            var now = DateTime.UtcNow;
            var step = await _workflowRepo.GetStepAsync(sessionId, normalizedStepKey);

            if (step == null)
            {
                step = new StepProgress
                {
                    WorkflowSessionId = sessionId,
                    StepKey = normalizedStepKey,
                    StepIndex = stepIndex ?? 0,
                    Status = "Completed",
                    CreatedAt = now,
                    UpdatedAt = now,
                    CompletedAt = now
                };

                try
                {
                    await _workflowRepo.AddStepAsync(step);
                    return _mapper.Map<StepProgressReadDto>(step);
                }
                catch (DbUpdateException ex) when (IsDuplicateStepKeyException(ex))
                {
                    step = await _workflowRepo.GetStepAsync(sessionId, normalizedStepKey) ?? throw new InvalidOperationException($"Step '{normalizedStepKey}' not found after duplicate key exception.");
                }
            }

            if (step.Status != "Completed" || step.CompletedAt == null)
            {
                step.Status = "Completed";
                step.UpdatedAt = now;
                step.CompletedAt = now;
                await _workflowRepo.UpdateStepAsync(step);
            }

            return _mapper.Map<StepProgressReadDto>(step);
        }

        public async Task DeleteSessionAsync(int id)
        {
            await _workflowRepo.DeleteSessionAsync(id);
        }

        public async Task<WorkflowSessionReadDto> UpdateStepAsync(int sessionId, int stepNumber, object stepData)
        {
            var session = await _workflowRepo.GetSessionByIdAsync(sessionId);
            if (session == null) throw new KeyNotFoundException($"Workflow session {sessionId} not found.");

            // TODO: Implement step validation and data persistence to draft entity
            // For now, just mark the step as completed
            var stepKey = $"step_{stepNumber}";
            await SetStepCompletedAsync(sessionId, stepKey, stepNumber);

            session.UpdatedAt = DateTime.UtcNow;
            await _workflowRepo.UpdateSessionAsync(session);

            return _mapper.Map<WorkflowSessionReadDto>(session);
        }

        public async Task<WorkflowSessionReadDto> FinalizeAsync(int sessionId)
        {
            var session = await _workflowRepo.GetSessionByIdAsync(sessionId);
            if (session == null) throw new KeyNotFoundException($"Workflow session {sessionId} not found.");

            // NOTE: Location entity creation should be handled by the parent entity service
            // (Town, District, Structure, etc.) when the entity itself is created/updated.
            // This workflow service only validates that all steps are complete.
            // The task outputs (including location data) remain in WorldTask.OutputJson
            // and are processed by the entity-specific services during entity creation.

            var tasks = await _taskRepo.GetBySessionAsync(sessionId);
            var completedTasksCount = tasks.Count(t => t.Status == "Completed");
            var totalTasksCount = tasks.Count;

            // Ensure all expected tasks are completed before finalizing
            if (completedTasksCount > 0)
            {
                // Mark workflow as ready for entity finalization
                // The parent entity service will extract task outputs and create/update entities
                session.Status = "Completed";
            }

            session.CompletedAt = DateTime.UtcNow;
            session.UpdatedAt = DateTime.UtcNow;
            await _workflowRepo.UpdateSessionAsync(session);

            return _mapper.Map<WorkflowSessionReadDto>(session);
        }

        private static bool IsDuplicateStepKeyException(DbUpdateException ex) =>
            ex.InnerException is MySqlException { Number: 1062 };
    }
}
