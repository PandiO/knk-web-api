namespace knkwebapi_v2.Configuration;

/// <summary>
/// Diagnostic telemetry (KNG-34 link 6, knk-workspace docs/specs/player-statistics/
/// IMPLEMENTATION_PLAN.md §4, DESIGN.md §F.12/§F.15). Not to be confused with
/// <see cref="TelemetryOptions"/> ("Telemetry"), the OpenTelemetry exporter settings.
/// <see cref="Enabled"/> = false: ingestion answers 503, the plugin config says disabled, nothing is
/// queued (API failures included) and retention stops.
/// </summary>
public class DiagnosticTelemetryOptions
{
    public const string SectionName = "DiagnosticTelemetry";

    public bool Enabled { get; set; } = true;

    /// <summary>Events waiting for the writer; when full, new events are dropped and counted.</summary>
    public int QueueCapacity { get; set; } = 10000;

    /// <summary>Most events per POST api/telemetry/events/batch.</summary>
    public int MaxBatchSize { get; set; } = 500;

    /// <summary>Seconds between writer flushes.</summary>
    public int WriteIntervalSeconds { get; set; } = 2;

    /// <summary>Most events per writer insert.</summary>
    public int WriteBatchSize { get; set; } = 1000;

    public int BaselineRetentionDays { get; set; } = 90;

    public int EnhancedRetentionDays { get; set; } = 14;

    /// <summary>Events older than this (by occurredAt) are rejected as TooOld.</summary>
    public int LateEventToleranceDays { get; set; } = 7;

    /// <summary>Events further in the future than this are rejected as InFuture.</summary>
    public int FutureToleranceSeconds { get; set; } = 300;

    /// <summary>Longest enhanced-target duration the owner may set.</summary>
    public int MaxEnhancedTargetHours { get; set; } = 168;
}
