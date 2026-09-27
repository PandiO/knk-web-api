namespace knkwebapi_v2.Configuration;

/// <summary>
/// The currency monitor (docs/specs/currency-payments/DESIGN.md §3.9, IMPLEMENTATION_PLAN.md
/// Phase 5): how often it runs and the anomaly rule thresholds. Section "CurrencyMonitor" in
/// appsettings; every value has the design's default, so the section is optional.
/// </summary>
public class CurrencyMonitorOptions
{
    public const string SectionName = "CurrencyMonitor";

    /// <summary>Run the background monitor at all (the alerts API works either way).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>Wait after startup before the first cycle, so the API is up first.</summary>
    public int StartupDelaySeconds { get; set; } = 30;

    /// <summary>One cycle: R8/R9 findings collected in memory become alerts.</summary>
    public int CycleSeconds { get; set; } = 60;

    /// <summary>The ledger rules R3–R7 run every this many minutes.</summary>
    public int RuleIntervalMinutes { get; set; } = 5;

    /// <summary>R1/R2: the reconciler runs every this many minutes (hourly by design).</summary>
    public int ReconciliationIntervalMinutes { get; set; } = 60;

    /// <summary>R1 switches player transfers off for the mismatched currency (DESIGN.md §4 D8).</summary>
    public bool AutoDisableTransfersOnMismatch { get; set; } = true;

    /// <summary>Lowest severity also sent to online staff in-game (Low = every alert).</summary>
    public string InGameMinSeverity { get; set; } = "Low";

    // ===== R3 funnel =====
    public int FunnelMinNewSenders { get; set; } = 5;
    public int FunnelNewAccountDays { get; set; } = 7;
    public int FunnelWindowHours { get; set; } = 24;

    // ===== R4 ping-pong =====
    public int PingPongMinCycles { get; set; } = 3;
    public int PingPongWindowMinutes { get; set; } = 60;

    // ===== R5 velocity (coins) =====
    public long VelocityMinCoinsPerHour { get; set; } = 500_000;
    public int VelocityMeanMultiplier { get; set; } = 10;
    public int VelocityBaselineDays { get; set; } = 30;

    // ===== R6 staff adjustments =====
    public long AdminSingleCoinsThreshold { get; set; } = 1_000_000;
    public long AdminSingleGemsThreshold { get; set; } = 500;
    public int AdminMaxAdjustmentsPerHour { get; set; } = 10;

    // ===== R7 mint rate =====
    public int MintRateMultiplier { get; set; } = 3;
    public int MintRateBaselineDays { get; set; } = 7;

    /// <summary>A day's mint below this isn't flagged however it compares (noise floor).</summary>
    public long MintRateMinCoinsPerDay { get; set; } = 10_000;
    public long MintRateMinGemsPerDay { get; set; } = 100;

    // ===== R9 probing =====
    public int ProbingMaxDeniedPerHour { get; set; } = 20;
}
