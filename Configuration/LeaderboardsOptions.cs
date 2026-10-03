namespace knkwebapi_v2.Configuration;

/// <summary>
/// Leaderboards (KNG-34, knk-workspace docs/specs/player-statistics/IMPLEMENTATION_PLAN.md §4,
/// DESIGN.md §F.11). <see cref="Enabled"/> = false stops the snapshot job (reads keep serving the
/// last snapshots, or empty boards).
/// </summary>
public class LeaderboardsOptions
{
    public const string SectionName = "Leaderboards";

    public bool Enabled { get; set; } = true;

    /// <summary>Seconds between snapshot refreshes (minimum 30).</summary>
    public int RefreshSeconds { get; set; } = 300;

    /// <summary>Most ranked players stored per snapshot.</summary>
    public int MaxEntriesPerBoard { get; set; } = 5000;

    /// <summary>
    /// PvP kills of one victim by one killer per local day that count toward pvp_kills boards
    /// (personal statistics count all). Applied when kills are ingested (internal metric
    /// pvp_kills.ranked); a change applies to kills ingested afterwards. 0 = no cap.
    /// </summary>
    public int RepeatVictimDailyCap { get; set; } = 3;

    /// <summary>Days a superseded closed-period snapshot is kept.</summary>
    public int SnapshotRetentionDays { get; set; } = 400;
}
