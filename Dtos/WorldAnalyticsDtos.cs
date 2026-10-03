using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos;

// World analytics contracts (KNG-34 link 7, knk-workspace docs/specs/player-statistics/
// IMPLEMENTATION_PLAN.md §3.4). Anonymous aggregates only: none of these shapes carries a user id,
// UUID or name.

/// <summary>Body of <c>POST api/world-analytics/batches</c>: everything the plugin aggregated in one flush window.</summary>
public class WorldAnalyticsBatchDto
{
    [JsonPropertyName("batchId")]
    public Guid BatchId { get; set; }

    [JsonPropertyName("serverName")]
    public string? ServerName { get; set; }

    /// <summary>
    /// Start of the window (UTC). Every row of the batch is stored on this instant's local day — the
    /// plugin never lets a window cross a local midnight.
    /// </summary>
    [JsonPropertyName("windowStart")]
    public DateTime WindowStart { get; set; }

    [JsonPropertyName("movementCells")]
    public List<WorldMovementCellDto>? MovementCells { get; set; }

    [JsonPropertyName("menuSteps")]
    public List<MenuFunnelStepDto>? MenuSteps { get; set; }

    [JsonPropertyName("domainInteractions")]
    public List<DomainInteractionDto>? DomainInteractions { get; set; }
}

public class WorldMovementCellDto
{
    [JsonPropertyName("world")]
    public string World { get; set; } = "";

    [JsonPropertyName("cellSize")]
    public int CellSize { get; set; }

    [JsonPropertyName("cellX")]
    public int CellX { get; set; }

    [JsonPropertyName("cellZ")]
    public int CellZ { get; set; }

    [JsonPropertyName("samples")]
    public int Samples { get; set; }
}

public class MenuFunnelStepDto
{
    [JsonPropertyName("menuKey")]
    public string MenuKey { get; set; } = "";

    /// <summary><c>opened</c>, <c>back</c>, <c>closed</c> or <c>action:&lt;actionTypeId&gt;</c>.</summary>
    [JsonPropertyName("step")]
    public string Step { get; set; } = "";

    /// <summary>succeeded, denied, failed or info (case-insensitive).</summary>
    [JsonPropertyName("outcome")]
    public string? Outcome { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }
}

/// <summary>
/// One domain's interactions in the window. The plugin knows WorldGuard region ids, not always
/// domain ids: <see cref="RegionId"/> is resolved to the domain by its <c>WgRegionId</c> when
/// <see cref="DomainId"/> is not given (L7-3). Regions that are no domain are refused.
/// </summary>
public class DomainInteractionDto
{
    [JsonPropertyName("domainId")]
    public int? DomainId { get; set; }

    [JsonPropertyName("regionId")]
    public string? RegionId { get; set; }

    /// <summary><c>enter</c>, <c>leave</c> or <c>discover</c>.</summary>
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "";

    [JsonPropertyName("count")]
    public int Count { get; set; }

    /// <summary>Distinct players of the local day so far (the plugin's running count, not this window's).</summary>
    [JsonPropertyName("uniquePlayers")]
    public int UniquePlayers { get; set; }
}

public class WorldAnalyticsBatchResultDto
{
    [JsonPropertyName("batchId")]
    public Guid BatchId { get; set; }

    /// <summary>True when the batch id was already ingested; nothing was applied again.</summary>
    [JsonPropertyName("duplicate")]
    public bool Duplicate { get; set; }

    [JsonPropertyName("day")]
    public DateOnly? Day { get; set; }

    [JsonPropertyName("accepted")]
    public int Accepted { get; set; }

    [JsonPropertyName("rejected")]
    public List<WorldAnalyticsRejectionDto> Rejected { get; set; } = new();
}

public class WorldAnalyticsRejectionDto
{
    /// <summary><c>movementCells</c>, <c>menuSteps</c> or <c>domainInteractions</c>.</summary>
    [JsonPropertyName("section")]
    public string Section { get; set; } = "";

    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("code")]
    public string Code { get; set; } = "";
}

// ------------------------------------------------------------------ owner reads

/// <summary>A world with movement samples in the range (heatmap picker).</summary>
public class HeatmapWorldDto
{
    [JsonPropertyName("world")]
    public string World { get; set; } = "";

