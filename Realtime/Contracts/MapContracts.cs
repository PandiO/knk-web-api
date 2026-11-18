namespace knkwebapi_v2.Realtime.Contracts;

/// <summary>
/// Overview map L1 (PandiO/knk-workspace#735, OVERVIEW_MAP_DESIGN § 6.2.6): the argument of the game hub's
/// <c>ReportRegionOutlines</c>. The plugin sends it (only with its <c>map.reporting.enabled</c> on) after its startup
/// reconcile with every domain region it found, and after a domain region is created, redefined or renamed with that
/// one region; a rename also names the old region in <see cref="Removed"/>.
/// </summary>
public sealed class RegionOutlineReportDto
{
    /// <summary>Outlines to store or replace (keyed by world and region id).</summary>
    public List<RegionOutlineDto>? Outlines { get; set; }

    /// <summary>Regions that no longer exist (the old name of a renamed region): their rows are deleted.</summary>
    public List<RegionOutlineRefDto>? Removed { get; set; }
}

/// <summary>One region's outline.</summary>
public sealed class RegionOutlineDto
{
    public string WgRegionId { get; set; } = string.Empty;

    public string WorldName { get; set; } = string.Empty;

    /// <summary>The outline's points in order (block x and z); at least three.</summary>
    public List<OutlinePointDto>? Points { get; set; }

    public int MinY { get; set; }

    public int MaxY { get; set; }
}

/// <summary>A point of an outline: block x and z.</summary>
public sealed class OutlinePointDto
{
    public int X { get; set; }

    public int Z { get; set; }
}

/// <summary>A region by world and id.</summary>
public sealed class RegionOutlineRefDto
{
    public string WgRegionId { get; set; } = string.Empty;

    public string WorldName { get; set; } = string.Empty;
}
