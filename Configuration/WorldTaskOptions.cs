namespace knkwebapi_v2.Configuration;

/// <summary>
/// PandiO/knk-workspace#180 (DESIGN §8.2, developer decisions Q10/Q18): world-task lifetimes and the sweep, section
/// "WorldTasks" in appsettings. Every value has a default, so the section is optional.
/// </summary>
public class WorldTaskOptions
{
    public const string SectionName = "WorldTasks";

    /// <summary>A Pending task nobody claimed within this time is Expired (counted from create or release).</summary>
    public int PendingTtlMinutes { get; set; } = 30;

    /// <summary>A claim (InProgress) lasts this long after the claim or the last heartbeat; then the task is released.</summary>
    public int LeaseMinutes { get; set; } = 3;

    /// <summary>Run the sweep at all.</summary>
    public bool SweepEnabled { get; set; } = true;

    /// <summary>How often the sweep runs.</summary>
    public int SweepIntervalSeconds { get; set; } = 30;

    /// <summary>A workflow session with no draft and no activity for this long is removed with its open tasks.</summary>
    public int SessionRetentionDays { get; set; } = 7;

    /// <summary>Rows handled per sweep step and pass.</summary>
    public int SweepBatchSize { get; set; } = 200;

    public TimeSpan PendingTtl => TimeSpan.FromMinutes(Math.Max(1, PendingTtlMinutes));
    public TimeSpan Lease => TimeSpan.FromMinutes(Math.Max(1, LeaseMinutes));
    public TimeSpan SessionRetention => TimeSpan.FromDays(Math.Max(1, SessionRetentionDays));
}
