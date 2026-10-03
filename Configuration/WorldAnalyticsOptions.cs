namespace knkwebapi_v2.Configuration;

/// <summary>
/// World analytics (KNG-34 link 7, knk-workspace docs/specs/player-statistics/
/// IMPLEMENTATION_PLAN.md §4, DESIGN.md D10/D11/§F.15). Anonymous aggregates only.
/// <see cref="Enabled"/> = false: batches answer 503 (the plugin drops them) and retention stops;
/// owner reads keep serving what is stored.
/// </summary>
public class WorldAnalyticsOptions
{
    public const string SectionName = "WorldAnalytics";

    public bool Enabled { get; set; } = true;

    /// <summary>Days of daily aggregates kept (DESIGN.md §F.15).</summary>
    public int RetentionDays { get; set; } = 180;

    /// <summary>Days a batch id is kept for de-duplication.</summary>
    public int BatchRetentionDays { get; set; } = 30;

    /// <summary>Most rows (cells + menu steps + domain rows) per batch.</summary>
    public int MaxBatchRows { get; set; } = 20000;

    /// <summary>A batch window older than this is refused (the plugin drops it).</summary>
    public int LateBatchToleranceDays { get; set; } = 7;

    /// <summary>Longest owner read range in days.</summary>
    public int MaxRangeDays { get; set; } = 92;

    /// <summary>Most cells a heatmap read returns (the busiest are kept, the answer says truncated).</summary>
    public int MaxHeatmapCells { get; set; } = 20000;
}
