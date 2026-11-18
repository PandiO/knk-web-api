using System;
using System.ComponentModel.DataAnnotations;

namespace knkwebapi_v2.Models;

/// <summary>
/// The 2D outline of a domain's WorldGuard region as the game server last reported it (overview map, O15, O28;
/// PandiO/knk-workspace#735). Written only by the plugin over the game hub (<c>ReportRegionOutlines</c>) at its startup
/// reconcile and whenever a domain region is created, redefined or renamed, so the public map keeps its outlines while
/// the game server is down. One row per region id and world. Read by the public map endpoints (#736).
/// See knk-workspace <c>docs/specs/in-game-maps/OVERVIEW_MAP_DESIGN.md</c> § 6.1.1.
/// </summary>
public class MapRegionOutline
{
    public int Id { get; set; }

    /// <summary>The WorldGuard region id, lower case (WorldGuard ids are case-insensitive); unique per world.</summary>
    [MaxLength(128)]
    public string WgRegionId { get; set; } = null!;

    /// <summary>The Minecraft world the region is in (a region id is only unique within its world, KNG-111).</summary>
    [MaxLength(64)]
    public string WorldName { get; set; } = null!;

    /// <summary>
    /// The outline as a JSON array of <c>[x, z]</c> block coordinates in order, at least three points. A cuboid region
    /// is its four corners; a polygon region its points. See <see cref="Services.Map.MapOutlinePoints"/>.
    /// </summary>
    public string PointsJson { get; set; } = "[]";

    /// <summary>Lowest block Y of the region.</summary>
    public int MinY { get; set; }

    /// <summary>Highest block Y of the region.</summary>
    public int MaxY { get; set; }

    /// <summary>
    /// The domain whose main region or sub-region this is, resolved by the API when the outline is stored (null when no
    /// domain names the region at that moment). A hint for readers: the domain's current <c>WgRegionId</c> stays the
    /// authority, because a domain can move to another region while no report is sent.
    /// </summary>
    public int? DomainId { get; set; }

    public Domain? Domain { get; set; }

    /// <summary>When the outline was last written (UTC).</summary>
    public DateTime UpdatedAt { get; set; }
}
