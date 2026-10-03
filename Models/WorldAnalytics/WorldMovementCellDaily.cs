using System;

namespace knkwebapi_v2.Models;

/// <summary>
/// Anonymous movement samples of one heatmap cell on one local day (KNG-34 link 7, knk-workspace
/// docs/specs/player-statistics/IMPLEMENTATION_PLAN.md §1.4, DESIGN.md D10). The plugin samples
/// online, non-AFK players at most once per 10 s and posts per-cell counts; no user id, name or
/// position trail is ever stored, so the GDPR deletion has nothing to remove here.
/// </summary>
public class WorldMovementCellDaily
{
    /// <summary>Local day in Statistics:TimeZone (StatisticsPeriods.LocalDay of the batch window).</summary>
    public DateOnly Day { get; set; }

    /// <summary>Minecraft world name.</summary>
    public string World { get; set; } = null!;

    /// <summary>Cell edge in blocks (world-analytics.cell-size, default 16).</summary>
    public short CellSize { get; set; }

    /// <summary>floor(blockX / CellSize).</summary>
    public int CellX { get; set; }

    /// <summary>floor(blockZ / CellSize).</summary>
    public int CellZ { get; set; }

    public int Samples { get; set; }
}
