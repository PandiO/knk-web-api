using System;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos;

// WorkflowSession DTOs
public class WorkflowSessionCreateDto
{
    [JsonPropertyName("userId")] public int UserId { get; set; }
    [JsonPropertyName("formConfigurationId")] public int? FormConfigurationId { get; set; }
    [JsonPropertyName("entityTypeName")] public string? EntityTypeName { get; set; }
    [JsonPropertyName("entityId")] public int? EntityId { get; set; }
}

public class WorkflowSessionUpdateDto
{
    [JsonPropertyName("currentStepIndex")] public int? CurrentStepIndex { get; set; }
    [JsonPropertyName("status")] public string? Status { get; set; }
}

public class WorkflowSessionReadDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("sessionGuid")] public Guid SessionGuid { get; set; }
    [JsonPropertyName("userId")] public int UserId { get; set; }
    [JsonPropertyName("formConfigurationId")] public int? FormConfigurationId { get; set; }
    [JsonPropertyName("entityTypeName")] public string? EntityTypeName { get; set; }
    [JsonPropertyName("entityId")] public int? EntityId { get; set; }
    [JsonPropertyName("currentStepIndex")] public int CurrentStepIndex { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "InProgress";
    [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");
    [JsonPropertyName("updatedAt")] public string? UpdatedAt { get; set; }
    [JsonPropertyName("completedAt")] public string? CompletedAt { get; set; }
}

// StepProgress DTOs
public class StepProgressCreateDto
{
    [JsonPropertyName("workflowSessionId")] public int WorkflowSessionId { get; set; }
    [JsonPropertyName("stepKey")] public string StepKey { get; set; } = null!;
    [JsonPropertyName("stepIndex")] public int StepIndex { get; set; }
}

public class StepProgressUpdateDto
{
    [JsonPropertyName("status")] public string? Status { get; set; }
}

public class StepProgressReadDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("workflowSessionId")] public int WorkflowSessionId { get; set; }
    [JsonPropertyName("stepKey")] public string StepKey { get; set; } = null!;
    [JsonPropertyName("stepIndex")] public int StepIndex { get; set; }
    [JsonPropertyName("status")] public string Status { get; set; } = "Pending";
    [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");
    [JsonPropertyName("updatedAt")] public string? UpdatedAt { get; set; }
    [JsonPropertyName("completedAt")] public string? CompletedAt { get; set; }
}

// WorldTask DTOs
public class WorldTaskCreateDto
{
    [JsonPropertyName("workflowSessionId")] public int WorkflowSessionId { get; set; }
    [JsonPropertyName("stepNumber")] public int StepNumber { get; set; }
    [JsonPropertyName("stepKey")] public string StepKey { get; set; } = null!;
    [JsonPropertyName("fieldName")] public string FieldName { get; set; } = null!;
    [JsonPropertyName("taskType")] public string TaskType { get; set; } = null!;
    [JsonPropertyName("assignedUserId")] public int? AssignedUserId { get; set; }
    [JsonPropertyName("inputJson")] public string? InputJson { get; set; }
    // Legacy client payload support (maps to InputJson when provided)
    [JsonPropertyName("payloadJson")] public string? PayloadJson { get; set; }
    /// <summary>PandiO/knk-workspace#180: the draft whose field this task fills (DESIGN §8.7).</summary>
    [JsonPropertyName("formSubmissionProgressId")] public int? FormSubmissionProgressId { get; set; }
}

public class WorldTaskUpdateDto
{
    [JsonPropertyName("status")] public string? Status { get; set; }
    [JsonPropertyName("outputJson")] public string? OutputJson { get; set; }
    [JsonPropertyName("errorMessage")] public string? ErrorMessage { get; set; }
}

/// <summary>How a claim was made (PandiO/knk-workspace#180, DESIGN §8.2/§8.3).</summary>
public static class WorldTaskClaimModes
{
    /// <summary>The player typed the task code; the API checks it against the task.</summary>
    public const string Code = "code";
    /// <summary>The player claimed by numeric id; the game server checked <c>knk.tasks.any</c>.</summary>
    public const string Id = "id";
    /// <summary>The game server runs a headless task (gate scans, catalogue scan) itself, without a player.</summary>
    public const string Headless = "headless";
    /// <summary>#181: the task was offered to this player (TargetUserId).</summary>
    public const string Offer = "offer";
}

public class ClaimTaskDto
{
    [JsonPropertyName("claimedByServerId")] public string? ClaimedByServerId { get; set; }
    [JsonPropertyName("claimedByMinecraftUsername")] public string? ClaimedByMinecraftUsername { get; set; }
    /// <summary>#180: the claiming player's Minecraft UUID (null for a headless claim).</summary>
    [JsonPropertyName("claimedByUuid")] public string? ClaimedByUuid { get; set; }
    /// <summary>#180: the task code the player typed (checked on a code claim). "linkCode" is the older spelling.</summary>
    [JsonPropertyName("taskCode")] public string? TaskCode { get; set; }
    [JsonPropertyName("linkCode")] public string? LinkCode { get; set; }
    /// <summary>#180: <see cref="WorldTaskClaimModes"/>; missing = "code" when a code is sent, else "headless" without a uuid.</summary>
    [JsonPropertyName("claimMode")] public string? ClaimMode { get; set; }
    // The plugin's DTO before #180 sent these names; the API ignored them, so the claimer was never stored.
    [JsonPropertyName("serverId")] public string? ServerId { get; set; }
    [JsonPropertyName("minecraftUsername")] public string? MinecraftUsername { get; set; }

