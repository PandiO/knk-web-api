using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Dtos;

// Diagnostic telemetry contracts (KNG-34 link 6, knk-workspace docs/specs/player-statistics/
// IMPLEMENTATION_PLAN.md §3.3, DESIGN.md §F.12). Enums in responses serialise as their names.

/// <summary>
/// One event as the plugin sends it (§F.12 envelope). <c>level</c>/<c>outcome</c> are strings
/// (case-insensitive) so one bad event is rejected alone instead of failing the whole batch.
/// </summary>
public class TelemetryEventDto
{
    [JsonPropertyName("eventId")]
    public Guid EventId { get; set; }

    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; } = 1;

    [JsonPropertyName("occurredAt")]
    public DateTime OccurredAt { get; set; }

    [JsonPropertyName("serverName")]
    public string? ServerName { get; set; }

    [JsonPropertyName("serverSeq")]
    public long ServerSeq { get; set; }

    [JsonPropertyName("appVersion")]
    public string? AppVersion { get; set; }

    /// <summary>"baseline" or "enhanced" — must match the catalogue.</summary>
    [JsonPropertyName("level")]
    public string? Level { get; set; }

    [JsonPropertyName("userId")]
    public int? UserId { get; set; }

    [JsonPropertyName("sessionKey")]
    public Guid? SessionKey { get; set; }

    [JsonPropertyName("testRunId")]
    public int? TestRunId { get; set; }

    [JsonPropertyName("matchId")]
    public int? MatchId { get; set; }

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }

    [JsonPropertyName("feature")]
    public string? Feature { get; set; }

    [JsonPropertyName("action")]
    public string? Action { get; set; }

    /// <summary>"succeeded", "denied", "failed" or "info".</summary>
    [JsonPropertyName("outcome")]
    public string? Outcome { get; set; }

    [JsonPropertyName("reasonCode")]
    public string? ReasonCode { get; set; }

    [JsonPropertyName("objectType")]
    public string? ObjectType { get; set; }

    [JsonPropertyName("objectId")]
    public string? ObjectId { get; set; }

    /// <summary>Scalar values only; keys not allowlisted for the name are dropped.</summary>
    [JsonPropertyName("payload")]
    public Dictionary<string, JsonElement>? Payload { get; set; }
}

public class TelemetryRejectionDto
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("eventId")]
    public Guid? EventId { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; } = "";
}

/// <summary>Result of <c>POST api/telemetry/events/batch</c>.</summary>
public class TelemetryBatchResultDto
{
    /// <summary>Queued for writing.</summary>
    [JsonPropertyName("accepted")]
    public int Accepted { get; set; }

    /// <summary>Already stored, or repeated within the batch.</summary>
    [JsonPropertyName("duplicates")]
    public int Duplicates { get; set; }

    /// <summary>Valid but dropped because the write queue was full (never retried).</summary>
    [JsonPropertyName("dropped")]
    public int Dropped { get; set; }

    [JsonPropertyName("rejected")]
    public List<TelemetryRejectionDto> Rejected { get; set; } = new();
}

/// <summary>A stored event as the owner sees it.</summary>
public class TelemetryEventViewDto
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("eventId")]
    public Guid EventId { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("schemaVersion")]
    public int SchemaVersion { get; set; }

    [JsonPropertyName("level")]
    public TelemetryLevel Level { get; set; }

    [JsonPropertyName("source")]
    public TelemetrySource Source { get; set; }

    [JsonPropertyName("occurredAt")]
    public DateTime OccurredAt { get; set; }

    [JsonPropertyName("receivedAt")]
    public DateTime ReceivedAt { get; set; }

    [JsonPropertyName("serverName")]
    public string ServerName { get; set; } = "";

    [JsonPropertyName("serverSeq")]
    public long ServerSeq { get; set; }

    [JsonPropertyName("appVersion")]
    public string AppVersion { get; set; } = "";

    [JsonPropertyName("userId")]
    public int? UserId { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("sessionKey")]
    public Guid? SessionKey { get; set; }

    [JsonPropertyName("testRunId")]
    public int? TestRunId { get; set; }

    [JsonPropertyName("matchId")]
    public int? MatchId { get; set; }

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }

    [JsonPropertyName("feature")]
    public string Feature { get; set; } = "";

    [JsonPropertyName("action")]
    public string Action { get; set; } = "";

    [JsonPropertyName("outcome")]
    public TelemetryOutcome Outcome { get; set; }

    [JsonPropertyName("reasonCode")]
    public string? ReasonCode { get; set; }

    [JsonPropertyName("objectType")]
    public string? ObjectType { get; set; }

    [JsonPropertyName("objectId")]
    public string? ObjectId { get; set; }

    [JsonPropertyName("payload")]
    public Dictionary<string, JsonElement>? Payload { get; set; }
}

