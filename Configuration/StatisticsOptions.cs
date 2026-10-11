namespace knkwebapi_v2.Configuration;

/// <summary>
/// Player statistics (KNG-34, knk-workspace docs/specs/player-statistics/IMPLEMENTATION_PLAN.md §4).
/// Section "Statistics" — not "Telemetry", which is the OpenTelemetry exporter's.
/// <see cref="Enabled"/> = false is the kill switch: ingestion answers 503 (the plugin keeps
/// spooling) and the projection and retention jobs do nothing; reads keep working.
/// </summary>
public class StatisticsOptions
{
    public const string SectionName = "Statistics";

    public bool Enabled { get; set; } = true;

    /// <summary>IANA zone of the day/week/month boundaries (D5, L1-14). Unknown → UTC.</summary>
    public string TimeZone { get; set; } = "Europe/Amsterdam";

    /// <summary>Most entries (all lists together) one plugin batch may carry.</summary>
    public int MaxBatchEntries { get; set; } = 2000;

    /// <summary>Entries older than this are rejected (TooOld) — spool replays stay inside it.</summary>
    public int LateEventToleranceDays { get; set; } = 7;

    /// <summary>Clock skew allowed for entries from the future before they're rejected (InFuture).</summary>
    public int FutureToleranceSeconds { get; set; } = 300;

    public int ProjectionIntervalSeconds { get; set; } = 30;

    /// <summary>
    /// Ledger legs younger than this are left for the next projection run, so a ledger transaction
    /// that got a lower id but committed after a higher one is not skipped by the cursor.
    /// </summary>
    public int ProjectionSafetyLagSeconds { get; set; } = 10;

    /// <summary>Open sessions without a heartbeat for this long are closed (EndReason Timeout).</summary>
    public int SessionTimeoutMinutes { get; set; } = 5;

    public int DailyRetentionDays { get; set; } = 730;

    public int SessionRetentionDays { get; set; } = 365;

    public int BatchRetentionDays { get; set; } = 30;

    public int KillPairRetentionDays { get; set; } = 62;
}
