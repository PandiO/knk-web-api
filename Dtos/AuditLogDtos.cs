using System;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos;

/// <summary>
/// One row of the audit log (docs/specs/user-management/DESIGN.md §4). ActorUsername/
/// TargetUsername are resolved server-side for display — the log only stores ids, per
/// AuditLogEntry's own doc comment on why it avoids a hard FK to User.
/// </summary>
public class AuditLogEntryDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("timestamp")]
    public DateTime Timestamp { get; set; }

    [JsonPropertyName("actorUserId")]
    public int? ActorUserId { get; set; }

    [JsonPropertyName("actorUsername")]
    public string? ActorUsername { get; set; }

    /// <summary>Null on an entry about another subject (SubjectType/SubjectId), e.g. a permission group.</summary>
    [JsonPropertyName("targetUserId")]
    public int? TargetUserId { get; set; }

    [JsonPropertyName("targetUsername")]
    public string? TargetUsername { get; set; }

    /// <summary>Serialized as its PascalCase string name (System.Text.Json default) — see AuditAction's own doc comment.</summary>
    [JsonPropertyName("action")]
    public string Action { get; set; } = null!;

    [JsonPropertyName("details")]
    public string? Details { get; set; }

    /// <summary>What the entry is about when that is not one player (AuditSubjectTypes, e.g. "PermissionGroup");
    /// null on player entries (PandiO/knk-workspace#50).</summary>
    [JsonPropertyName("subjectType")]
    public string? SubjectType { get; set; }

    [JsonPropertyName("subjectId")]
    public int? SubjectId { get; set; }
}