/// <summary>
/// One page of <c>GET api/telemetry/events</c>, newest first. <c>nextBefore</c> is an opaque
/// cursor for the next (older) page; null on the last page.
/// </summary>
public class TelemetryEventPageDto
{
    [JsonPropertyName("items")]
    public List<TelemetryEventViewDto> Items { get; set; } = new();

    [JsonPropertyName("nextBefore")]
    public string? NextBefore { get; set; }
}

/// <summary>Links from an event to authoritative records, resolved at read time.</summary>
public class TelemetryEventLinksDto
{
    /// <summary>Ledger transactions carrying the event's correlation id.</summary>
    [JsonPropertyName("ledgerTransactionPublicIds")]
    public List<string> LedgerTransactionPublicIds { get; set; } = new();

    [JsonPropertyName("siegeMatchId")]
    public int? SiegeMatchId { get; set; }
}

/// <summary><c>GET api/telemetry/events/{eventId}</c>.</summary>
public class TelemetryEventDetailDto
{
    [JsonPropertyName("event")]
    public TelemetryEventViewDto Event { get; set; } = new();

    /// <summary>Other events with the same correlation id (≤ 100, in order).</summary>
    [JsonPropertyName("related")]
    public List<TelemetryEventViewDto> Related { get; set; } = new();

    [JsonPropertyName("links")]
    public TelemetryEventLinksDto Links { get; set; } = new();
}

/// <summary>A ledger transaction leg of the timeline's player (amounts read from the ledger, not copied).</summary>
public class TelemetryLedgerItemDto
{
    [JsonPropertyName("publicId")]
    public string PublicId { get; set; } = "";

    [JsonPropertyName("reasonCode")]
    public string ReasonCode { get; set; } = "";

    [JsonPropertyName("correlationId")]
    public string? CorrelationId { get; set; }

    [JsonPropertyName("currency")]
    public string Currency { get; set; } = "";

    /// <summary>Signed change of the player's balance.</summary>
    [JsonPropertyName("delta")]
    public long Delta { get; set; }
}

/// <summary>A Siege participation of the timeline's player.</summary>
public class TelemetrySiegeItemDto
{
    [JsonPropertyName("matchId")]
    public int MatchId { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "";

    /// <summary>"joined", "left" or "ended".</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("teamId")]
    public int? TeamId { get; set; }

    [JsonPropertyName("kills")]
    public int Kills { get; set; }

    [JsonPropertyName("deaths")]
    public int Deaths { get; set; }

    [JsonPropertyName("captures")]
    public int Captures { get; set; }
}

/// <summary>One line of the merged timeline; exactly one of event/ledger/siege is set.</summary>
public class TelemetryTimelineItemDto
{
    /// <summary>"event", "ledger" or "siege".</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("at")]
    public DateTime At { get; set; }

    [JsonPropertyName("event")]
    public TelemetryEventViewDto? Event { get; set; }

    [JsonPropertyName("ledger")]
    public TelemetryLedgerItemDto? Ledger { get; set; }

    [JsonPropertyName("siege")]
    public TelemetrySiegeItemDto? Siege { get; set; }
}

/// <summary><c>GET api/telemetry/timeline/{userId}</c>, oldest first.</summary>
public class TelemetryTimelineDto
{
    [JsonPropertyName("userId")]
    public int UserId { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("from")]
    public DateTime From { get; set; }

