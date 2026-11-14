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

    /// <summary>
    /// WT2 (#181, decision Q8): how long a "Hand off to…" offer (Accept/Decline in game) stays open, and how long the API
    /// waits for any offer's answer before it counts as timed out (the web app then expands the task code).
    /// </summary>
    public int OfferTimeoutSeconds { get; set; } = 120;

    /// <summary>
    /// PandiO/knk-workspace#269: a released temp region is offered to the game server for removal only this long after the
    /// release (and after its task last changed), so a result in flight or a web app that still shows it has settled.
    /// </summary>
    public int TempRegionReleaseGraceMinutes { get; set; } = 5;

    /// <summary>
    /// #269: a release still unsettled after this many days is dropped (something kept using the region, or no game server
    /// answered); the plugin's 14-day temp-region cleanup has had its turn by then.
    /// </summary>
    public int TempRegionReleaseKeepDays { get; set; } = 30;

    public TimeSpan TempRegionReleaseGrace => TimeSpan.FromMinutes(Math.Max(0, TempRegionReleaseGraceMinutes));
    public TimeSpan TempRegionReleaseKeep => TimeSpan.FromDays(Math.Max(1, TempRegionReleaseKeepDays));

    public TimeSpan OfferTimeout => TimeSpan.FromSeconds(Math.Max(10, OfferTimeoutSeconds));

    public TimeSpan PendingTtl => TimeSpan.FromMinutes(Math.Max(1, PendingTtlMinutes));
    public TimeSpan Lease => TimeSpan.FromMinutes(Math.Max(1, LeaseMinutes));
    public TimeSpan SessionRetention => TimeSpan.FromDays(Math.Max(1, SessionRetentionDays));
}
