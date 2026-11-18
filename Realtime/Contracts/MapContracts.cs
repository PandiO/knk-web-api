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

/// <summary>
/// Overview map L4 (PandiO/knk-workspace#738, OVERVIEW_MAP_DESIGN § 5 rule 6, § 6.2.6): the argument of the game hub's
/// <c>ReportMapPlayers</c>. The plugin sends it (only with its <c>map.reporting.enabled</c> on) every
/// <c>map.reporting.interval-ticks</c> with its online players; a large server splits it into several messages. The API
/// keeps each player's latest entry and drops it on <c>PlayerLeft</c> or after three missed intervals.
/// </summary>
public sealed class MapPlayersReportDto
{
    /// <summary>The plugin's report interval in milliseconds (the "three missed intervals" rule).</summary>
    public int IntervalMs { get; set; }

    public List<MapPlayerReportDto>? Players { get; set; }
}

/// <summary>One online player as reported. Never served as is: <c>GET api/Map/players</c> builds its own DTOs.</summary>
public sealed class MapPlayerReportDto
{
    public string Uuid { get; set; } = string.Empty;

    public string World { get; set; } = string.Empty;

    public double X { get; set; }

    public double Y { get; set; }

    public double Z { get; set; }

    public double Yaw { get; set; }

    public bool Afk { get; set; }

    /// <summary><c>none</c>, <c>staff</c> or <c>owner</c>; anything else counts as hidden.</summary>
    public string? Mode { get; set; }

    /// <summary>The short key of the running minigame the player plays in (<c>siege</c>), or null.</summary>
    public string? Minigame { get; set; }

    /// <summary>The API id of that match (a <c>SiegeMatch</c> for a siege), or null while the plugin does not know it.</summary>
    public long? MatchId { get; set; }
}