    public string? EffectiveServerId => string.IsNullOrWhiteSpace(ClaimedByServerId) ? ServerId : ClaimedByServerId;
    public string? EffectiveUsername => string.IsNullOrWhiteSpace(ClaimedByMinecraftUsername) ? MinecraftUsername : ClaimedByMinecraftUsername;
    public string? EffectiveCode => string.IsNullOrWhiteSpace(TaskCode) ? LinkCode : TaskCode;
}

public class CompleteTaskDto
{
    [JsonPropertyName("outputJson")] public string OutputJson { get; set; } = null!;
    /// <summary>#180: the completing game server; when sent it must be the one holding the claim.</summary>
    [JsonPropertyName("serverId")] public string? ServerId { get; set; }
}

public class FailTaskDto
{
    [JsonPropertyName("errorMessage")] public string ErrorMessage { get; set; } = null!;
    /// <summary>#180: the failing game server; when sent it must be the one holding the claim.</summary>
    [JsonPropertyName("serverId")] public string? ServerId { get; set; }
}

/// <summary>#180: POST api/WorldTasks/{id}/cancel (web: Capture again, Try again, leaving the form, "Cancel task").</summary>
public class CancelTaskDto
{
    [JsonPropertyName("reason")] public string? Reason { get; set; }
}

/// <summary>#180: POST api/WorldTasks/{id}/release (game server: in-game cancel, quit, restart, plugin disable).</summary>
public class ReleaseTaskDto
{
    [JsonPropertyName("serverId")] public string? ServerId { get; set; }
    [JsonPropertyName("reason")] public string? Reason { get; set; }
}

/// <summary>#180: POST api/WorldTasks/{id}/heartbeat (game server, every 60 s for each task it holds).</summary>
public class HeartbeatTaskDto
{
    [JsonPropertyName("serverId")] public string? ServerId { get; set; }
}

public class WorldTaskReadDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("workflowSessionId")] public int WorkflowSessionId { get; set; }
    [JsonPropertyName("stepNumber")] public int? StepNumber { get; set; }
    [JsonPropertyName("stepKey")] public string? StepKey { get; set; }
    [JsonPropertyName("fieldName")] public string? FieldName { get; set; }
    [JsonPropertyName("taskType")] public string TaskType { get; set; } = null!;
    [JsonPropertyName("status")] public string Status { get; set; } = "Pending";
    [JsonPropertyName("linkCode")] public string? LinkCode { get; set; }
    [JsonPropertyName("assignedUserId")] public int? AssignedUserId { get; set; }
    [JsonPropertyName("claimedByServerId")] public string? ClaimedByServerId { get; set; }
    [JsonPropertyName("claimedByMinecraftUsername")] public string? ClaimedByMinecraftUsername { get; set; }
    [JsonPropertyName("inputJson")] public string? InputJson { get; set; }
    [JsonPropertyName("outputJson")] public string? OutputJson { get; set; }
    [JsonPropertyName("errorMessage")] public string? ErrorMessage { get; set; }
    [JsonPropertyName("createdAt")] public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");
    [JsonPropertyName("claimedAt")] public string? ClaimedAt { get; set; }
    [JsonPropertyName("updatedAt")] public string? UpdatedAt { get; set; }
    [JsonPropertyName("completedAt")] public string? CompletedAt { get; set; }
    // PandiO/knk-workspace#180
    /// <summary>The form's entity type (the session's), shown in game next to the field.</summary>
    [JsonPropertyName("entityType")] public string? EntityType { get; set; }
    [JsonPropertyName("createdByUserId")] public int? CreatedByUserId { get; set; }
    [JsonPropertyName("claimedByUuid")] public string? ClaimedByUuid { get; set; }
    [JsonPropertyName("claimedByUserId")] public int? ClaimedByUserId { get; set; }
    [JsonPropertyName("expiresAt")] public string? ExpiresAt { get; set; }
    [JsonPropertyName("leaseUntil")] public string? LeaseUntil { get; set; }
    [JsonPropertyName("releaseCount")] public int ReleaseCount { get; set; }
    [JsonPropertyName("formSubmissionProgressId")] public int? FormSubmissionProgressId { get; set; }
    [JsonPropertyName("appliedAt")] public string? AppliedAt { get; set; }
    [JsonPropertyName("targetUserId")] public int? TargetUserId { get; set; }
    [JsonPropertyName("offerState")] public string OfferState { get; set; } = "None";
}
