using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// A junction, dead end, tile-border crossing or admin anchor on the road network
/// (docs/specs/navigation/DESIGN.md §3.5). Ids are stable across rebuilds: the builder sends the
/// matched existing id with each node (§5.7). Manual nodes survive rebuilds; Locked nodes keep
/// their position.
/// </summary>
public class RoadNode
{
    public int Id { get; set; }

    /// <summary>Standing position; unique per world.</summary>
    public string World { get; set; } = null!;
    public int X { get; set; }
    public int Y { get; set; }
    public int Z { get; set; }

    public int TileId { get; set; }
    public RoadTile Tile { get; set; } = null!;

    public RoadNodeKind Kind { get; set; } = RoadNodeKind.Junction;
    public RoadNodeSource Source { get; set; } = RoadNodeSource.Detected;

    /// <summary>Optional, max 100; a named node is a /navigate destination.</summary>
    public string? Name { get; set; }

    /// <summary>Connected component (smallest node id in it); recomputed after every build (§5.8).</summary>
    public int ComponentId { get; set; }

    /// <summary>Set when an admin edits the node; rebuilds keep it in place.</summary>
    public bool Locked { get; set; }

    /// <summary>Radius (1-32) of the designed plaza this node is the centre of, or null (DESIGN §5.6
    /// step 4, rev. 5). Only on Junction and Anchor nodes; the builder makes the whole footprint one
    /// junction on this node.</summary>
    public int? PlazaRadius { get; set; }
}
