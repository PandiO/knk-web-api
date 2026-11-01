using AutoMapper;
using knkwebapi_v2.Configuration;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using MySqlConnector;
using System.Security.Cryptography;
using System.Text.Json;

namespace knkwebapi_v2.Services
{
    /// <summary>
    /// World tasks (web form field → game server). PandiO/knk-workspace#180 (DESIGN §8.2, §8.7): the status moves only
    /// along <see cref="WorldTaskStatuses.CanTransition"/>, every write is conditional on the stored status and row
    /// version (a lost race answers 409), the task code is checked on a code claim and unique among open tasks, a claim
    /// is a lease the game server renews with heartbeats, and a task belongs to the form draft whose field it fills.
    /// </summary>
    public class WorldTaskService : IWorldTaskService
    {
        /// <summary>Task codes: 6 characters from A-Z0-9 (as before #180), now from a CSPRNG.</summary>
        public const string TaskCodeAlphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        public const int TaskCodeLength = 6;
        private const int TaskCodeAttempts = 20;

        /// <summary>Headless task types: the game server claims and runs them itself, without a player.</summary>
        public static readonly string[] HeadlessTaskTypes =
        {
            WorldTaskTypes.GateBlockScan, WorldTaskTypes.GateOpenedBlockScan, WorldTaskTypes.MinecraftCatalogScan
        };

        private readonly IWorldTaskRepository _taskRepo;
        private readonly IWorkflowRepository _workflowRepo;
        private readonly IGateDoorService _gateDoorService;
        private readonly IMapper _mapper;
        private readonly WorldTaskOptions _options;
        private readonly Func<DateTime> _utcNow;

        public WorldTaskService(
            IWorldTaskRepository taskRepo,
            IWorkflowRepository workflowRepo,
            IGateDoorService gateDoorService,
            IMapper mapper,
            IOptions<WorldTaskOptions>? options = null,
            Func<DateTime>? utcNow = null)
        {
            _taskRepo = taskRepo;
            _workflowRepo = workflowRepo;
            _gateDoorService = gateDoorService;
            _mapper = mapper;
            _options = options?.Value ?? new WorldTaskOptions();
            _utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        public Task<WorldTaskReadDto> CreateAsync(WorldTaskCreateDto dto) => CreateAsync(dto, null);

        /// <param name="callerUserId">#180: the logged-in web user (the JWT); null for the game server, which makes the session owner the creator.</param>
        public async Task<WorldTaskReadDto> CreateAsync(WorldTaskCreateDto dto, int? callerUserId)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));

            // Validate that critical routing fields are provided
            if (dto.WorkflowSessionId <= 0)
                throw new ArgumentException("WorkflowSessionId must be greater than 0", nameof(dto.WorkflowSessionId));

            if (dto.StepNumber < 0)
                throw new ArgumentException("StepNumber must be >= 0", nameof(dto.StepNumber));

            dto.StepKey = dto.StepKey?.Trim();
            if (string.IsNullOrWhiteSpace(dto.StepKey))
                throw new ArgumentException("StepKey is required for workflow context", nameof(dto.StepKey));

            var payloadJson = dto.InputJson ?? dto.PayloadJson;
            var normalizedFieldName = string.IsNullOrWhiteSpace(dto.FieldName) ? null : dto.FieldName.Trim();
            if (normalizedFieldName == null && !string.IsNullOrWhiteSpace(payloadJson))
            {
                normalizedFieldName = ExtractFieldNameFromPayload(payloadJson);
            }

            if (string.IsNullOrWhiteSpace(normalizedFieldName))
                throw new ArgumentException("FieldName is required for plugin field routing", nameof(dto.FieldName));

            dto.FieldName = normalizedFieldName;

            if (string.IsNullOrWhiteSpace(dto.TaskType))
                throw new ArgumentException("TaskType is required", nameof(dto.TaskType));

            var session = await _workflowRepo.GetSessionByIdAsync(dto.WorkflowSessionId)
                ?? throw new ArgumentException($"Workflow session {dto.WorkflowSessionId} does not exist.", nameof(dto.WorkflowSessionId));

            FormSubmissionProgress? draft = null;
            if (dto.FormSubmissionProgressId is { } progressId)
            {
                draft = await _taskRepo.GetProgressAsync(progressId)
                    ?? throw new ArgumentException($"Form draft {progressId} does not exist.", nameof(dto.FormSubmissionProgressId));
            }

            await EnsureGateScanCanStartAsync(dto.TaskType, payloadJson);

