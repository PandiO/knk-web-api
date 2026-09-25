using System;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos;

/// <summary>
/// Request bodies for the user-management quick-action endpoints
/// (docs/specs/user-management/IMPLEMENTATION_PLAN.md Phase 2 §3) — slimmer than the generic
/// CRUD DTOs (UpsertUserPermissionGroupDto/PermissionGrantDto) since the target user id comes
/// from the route, not the body.
/// </summary>
public class AssignGroupRequestDto
{
    [JsonPropertyName("permissionGroupId")]
    public int PermissionGroupId { get; set; }

    /// <summary>Null = permanent. Must be in the future when set.</summary>
    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }
}

public class GrantNodeRequestDto
{
    [JsonPropertyName("node")]
    public string Node { get; set; } = null!;

    /// <summary>true = grant, false = explicit deny.</summary>
    [JsonPropertyName("value")]
    public bool Value { get; set; } = true;

    [JsonPropertyName("expiresAt")]
    public DateTime? ExpiresAt { get; set; }
}
