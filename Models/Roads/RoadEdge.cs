using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// A stretch of road between two nodes (docs/specs/navigation/DESIGN.md §3.6): the centreline
/// polyline, its length and width, the profile that classifies it, the street it belongs to, the
/// gate doors and domains (plus WorldGuard region ids, plan D11) it passes through, and the admin
/// tuning that survives rebuilds. <c>FromNodeId &lt; ToNodeId</c>; the pair is unique.
/// </summary>
public class RoadEdge
{
    public int Id { get; set; }

    public int FromNodeId { get; set; }
    public RoadNode FromNode { get; set; } = null!;

    public int ToNodeId { get; set; }
    public RoadNode ToNode { get; set; } = null!;

    /// <summary>The tile that owns the edge: the one whose build produced it, or for a stitch edge
    /// the tile upserted last (plan D7).</summary>
    public int TileId { get; set; }
    public RoadTile Tile { get; set; } = null!;

    /// <summary>Denormalised from the tile for per-world queries; indexed with the bbox.</summary>
    public string World { get; set; } = null!;

    /// <summary>JSON [[x,y,z],...] RDP-simplified 3D polyline of standing positions, From to To.</summary>
    public string GeometryJson { get; set; } = "[]";

    /// <summary>Walked 3D length of the unsimplified centreline.</summary>
    public double Length { get; set; }

    public int MinX { get; set; }
    public int MinY { get; set; }
    public int MinZ { get; set; }
    public int MaxX { get; set; }
    public int MaxY { get; set; }
    public int MaxZ { get; set; }

    /// <summary>From the distance transform.</summary>
    public double AvgWidth { get; set; }

    /// <summary>Best-matching profile (§5.6); gives the road class. SetNull.</summary>
    public int? ProfileId { get; set; }
    public RoadProfile? Profile { get; set; }

    /// <summary>SetNull when the street is deleted.</summary>
    public int? StreetId { get; set; }
    public Street? Street { get; set; }

    public RoadStreetSource StreetSource { get; set; } = RoadStreetSource.None;

    /// <summary>Admin tuning; later the terrain/ambush-risk hook.</summary>
    public double CostMultiplier { get; set; } = 1.0;

    /// <summary>Static admin flags, stored as an int (the set of Oneway/NoGps/Closed).</summary>
    public RoadEdgeFlags Flags { get; set; } = RoadEdgeFlags.None;

    /// <summary>JSON int[]: gate doors whose closed-state blocks sit on this edge (§5.2).</summary>
    public string GateDoorIdsJson { get; set; } = "[]";

    /// <summary>JSON int[]: domains whose regions the edge passes through, in order.</summary>
    public string DomainIdsJson { get; set; } = "[]";

    /// <summary>JSON string[]: WorldGuard region ids the edge passes through, in order (plan D11).</summary>
    public string RegionIdsJson { get; set; } = "[]";

    public RoadEdgeSource Source { get; set; } = RoadEdgeSource.Detected;
    public RoadEdgeStatus Status { get; set; } = RoadEdgeStatus.Ok;
}