            var now = _utcNow();
            var entity = _mapper.Map<WorldTask>(dto);
            entity.Status = WorldTaskStatuses.Pending;
            entity.CreatedAt = now;
            entity.FieldName = normalizedFieldName;
            entity.FormSubmissionProgressId = draft?.Id;
            // #180: the creator is the logged-in caller, never a value from the body.
            entity.CreatedByUserId = callerUserId ?? (session.UserId > 0 ? session.UserId : null);
            entity.ExpiresAt = now + _options.PendingTtl;
            entity.OfferState = WorldTaskOfferStates.None;
            if (payloadJson != null)
            {
                entity.InputJson ??= payloadJson;
                entity.PayloadJson ??= payloadJson;
            }

            entity.LinkCode = await GenerateUniqueTaskCodeAsync();

            // DESIGN §8.7: "Capture again" on a field replaces its task; the previous open one is cancelled.
            var replaced = await _taskRepo.ListOpenForFieldAsync(session.Id, draft?.Id, dto.StepKey, normalizedFieldName);

            await _taskRepo.AddAsync(entity);

            foreach (var previous in replaced.Where(t => t.Id != entity.Id))
            {
                try
                {
                    Close(previous, WorldTaskStatuses.Cancelled, $"Replaced by task #{entity.Id}.");
                    await SaveAsync(previous);
                }
                catch (WorldTaskConflictException)
                {
                    // It moved meanwhile (completed or claimed elsewhere); leave it.
                }
            }

            // One session per draft: the first task of a draft links the draft to its session.
            if (draft != null && draft.WorkflowSessionId == null)
            {
                draft.WorkflowSessionId = session.Id;
                await _taskRepo.UpdateProgressAsync(draft);
            }

