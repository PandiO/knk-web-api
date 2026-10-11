using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos;

// ===== Location retention: orphan review (KNG-80) =====

/// <summary>One orphaned Location under review, with the snapshot taken when it was flagged.</summary>
public class LocationOrphanDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("locationId")]
    public int LocationId { get; set; }

    /// <summary>Open, Kept, Deleted or Resolved.</summary>
    [JsonPropertyName("status")]
    public string Status { get; set; } = "Open";

    [JsonPropertyName("flaggedAt")]
    public DateTime FlaggedAt { get; set; }

    [JsonPropertyName("flaggedByRunId")]
    public int? FlaggedByRunId { get; set; }

    [JsonPropertyName("lastSeenAt")]
    public DateTime LastSeenAt { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("world")]
    public string? World { get; set; }

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }

    [JsonPropertyName("yaw")]
    public float Yaw { get; set; }

    [JsonPropertyName("pitch")]
    public float Pitch { get; set; }

    /// <summary>The Location's creation time; null for Locations older than the column.</summary>
    [JsonPropertyName("locationCreatedAt")]
    public DateTime? LocationCreatedAt { get; set; }

    /// <summary>False once the Location row is gone (deleted here or elsewhere).</summary>
    [JsonPropertyName("locationExists")]
    public bool LocationExists { get; set; }

    [JsonPropertyName("decidedByUserId")]
    public int? DecidedByUserId { get; set; }

    [JsonPropertyName("decidedByUsername")]
    public string? DecidedByUsername { get; set; }

    [JsonPropertyName("decidedAt")]
    public DateTime? DecidedAt { get; set; }

    [JsonPropertyName("decisionNote")]
    public string? DecisionNote { get; set; }

    [JsonPropertyName("resolvedReason")]
    public string? ResolvedReason { get; set; }

    /// <summary>When this item re-flags a Location that was kept before: that earlier decision.</summary>
    [JsonPropertyName("previousDecision")]
    public LocationOrphanPreviousDecisionDto? PreviousDecision { get; set; }
}

/// <summary>The Keep decision an item re-flags (who, when, note).</summary>
public class LocationOrphanPreviousDecisionDto
{
    [JsonPropertyName("itemId")]
    public int ItemId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "Kept";

    [JsonPropertyName("decidedByUserId")]
    public int? DecidedByUserId { get; set; }

    [JsonPropertyName("decidedByUsername")]
    public string? DecidedByUsername { get; set; }

    [JsonPropertyName("decidedAt")]
    public DateTime? DecidedAt { get; set; }

    [JsonPropertyName("decisionNote")]
    public string? DecisionNote { get; set; }
}

/// <summary>A page of orphans plus the open/kept totals for the panel's badges.</summary>
public class LocationOrphanPageDto : PagedResultDto<LocationOrphanDto>
{
    [JsonPropertyName("openCount")]
    public int OpenCount { get; set; }

    [JsonPropertyName("keptCount")]
    public int KeptCount { get; set; }
}

/// <summary>Body of keep/delete: an optional note for the record.</summary>
public class LocationOrphanDecisionDto
{
    [JsonPropertyName("note")]
    public string? Note { get; set; }
}

/// <summary>What a delete did.</summary>
public class LocationOrphanDeleteResultDto
{
    /// <summary>Deleted, or NoLongerOrphan when the re-check found a relation or custom name (nothing deleted).</summary>
    [JsonPropertyName("outcome")]
    public string Outcome { get; set; } = "Deleted";

    [JsonPropertyName("message")]
    public string Message { get; set; } = "";

    [JsonPropertyName("item")]
    public LocationOrphanDto Item { get; set; } = null!;
}

/// <summary>One run of the orphan check.</summary>
public class LocationRetentionRunDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("trigger")]
    public string Trigger { get; set; } = "scheduled";

    [JsonPropertyName("triggeredByUserId")]
    public int? TriggeredByUserId { get; set; }

    [JsonPropertyName("triggeredByUsername")]
    public string? TriggeredByUsername { get; set; }

    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; }

    [JsonPropertyName("finishedAt")]
    public DateTime? FinishedAt { get; set; }

    [JsonPropertyName("succeeded")]
    public bool Succeeded { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }

    [JsonPropertyName("candidatesScanned")]
    public int CandidatesScanned { get; set; }

    [JsonPropertyName("orphansFound")]
    public int OrphansFound { get; set; }

    [JsonPropertyName("newOrphans")]
    public int NewOrphans { get; set; }

    [JsonPropertyName("alreadyKnown")]
    public int AlreadyKnown { get; set; }

    [JsonPropertyName("reflagged")]
    public int Reflagged { get; set; }

    [JsonPropertyName("resolved")]
    public int Resolved { get; set; }

    [JsonPropertyName("durationMs")]
    public long DurationMs { get; set; }

    [JsonPropertyName("digestQueuedAt")]
    public DateTime? DigestQueuedAt { get; set; }
}

/// <summary>Schedule and thresholds (GET and PUT).</summary>
public class LocationRetentionSettingsDto
{
    [JsonPropertyName("scheduleEnabled")]
    public bool ScheduleEnabled { get; set; } = true;

    /// <summary>Daily or Weekly.</summary>
    [JsonPropertyName("frequency")]
    public string Frequency { get; set; } = "Weekly";

    /// <summary>Monday … Sunday (weekly runs).</summary>
    [JsonPropertyName("runDayOfWeek")]
    public string RunDayOfWeek { get; set; } = "Sunday";

    /// <summary>HH:mm in the server's time zone.</summary>
    [JsonPropertyName("runAtTime")]
    public string RunAtTime { get; set; } = "04:00";

    [JsonPropertyName("gracePeriodDays")]
    public int GracePeriodDays { get; set; } = 7;

    [JsonPropertyName("keptRecheckMonths")]
    public int KeptRecheckMonths { get; set; } = 6;

    /// <summary>Read only: the time zone runAtTime is in.</summary>
    [JsonPropertyName("timeZone")]
    public string? TimeZone { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTime? UpdatedAt { get; set; }

    [JsonPropertyName("updatedByUsername")]
    public string? UpdatedByUsername { get; set; }
}

/// <summary>The panel header: settings, the last run, the next scheduled one, and what counts as a reference.</summary>
public class LocationRetentionStatusDto
{
    [JsonPropertyName("settings")]
    public LocationRetentionSettingsDto Settings { get; set; } = new();

    [JsonPropertyName("lastRun")]
    public LocationRetentionRunDto? LastRun { get; set; }

    [JsonPropertyName("nextScheduledRunAt")]
    public DateTime? NextScheduledRunAt { get; set; }

    [JsonPropertyName("running")]
    public bool Running { get; set; }

    /// <summary>Every FK to Location the check anti-joins, e.g. "GateDoor.AnchorPointId".</summary>
    [JsonPropertyName("relations")]
    public List<string> Relations { get; set; } = new();

    /// <summary>Non-FK places also checked (JSON), e.g. "Game settings", "Unfinished form drafts".</summary>
    [JsonPropertyName("otherReferenceSources")]
    public List<string> OtherReferenceSources { get; set; } = new();
}

/// <summary>Where /knk location tp sends a staff member.</summary>
public class LocationTeleportTargetDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("world")]
    public string? World { get; set; }

    [JsonPropertyName("x")]
    public double X { get; set; }

    [JsonPropertyName("y")]
    public double Y { get; set; }

    [JsonPropertyName("z")]
    public double Z { get; set; }

    [JsonPropertyName("yaw")]
    public float Yaw { get; set; }

    [JsonPropertyName("pitch")]
    public float Pitch { get; set; }
}