    [JsonPropertyName("to")]
    public DateTime To { get; set; }

    /// <summary>True when a source had more rows than the limit (narrow the window).</summary>
    [JsonPropertyName("truncated")]
    public bool Truncated { get; set; }

    [JsonPropertyName("items")]
    public List<TelemetryTimelineItemDto> Items { get; set; } = new();
}

public class TelemetryTestRunDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("description")]
    public string? Description { get; set; }

    [JsonPropertyName("startedAt")]
    public DateTime StartedAt { get; set; }

    [JsonPropertyName("endedAt")]
    public DateTime? EndedAt { get; set; }

    [JsonPropertyName("createdByUserId")]
    public int CreatedByUserId { get; set; }
}

public class TelemetryTestRunCreateDto
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

public class EnhancedTargetDto
{
    [JsonPropertyName("id")]
    public int Id { get; set; }

    [JsonPropertyName("userId")]
    public int? UserId { get; set; }

    [JsonPropertyName("username")]
    public string? Username { get; set; }

    [JsonPropertyName("testRunId")]
    public int? TestRunId { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }

    [JsonPropertyName("createdByUserId")]
    public int CreatedByUserId { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTime CreatedAt { get; set; }
}

/// <summary>Exactly one of userId/testRunId; expiresAt in the future (≤ MaxEnhancedTargetHours).</summary>
public class EnhancedTargetCreateDto
{
    [JsonPropertyName("userId")]
    public int? UserId { get; set; }

    [JsonPropertyName("testRunId")]
    public int? TestRunId { get; set; }

    [JsonPropertyName("expiresAt")]
    public DateTime ExpiresAt { get; set; }
}

/// <summary><c>GET api/telemetry/config</c> — what the plugin should emit.</summary>
public class TelemetryClientConfigDto
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("enhancedUserIds")]
    public List<int> EnhancedUserIds { get; set; } = new();

    /// <summary>Active test runs, newest first; the plugin stamps the first on its events.</summary>
    [JsonPropertyName("activeTestRunIds")]
    public List<int> ActiveTestRunIds { get; set; } = new();

    /// <summary>Active test runs with an enhanced target: enhanced events for everyone online.</summary>
    [JsonPropertyName("enhancedTestRunIds")]
    public List<int> EnhancedTestRunIds { get; set; } = new();

    [JsonPropertyName("baselineEventNames")]
    public List<string> BaselineEventNames { get; set; } = new();

    [JsonPropertyName("enhancedEventNames")]
    public List<string> EnhancedEventNames { get; set; } = new();
}

/// <summary><c>GET api/telemetry/health</c>.</summary>
public class TelemetryHealthDto
{
    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }

    [JsonPropertyName("queueDepth")]
    public int QueueDepth { get; set; }

    [JsonPropertyName("queueCapacity")]
    public int QueueCapacity { get; set; }

    [JsonPropertyName("droppedSinceStart")]
    public long DroppedSinceStart { get; set; }

    [JsonPropertyName("lastWriteAt")]
    public DateTime? LastWriteAt { get; set; }

    [JsonPropertyName("eventsLast24h")]
    public int EventsLast24h { get; set; }
}

/// <summary><c>POST api/statistics/rebuild</c> body.</summary>
public class StatisticsRebuildRequestDto
{
    /// <summary>"ledger", "siege" or "all".</summary>
    [JsonPropertyName("projection")]
    public string? Projection { get; set; }

    /// <summary>One user, or everyone when null.</summary>
    [JsonPropertyName("userId")]
    public int? UserId { get; set; }
}

public class StatisticsRebuildResultDto
{
    [JsonPropertyName("projection")]
    public string Projection { get; set; } = "";

    [JsonPropertyName("userId")]
    public int? UserId { get; set; }

    /// <summary>Source records re-projected.</summary>
    [JsonPropertyName("reprojected")]
    public int Reprojected { get; set; }
}