    /// <summary>Cell sizes stored for the world (a read may ask for any multiple of one of them).</summary>
    [JsonPropertyName("cellSizes")]
    public List<int> CellSizes { get; set; } = new();

    [JsonPropertyName("samples")]
    public long Samples { get; set; }
}

/// <summary><c>GET api/world-analytics/heatmap</c>.</summary>
public class HeatmapDto
{
    [JsonPropertyName("world")]
    public string World { get; set; } = "";

    [JsonPropertyName("cellSize")]
    public int CellSize { get; set; }

    [JsonPropertyName("from")]
    public DateOnly From { get; set; }

    [JsonPropertyName("to")]
    public DateOnly To { get; set; }

    [JsonPropertyName("cells")]
    public List<HeatmapCellDto> Cells { get; set; } = new();

    [JsonPropertyName("maxSamples")]
    public long MaxSamples { get; set; }

    [JsonPropertyName("totalSamples")]
    public long TotalSamples { get; set; }

    /// <summary>More cells than WorldAnalytics:MaxHeatmapCells; the busiest were kept.</summary>
    [JsonPropertyName("truncated")]
    public bool Truncated { get; set; }
}

public class HeatmapCellDto
{
    /// <summary>Cell index: blocks [x × cellSize, (x + 1) × cellSize).</summary>
    [JsonPropertyName("x")]
    public int X { get; set; }

    [JsonPropertyName("z")]
    public int Z { get; set; }

    [JsonPropertyName("samples")]
    public long Samples { get; set; }
}

/// <summary><c>GET api/world-analytics/menu-funnels</c>.</summary>
public class MenuFunnelReportDto
{
    [JsonPropertyName("from")]
    public DateOnly From { get; set; }

    [JsonPropertyName("to")]
    public DateOnly To { get; set; }

    /// <summary>Busiest menu first.</summary>
    [JsonPropertyName("menus")]
    public List<MenuFunnelDto> Menus { get; set; } = new();
}

public class MenuFunnelDto
{
    [JsonPropertyName("menuKey")]
    public string MenuKey { get; set; } = "";

    [JsonPropertyName("opened")]
    public long Opened { get; set; }

    [JsonPropertyName("back")]
    public long Back { get; set; }

    [JsonPropertyName("closed")]
    public long Closed { get; set; }

    /// <summary>Every step × outcome, opened/back/closed first, then actions by count.</summary>
    [JsonPropertyName("steps")]
    public List<MenuFunnelStepCountDto> Steps { get; set; } = new();
}

public class MenuFunnelStepCountDto
{
    [JsonPropertyName("step")]
    public string Step { get; set; } = "";

    /// <summary>succeeded, denied, failed or info.</summary>
    [JsonPropertyName("outcome")]
    public string Outcome { get; set; } = "";

    [JsonPropertyName("count")]
    public long Count { get; set; }
}

/// <summary><c>GET api/world-analytics/domains</c>.</summary>
public class DomainInteractionReportDto
{
    [JsonPropertyName("from")]
    public DateOnly From { get; set; }

    [JsonPropertyName("to")]
    public DateOnly To { get; set; }

    [JsonPropertyName("kind")]
    public string? Kind { get; set; }

    /// <summary>Most entries first.</summary>
    [JsonPropertyName("domains")]
    public List<DomainInteractionSummaryDto> Domains { get; set; } = new();
}

public class DomainInteractionSummaryDto
{
    [JsonPropertyName("domainId")]
    public int DomainId { get; set; }

    /// <summary>Null when the domain no longer exists.</summary>
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("regionId")]
    public string? RegionId { get; set; }

    [JsonPropertyName("enter")]
    public long Enter { get; set; }

    [JsonPropertyName("leave")]
    public long Leave { get; set; }

    [JsonPropertyName("discover")]
    public long Discover { get; set; }

    /// <summary>Sum of each day's distinct visitors (entries) — "player-days", not distinct players over the range.</summary>
    [JsonPropertyName("visitorDays")]
    public long VisitorDays { get; set; }

    /// <summary>Most distinct visitors (entries) on one day of the range.</summary>
    [JsonPropertyName("peakDailyVisitors")]
    public int PeakDailyVisitors { get; set; }
}