            entity.WorkflowSession ??= session;
            return _mapper.Map<WorldTaskReadDto>(entity);
        }

        public async Task<WorldTaskReadDto?> GetByIdAsync(int id)
        {
            var item = await _taskRepo.GetByIdAsync(id);
            return item == null ? null : _mapper.Map<WorldTaskReadDto>(item);
        }

        /// <summary>#180: only an open task answers to its code; codes of closed tasks are dead.</summary>
        public async Task<WorldTaskReadDto?> GetByLinkCodeAsync(string linkCode)
        {
            var code = NormalizeTaskCode(linkCode);
            if (code == null) return null;
            var item = await _taskRepo.GetOpenByLinkCodeAsync(code);
            return item == null ? null : _mapper.Map<WorldTaskReadDto>(item);
        }

        public async Task<List<WorldTaskReadDto>> ListByStatusAsync(string status, string? serverId = null)
        {
            var items = await _taskRepo.ListByStatusAsync(WorldTaskStatuses.Normalize(status) ?? status, serverId);
            return items.Select(_mapper.Map<WorldTaskReadDto>).ToList();
        }

        public async Task<PagedResultDto<WorldTaskReadDto>> SearchAsync(PagedQueryDto queryDto)
        {
            var q = _mapper.Map<PagedQuery>(queryDto);
            var result = await _taskRepo.SearchAsync(q);
            return new PagedResultDto<WorldTaskReadDto>
            {
                Items = result.Items.Select(_mapper.Map<WorldTaskReadDto>).ToList(),
                TotalCount = result.TotalCount,
                PageNumber = result.PageNumber,
                PageSize = result.PageSize
            };
        }

        public async Task<List<WorldTaskReadDto>> GetBySessionAsync(int sessionId)
        {
            var items = await _taskRepo.GetBySessionAsync(sessionId);
            return items.Select(_mapper.Map<WorldTaskReadDto>).ToList();
        }

        public async Task<PagedResultDto<WorldTaskReadDto>> GetByUserAsync(int userId, PagedQueryDto queryDto)
        {
            var q = _mapper.Map<PagedQuery>(queryDto);
            var result = await _taskRepo.GetByUserAsync(userId, q);
            return new PagedResultDto<WorldTaskReadDto>
            {
                Items = result.Items.Select(_mapper.Map<WorldTaskReadDto>).ToList(),
                TotalCount = result.TotalCount,
                PageNumber = result.PageNumber,
                PageSize = result.PageSize
            };
        }

        /// <summary>
        /// POST {id}/status (no caller in the web app or plugin). #180: only a known status and a transition the state
        /// machine allows; Completed goes through the same step bookkeeping as before.
        /// </summary>
        public async Task<WorldTaskReadDto> UpdateStatusAsync(int id, string status, string? payloadJson = null)
        {
            var entity = await _taskRepo.GetByIdAsync(id) ?? throw new KeyNotFoundException();
            var target = WorldTaskStatuses.Normalize(status)
                ?? throw new ArgumentException($"Unknown status '{status}'. Use one of: {string.Join(", ", WorldTaskStatuses.All)}.", nameof(status));

            if (target != entity.Status && !WorldTaskStatuses.CanTransition(entity.Status, target))
            {
                throw new WorldTaskConflictException($"A {entity.Status} task cannot become {target}.", entity.Status);
            }

            var now = _utcNow();
            if (target == WorldTaskStatuses.Pending && entity.Status == WorldTaskStatuses.InProgress)
            {
                ReleaseClaim(entity, now);
            }
            else if (target != entity.Status && !WorldTaskStatuses.IsOpen(target))
            {
                Close(entity, target, entity.ErrorMessage);
            }
            entity.Status = target;
            if (payloadJson != null)
            {
                entity.PayloadJson = payloadJson;
            }
            entity.UpdatedAt = now;
            if (target == WorldTaskStatuses.Completed)
            {
                entity.CompletedAt = now;

                // Link to StepProgress if configured (idempotent, handles duplicate keys)
                if (!string.IsNullOrWhiteSpace(entity.StepKey))
                {
                    var step = await GetOrCreateStepAsync(entity.WorkflowSessionId, entity.StepKey!, entity.StepNumber ?? 0);
                    if (step.Status != "Completed" || step.CompletedAt == null)
                    {
                        step.Status = "Completed";
                        step.UpdatedAt = now;
                        step.CompletedAt = now;
                        await _workflowRepo.UpdateStepAsync(step);
                    }
                }
            }

            await SaveAsync(entity);
            return _mapper.Map<WorldTaskReadDto>(entity);
        }

        /// <summary>
        /// Pending → InProgress (#180). A repeat claim by the same player (uuid) on the same server is idempotent and renews
        /// the lease; any other claim of a task that is not Pending answers 409, as does a claim that loses the race.
        /// </summary>
        public async Task<WorldTaskReadDto> ClaimAsync(int id, ClaimTaskDto dto)
        {
            if (dto == null) throw new ArgumentNullException(nameof(dto));
            var entity = await _taskRepo.GetByIdAsync(id) ?? throw new KeyNotFoundException();
            var now = _utcNow();
            var serverId = NormalizeServerId(dto.EffectiveServerId);
            var uuid = NormalizeUuid(dto.ClaimedByUuid);
            var code = NormalizeTaskCode(dto.EffectiveCode);
            var mode = (dto.ClaimMode ?? (code != null ? WorldTaskClaimModes.Code
                : uuid == null ? WorldTaskClaimModes.Headless : WorldTaskClaimModes.Code)).Trim().ToLowerInvariant();

            if (entity.Status == WorldTaskStatuses.InProgress)
            {
                var samePlayer = uuid != null && string.Equals(entity.ClaimedByUuid, uuid, StringComparison.OrdinalIgnoreCase);
                var sameHeadless = uuid == null && mode == WorldTaskClaimModes.Headless && entity.ClaimedByUuid == null;
                var sameServer = serverId == null || entity.ClaimedByServerId == null || entity.ClaimedByServerId == serverId;
                if ((samePlayer || sameHeadless) && sameServer)
                {
                    entity.LeaseUntil = now + _options.Lease;
                    entity.UpdatedAt = now;
                    await SaveAsync(entity);
                    return _mapper.Map<WorldTaskReadDto>(entity);
                }
                throw new WorldTaskConflictException(
                    $"Task {id} is already claimed{(entity.ClaimedByMinecraftUsername != null ? $" by {entity.ClaimedByMinecraftUsername}" : "")}.",
                    entity.Status);
            }

            if (entity.Status != WorldTaskStatuses.Pending)
            {
                throw new WorldTaskConflictException($"Task {id} is {entity.Status} and can no longer be claimed.", entity.Status);
            }

            if (IsPastExpiry(entity, now))
            {
                Close(entity, WorldTaskStatuses.Expired, ExpiredMessage());
                await SaveAsync(entity);
                throw new WorldTaskConflictException($"Task {id} has expired.", WorldTaskStatuses.Expired);
            }

            int? claimerUserId = uuid != null ? await _taskRepo.FindUserIdByUuidAsync(uuid) : null;
            switch (mode)
            {
                case WorldTaskClaimModes.Code:
                    if (entity.LinkCode == null)
                        throw new ArgumentException("This task has no task code; it cannot be claimed by code.");
                    if (code == null || !string.Equals(NormalizeTaskCode(entity.LinkCode), code, StringComparison.Ordinal))
                        throw new ArgumentException("The task code does not match this task.");
                    break;
                case WorldTaskClaimModes.Id:
                    // The game server checked knk.tasks.any (owner/rescue, DESIGN Q11) before sending an id claim.
                    if (uuid == null) throw new ArgumentException("An id claim needs the claiming player's uuid.");
                    break;
                case WorldTaskClaimModes.Headless:
                    if (!HeadlessTaskTypes.Contains(entity.TaskType))
                        throw new ArgumentException($"A {entity.TaskType} task needs a player; claim it with its task code.");
                    break;
                case WorldTaskClaimModes.Offer:
                    if (entity.TargetUserId == null || claimerUserId != entity.TargetUserId)
                        throw new WorldTaskConflictException($"Task {id} was not offered to this player.", entity.Status);
                    entity.OfferState = WorldTaskOfferStates.Accepted;
                    break;
                default:
                    throw new ArgumentException($"Unknown claim mode '{dto.ClaimMode}'.");
            }

            entity.Status = WorldTaskStatuses.InProgress;
            entity.ClaimedAt = now;
            entity.UpdatedAt = now;
            entity.ClaimedByServerId = serverId;
            entity.ClaimedByMinecraftUsername = string.IsNullOrWhiteSpace(dto.EffectiveUsername) ? null : dto.EffectiveUsername!.Trim();
            entity.ClaimedByUuid = uuid;
            entity.ClaimedByUserId = claimerUserId;
            entity.LeaseUntil = now + _options.Lease;

            await SaveAsync(entity, $"Task {id} was claimed by someone else just now.");
            return _mapper.Map<WorldTaskReadDto>(entity);
        }

        public async Task<WorldTaskReadDto> CompleteAsync(int id, CompleteTaskDto dto)
        {
            var entity = await _taskRepo.GetByIdAsync(id) ?? throw new KeyNotFoundException();

            // Idempotent check: if already Completed, return existing
            if (entity.Status == WorldTaskStatuses.Completed)
            {
                return _mapper.Map<WorldTaskReadDto>(entity);
            }

            // #180: only a claimed task completes, and only on the server holding the claim.
            EnsureHeldBy(entity, dto.ServerId, "completed");

            // VALIDATION: Re-validate output against validation rules from InputJson
            var validationResult = await ValidateTaskOutputAsync(entity, dto.OutputJson);
            if (!validationResult.IsValid && validationResult.IsBlocking)
            {
                // Validation failed and is blocking - reject completion
                Close(entity, WorldTaskStatuses.Failed, validationResult.Message ?? "Validation failed");
                await SaveAsync(entity);

                throw new WorldTaskRejectedException($"Task validation failed: {validationResult.Message}");
            }

            if (entity.TaskType == WorldTaskTypes.GateBlockScan)
            {
                var scanError = await TryApplyGateBlockScanResultAsync(entity, dto.OutputJson);
                if (scanError != null)
                {
                    Close(entity, WorldTaskStatuses.Failed, scanError);
                    await SaveAsync(entity);

                    throw new WorldTaskRejectedException($"Gate block scan result could not be applied: {scanError}");
                }
            }
            else if (entity.TaskType == WorldTaskTypes.GateOpenedBlockScan)
            {
                var scanError = await TryApplyGateOpenedBlockScanResultAsync(entity, dto.OutputJson);
                if (scanError != null)
                {
                    Close(entity, WorldTaskStatuses.Failed, scanError);
                    await SaveAsync(entity);

                    throw new WorldTaskRejectedException($"Gate opened-block scan result could not be applied: {scanError}");
                }
            }

            var now = _utcNow();
            Close(entity, WorldTaskStatuses.Completed, null);
            entity.OutputJson = dto.OutputJson;
            entity.CompletedAt = now;

            // Link to StepProgress if configured
            if (!string.IsNullOrWhiteSpace(entity.StepKey))
            {
                var step = await GetOrCreateStepAsync(entity.WorkflowSessionId, entity.StepKey!, entity.StepNumber ?? 0);
                if (step.Status != "Completed" || step.CompletedAt == null)
                {
                    step.Status = "Completed";
                    step.UpdatedAt = now;
                    step.CompletedAt = now;
                    await _workflowRepo.UpdateStepAsync(step);
                }
            }

            await SaveAsync(entity);
            return _mapper.Map<WorldTaskReadDto>(entity);
        }

        public async Task<WorldTaskReadDto> FailAsync(int id, FailTaskDto dto)
        {
            var entity = await _taskRepo.GetByIdAsync(id) ?? throw new KeyNotFoundException();
            if (entity.Status == WorldTaskStatuses.Failed)
            {
                return _mapper.Map<WorldTaskReadDto>(entity);
            }

            EnsureHeldBy(entity, dto.ServerId, "failed");
            Close(entity, WorldTaskStatuses.Failed, dto.ErrorMessage);

            await SaveAsync(entity);
            return _mapper.Map<WorldTaskReadDto>(entity);
        }

        /// <summary>
        /// #180: Pending/InProgress → Cancelled (web: Capture again, Try again, leaving the form, "Cancel task"). Cancelling
        /// a cancelled task returns it; a closed task answers 409. The game server learns on its next heartbeat (409).
        /// </summary>
        public async Task<WorldTaskReadDto> CancelAsync(int id, CancelTaskDto? dto)
        {
            var entity = await _taskRepo.GetByIdAsync(id) ?? throw new KeyNotFoundException();
            if (entity.Status == WorldTaskStatuses.Cancelled)
            {
                return _mapper.Map<WorldTaskReadDto>(entity);
            }
            if (!WorldTaskStatuses.IsOpen(entity.Status))
            {
                throw new WorldTaskConflictException($"Task {id} is {entity.Status} and can no longer be cancelled.", entity.Status);
            }

            var reason = string.IsNullOrWhiteSpace(dto?.Reason) ? "Cancelled in the web app." : Truncate(dto!.Reason!.Trim(), 500);
            Close(entity, WorldTaskStatuses.Cancelled, reason);
            await SaveAsync(entity);
            return _mapper.Map<WorldTaskReadDto>(entity);
        }

        /// <summary>
        /// #180: InProgress → Pending (game server: in-game cancel, quit, restart, plugin disable). The task keeps its code and
        /// gets a fresh expiry, so the form can still get it done. Releasing a Pending task returns it.
        /// </summary>
        public async Task<WorldTaskReadDto> ReleaseAsync(int id, ReleaseTaskDto? dto)
        {
            var entity = await _taskRepo.GetByIdAsync(id) ?? throw new KeyNotFoundException();
            if (entity.Status == WorldTaskStatuses.Pending)
            {
                return _mapper.Map<WorldTaskReadDto>(entity);
            }

            EnsureHeldBy(entity, dto?.ServerId, "released");
            ReleaseClaim(entity, _utcNow());
            await SaveAsync(entity);
            return _mapper.Map<WorldTaskReadDto>(entity);
        }

        /// <summary>#180: renews the claim's lease. 409 when the task is no longer InProgress on this server (stop the handler).</summary>
        public async Task<WorldTaskReadDto> HeartbeatAsync(int id, HeartbeatTaskDto? dto)
        {
            var entity = await _taskRepo.GetByIdAsync(id) ?? throw new KeyNotFoundException();
            EnsureHeldBy(entity, dto?.ServerId, "renewed");
            var now = _utcNow();
            entity.LeaseUntil = now + _options.Lease;
            entity.UpdatedAt = now;
            await SaveAsync(entity);
            return _mapper.Map<WorldTaskReadDto>(entity);
        }

        /// <summary>#180: GET open?serverId= — the tasks this game server holds (plugin resync after a restart).</summary>
        public async Task<List<WorldTaskReadDto>> ListOpenForServerAsync(string serverId)
        {
            var normalized = NormalizeServerId(serverId) ?? throw new ArgumentException("serverId is required.", nameof(serverId));
            var items = await _taskRepo.ListInProgressByServerAsync(normalized);
            return items.Select(_mapper.Map<WorldTaskReadDto>).ToList();
        }

        /// <summary>#180: GET session/{id}/open — the open tasks of a workflow session (web reattach).</summary>
        public async Task<List<WorldTaskReadDto>> ListOpenBySessionAsync(int sessionId)
        {
            var items = await _taskRepo.ListOpenBySessionAsync(sessionId);
            return items.Select(_mapper.Map<WorldTaskReadDto>).ToList();
        }

        /// <summary>DESIGN §8.7: the latest task per field of a draft, whatever its status (the field panel reattaches to it).</summary>
        public async Task<List<WorldTaskReadDto>> ListLatestForProgressAsync(int progressId)
        {
            var items = await _taskRepo.ListByProgressAsync(progressId);
            return items
                .GroupBy(t => t.FieldName ?? string.Empty, StringComparer.Ordinal)
                .Select(g => g.OrderByDescending(t => t.CreatedAt).ThenByDescending(t => t.Id).First())
                .OrderBy(t => t.Id)
                .Select(_mapper.Map<WorldTaskReadDto>)
                .ToList();
        }

        /// <summary>DESIGN §8.7: the web app wrote the Completed result into the draft. Idempotent (keeps the first time).</summary>
        public async Task<WorldTaskReadDto> MarkAppliedAsync(int id)
        {
            var entity = await _taskRepo.GetByIdAsync(id) ?? throw new KeyNotFoundException();
            if (entity.Status != WorldTaskStatuses.Completed)
            {
                throw new WorldTaskConflictException($"Task {id} is {entity.Status}; only a completed result can be applied.", entity.Status);
            }
            if (entity.AppliedAt == null)
            {
                entity.AppliedAt = _utcNow();
                await SaveAsync(entity);
            }
            return _mapper.Map<WorldTaskReadDto>(entity);
        }

        /// <summary>A task code as typed: trimmed, upper case, without spaces or dashes; null when empty.</summary>
        public static string? NormalizeTaskCode(string? code)
        {
            if (string.IsNullOrWhiteSpace(code)) return null;
            var cleaned = new string(code.Where(c => !char.IsWhiteSpace(c) && c != '-').ToArray()).ToUpperInvariant();
            return cleaned.Length == 0 ? null : cleaned;
        }

        /// <summary>A new task code from <see cref="RandomNumberGenerator"/>.</summary>
        public static string NewTaskCode() => RandomNumberGenerator.GetString(TaskCodeAlphabet, TaskCodeLength);

        private async Task<string> GenerateUniqueTaskCodeAsync()
        {
            for (var attempt = 0; attempt < TaskCodeAttempts; attempt++)
            {
                var code = NewTaskCode();
                if (!await _taskRepo.IsLinkCodeInUseAsync(code))
                {
                    return code;
                }
            }
            throw new InvalidOperationException("Could not find a free task code.");
        }

        private void EnsureHeldBy(WorldTask entity, string? serverId, string verb)
        {
            if (entity.Status != WorldTaskStatuses.InProgress)
            {
                throw new WorldTaskConflictException(
                    $"Task {entity.Id} is {entity.Status}; only a claimed (InProgress) task can be {verb}.", entity.Status);
            }
            var server = NormalizeServerId(serverId);
            if (server != null && entity.ClaimedByServerId != null && entity.ClaimedByServerId != server)
            {
                throw new WorldTaskConflictException(
                    $"Task {entity.Id} is held by another game server.", entity.Status);
            }
        }

        private void ReleaseClaim(WorldTask entity, DateTime now)
        {
            entity.Status = WorldTaskStatuses.Pending;
            entity.ClaimedAt = null;
            entity.ClaimedByServerId = null;
            entity.ClaimedByMinecraftUsername = null;
            entity.ClaimedByUuid = null;
            entity.ClaimedByUserId = null;
            entity.LeaseUntil = null;
            entity.ReleaseCount++;
            entity.ExpiresAt = now + _options.PendingTtl;
            entity.UpdatedAt = now;
        }

        private void Close(WorldTask entity, string status, string? errorMessage)
        {
            entity.Status = status;
            entity.LeaseUntil = null;
            entity.UpdatedAt = _utcNow();
            if (status != WorldTaskStatuses.Completed)
            {
                entity.ErrorMessage = errorMessage;
            }
        }

        private bool IsPastExpiry(WorldTask entity, DateTime now) =>
            (entity.ExpiresAt ?? (entity.UpdatedAt ?? entity.CreatedAt) + _options.PendingTtl) <= now;

        private string ExpiredMessage() => $"Expired: nobody claimed it within {_options.PendingTtlMinutes} minutes.";

        /// <summary>Saves; a lost race (status or row version changed meanwhile) becomes a 409.</summary>
        private async Task SaveAsync(WorldTask entity, string? conflictMessage = null)
        {
            try
            {
                await _taskRepo.UpdateAsync(entity);
            }
            catch (DbUpdateConcurrencyException)
            {
                throw new WorldTaskConflictException(conflictMessage ?? $"Task {entity.Id} changed meanwhile; read it again.");
            }
        }

        private static string? NormalizeServerId(string? serverId) =>
            string.IsNullOrWhiteSpace(serverId) ? null : serverId.Trim();

        private static string? NormalizeUuid(string? uuid) =>
            string.IsNullOrWhiteSpace(uuid) ? null : uuid.Trim().ToLowerInvariant();

        private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];

        public async Task DeleteAsync(int id)
        {
            await _taskRepo.DeleteAsync(id);
        }
        /// <summary>
        /// Creates a world task from FormField context with guaranteed step/field information.
        /// </summary>
        public async Task<WorldTaskReadDto> CreateFromFormFieldAsync(
            int workflowSessionId,
            int stepNumber,
            string stepKey,
            string fieldName,
            string taskType,
            string? inputJson = null,
            int? assignedUserId = null)
        {
            var dto = new WorldTaskCreateDto
            {
                WorkflowSessionId = workflowSessionId,
                StepNumber = stepNumber,
                StepKey = stepKey,
                FieldName = fieldName,
                TaskType = taskType,
                InputJson = inputJson,
                AssignedUserId = assignedUserId
            };
            return await CreateAsync(dto);
        }

        private async Task<StepProgress> GetOrCreateStepAsync(int sessionId, string stepKey, int stepIndex)
        {
            var normalizedStepKey = stepKey?.Trim();
            if (string.IsNullOrWhiteSpace(normalizedStepKey)) throw new ArgumentException("stepKey is required", nameof(stepKey));

            var existing = await _workflowRepo.GetStepAsync(sessionId, normalizedStepKey);
            if (existing != null) return existing;

            var now = DateTime.UtcNow;
            var step = new StepProgress
            {
                WorkflowSessionId = sessionId,
                StepKey = normalizedStepKey,
                StepIndex = stepIndex,
                Status = "Pending",
                CreatedAt = now,
                UpdatedAt = now
            };

            try
            {
                await _workflowRepo.AddStepAsync(step);
                return step;
            }
            catch (DbUpdateException ex) when (IsDuplicateStepKeyException(ex))
            {
                return await _workflowRepo.GetStepAsync(sessionId, normalizedStepKey)
                    ?? throw new InvalidOperationException($"Step '{normalizedStepKey}' not found after duplicate key exception.");
            }
        }

        private static bool IsDuplicateStepKeyException(DbUpdateException ex) =>
            ex.InnerException is MySqlException { Number: 1062 };

        private static string? ExtractFieldNameFromPayload(string payloadJson)
        {
            if (string.IsNullOrWhiteSpace(payloadJson)) return null;

            // Try raw payload first, then a JSON-unescaped version in case the payload arrived double-encoded.
            foreach (var candidate in EnumeratePayloadCandidates(payloadJson))
            {
                var value = TryParseFieldName(candidate);
                if (!string.IsNullOrWhiteSpace(value)) return value;
            }

            return null;
        }

        /// <summary>
        /// Parses a GateBlockScan result and persists the scanned snapshots on the target door.
        /// Returns null on success, or an error message if the result should fail the task instead.
        /// </summary>
        private async Task<string?> TryApplyGateBlockScanResultAsync(WorldTask entity, string? outputJson)
        {
            if (string.IsNullOrWhiteSpace(outputJson))
                return "GateBlockScan task completed without an output payload.";

            GateBlockScanResultDto? result;
            try
            {
                result = JsonSerializer.Deserialize<GateBlockScanResultDto>(outputJson);
            }
            catch (JsonException ex)
            {
                return $"Could not parse GateBlockScan result: {ex.Message}";
            }

            if (result == null)
                return "GateBlockScan result payload was empty.";

            if (result.Status == GateBlockScanStatus.Failed)
                return result.ErrorMessage ?? "Gate block scan reported failure.";

            var gateDoorId = ExtractGateDoorId(entity.InputJson);
            if (gateDoorId is null || gateDoorId <= 0)
                return "GateBlockScan task InputJson did not contain a valid gateDoorId.";

            if (result.Snapshots.Count > 0)
            {
                try
                {
                    await _gateDoorService.ClearBlockSnapshotsAsync(gateDoorId.Value);
                    await _gateDoorService.AddBlockSnapshotsAsync(gateDoorId.Value, result.Snapshots);
                }
                catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException)
                {
                    return $"Could not persist gate block snapshots: {ex.Message}";
                }
            }

            return null;
        }

        /// <summary>
        /// Parses a GateOpenedBlockScan result and persists the scanned snapshots on the target
        /// door's OpenedBlockSnapshots. Mirrors TryApplyGateBlockScanResultAsync exactly, but
        /// targets the separate GateOpenedBlockSnapshot table - see
        /// docs/features/gate-structure-animation/ROTATION_GAP_FILL_DESIGN.md, Decision 6.
        /// Returns null on success, or an error message if the result should fail the task instead.
        /// </summary>
        private async Task<string?> TryApplyGateOpenedBlockScanResultAsync(WorldTask entity, string? outputJson)
        {
            if (string.IsNullOrWhiteSpace(outputJson))
                return "GateOpenedBlockScan task completed without an output payload.";

            GateOpenedBlockScanResultDto? result;
            try
            {
                result = JsonSerializer.Deserialize<GateOpenedBlockScanResultDto>(outputJson);
            }
            catch (JsonException ex)
            {
                return $"Could not parse GateOpenedBlockScan result: {ex.Message}";
            }

            if (result == null)
                return "GateOpenedBlockScan result payload was empty.";

            if (result.Status == GateBlockScanStatus.Failed)
                return result.ErrorMessage ?? "Gate opened-block scan reported failure.";

            var gateDoorId = ExtractGateDoorId(entity.InputJson);
            if (gateDoorId is null || gateDoorId <= 0)
                return "GateOpenedBlockScan task InputJson did not contain a valid gateDoorId.";

            if (result.Snapshots.Count > 0)
            {
                try
                {
                    await _gateDoorService.ClearOpenedBlockSnapshotsAsync(gateDoorId.Value);
                    await _gateDoorService.AddOpenedBlockSnapshotsAsync(gateDoorId.Value, result.Snapshots);
                }
                catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException)
                {
                    return $"Could not persist gate opened-block snapshots: {ex.Message}";
                }
            }

            return null;
        }

        // Reads "gateDoorId" from a scan task's InputJson (moved from "gateStructureId" by item
        // 5's multi-door support - a scan targets one door, not a whole structure).
        /// <summary>
        /// KNG-268: refuses a GateBlockScan/GateOpenedBlockScan the plugin can't run (a region door
        /// without its captured region, a flood-fill open scan, no anchor) with a reason the form shows.
        /// </summary>
        private async Task EnsureGateScanCanStartAsync(string taskType, string? inputJson)
        {
            var openScan = taskType == WorldTaskTypes.GateOpenedBlockScan;
            if (!openScan && taskType != WorldTaskTypes.GateBlockScan) return;

            var gateDoorId = ExtractGateDoorId(inputJson);
            if (gateDoorId is null || gateDoorId <= 0) return; // the plugin reports it

            var door = await _gateDoorService.GetByIdAsync(gateDoorId.Value);
            if (door == null) return;

            var problem = GateScanReadiness.Problem(door, openScan);
            if (problem != null) throw new GateScanNotReadyException(problem);
        }

        private static int? ExtractGateDoorId(string? inputJson)
        {
            if (string.IsNullOrWhiteSpace(inputJson)) return null;

            try
            {
                using var doc = JsonDocument.Parse(inputJson);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "gateDoorId", StringComparison.OrdinalIgnoreCase))
                    {
                        return prop.Value.TryGetInt32(out var value) ? value : null;
                    }
                }
            }
            catch (JsonException)
            {
                // Ignore malformed InputJson; caller treats this as "not found".
            }

            return null;
        }

        private static IEnumerable<string> EnumeratePayloadCandidates(string payload)
        {
            yield return payload;

            // If payload looks escaped (contains \" or starts/ends with quotes), attempt to unescape once.
            var looksEscaped = payload.Contains("\\\"") || (payload.StartsWith("\"") && payload.EndsWith("\""));
            if (looksEscaped)
            {
                string? unescaped = null;
                try
                {
                    unescaped = JsonSerializer.Deserialize<string>(payload);
                }
                catch (JsonException)
                {
                    // Ignore; the raw candidate may still work.
                }

                if (!string.IsNullOrWhiteSpace(unescaped)) yield return unescaped;
            }
        }

        private static string? TryParseFieldName(string json)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.ValueKind != JsonValueKind.Object) return null;

                foreach (var prop in doc.RootElement.EnumerateObject())
                {
                    if (string.Equals(prop.Name, "fieldName", StringComparison.OrdinalIgnoreCase))
                    {
                        var value = prop.Value.GetString();
                        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
                    }
                }
            }
            catch (JsonException)
            {
                // Ignore malformed payloads; caller will handle missing field names.
            }

            return null;
        }

        /// <summary>
        /// Validates task output against validation rules embedded in InputJson.
        /// This provides server-side validation as a second check after plugin validation.
        /// </summary>
        /// <param name="task">The WorldTask with InputJson containing validation context</param>
        /// <param name="outputJson">The captured output to validate</param>
        /// <returns>ValidationResult indicating success or failure</returns>
        private async Task<WorldTaskValidationResultDto> ValidateTaskOutputAsync(WorldTask task, string? outputJson)
        {
            // If no InputJson, no validation configured
            if (string.IsNullOrWhiteSpace(task.InputJson))
            {
                return new WorldTaskValidationResultDto { IsValid = true };
            }

            try
            {
                // Parse InputJson
                using var doc = JsonDocument.Parse(task.InputJson);
                var root = doc.RootElement;

                // Check if validationContext exists
                if (!root.TryGetProperty("validationContext", out var validationContextElement))
                {
                    // No validation context - validation passes
                    return new WorldTaskValidationResultDto { IsValid = true };
                }

                // Parse validation context
                var validationContext = JsonSerializer.Deserialize<WorldTaskValidationContextDto>(
                    validationContextElement.GetRawText()
                );

                if (validationContext == null || validationContext.ValidationRules.Count == 0)
                {
                    return new WorldTaskValidationResultDto { IsValid = true };
                }

                // NOTE: Actual validation logic would go here
                // For now, we trust the plugin validation and return success
                // In the future, this could call IWorldTaskValidationService to re-validate
                
                // Example validation call (when service is implemented):
                // foreach (var rule in validationContext.ValidationRules)
                // {
                //     var result = await _validationService.ValidateAsync(rule, outputJson, validationContext.FormContext);
                //     if (!result.IsValid)
                //     {
                //         return result;
                //     }
                // }

                return new WorldTaskValidationResultDto { IsValid = true };
            }
            catch (JsonException ex)
            {
                // Fail-open: If we can't parse validation context, allow completion
                // Log the error for debugging
                System.Diagnostics.Debug.WriteLine($"Failed to parse validation context for task {task.Id}: {ex.Message}");
                return new WorldTaskValidationResultDto { IsValid = true };
            }
            catch (Exception ex)
            {
                // Fail-open for unexpected errors
                System.Diagnostics.Debug.WriteLine($"Unexpected error validating task {task.Id}: {ex.Message}");
                return new WorldTaskValidationResultDto { IsValid = true };
            }
        }
    }
}
