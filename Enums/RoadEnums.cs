using System;

namespace knkwebapi_v2.Enums;

// Road navigation enums (docs/specs/navigation/DESIGN.md §3.1-3.6, IMPLEMENTATION_PLAN.md Phase 1.1).
// Stored as strings (HasConversion<string>()) and serialized by name by the global
// JsonStringEnumConverter - except RoadEdgeFlags, a [Flags] set stored as an int (see below).

/// <summary>Drives the routing cost factor (plugin config class-cost) - DESIGN §3.1.</summary>
public enum RoadClass
{
    Main,
    Road,
    Path
}

/// <summary>What a material does in a road profile (DESIGN §5.1).</summary>
public enum RoadMaterialRole
{
    Surface,
    Edge,
    Accent,
    Overlay
}

/// <summary>DESIGN §3.5. Boundary nodes sit on a tile border and are stitched to the neighbour
/// tile's boundary nodes by the API (plan D7); Anchor nodes are admin-made split points. A Pruned
/// node is a tombstone: an admin removed the dead end that ended there (it has no edges); the
/// builder leaves that arm out of every later build until the tombstone is deleted (unprune).</summary>
public enum RoadNodeKind
{
    Junction,
    Endpoint,
    Boundary,
    Anchor,
    Pruned
}

/// <summary>Manual nodes survive rebuilds untouched (DESIGN §3.5).</summary>
public enum RoadNodeSource
{
    Detected,
    Manual
}

/// <summary>Detected by the builder, Recorded by an admin walk (DESIGN §5.10), or a Stitch edge
/// the API creates between two tiles' boundary nodes (plan D7). Recorded and Stitch edges are
/// never replaced by a tile upsert's detected edges.</summary>
public enum RoadEdgeSource
{
    Detected,
    Recorded,
    Stitch
}

/// <summary>Stale when the edge's tile is dirty; still routable (DESIGN §3.6).</summary>
public enum RoadEdgeStatus
{
    Ok,
    Stale
}

/// <summary>Where an edge's street label came from; Manual is never overwritten (DESIGN §5.11).</summary>
public enum RoadStreetSource
{
    Inferred,
    Manual,
    None
}

/// <summary>DESIGN §3.4.</summary>
public enum RoadSeedSource
{
    Admin,
    Survey
}

/// <summary>
/// Static admin flags on an edge (DESIGN §3.6). The one enum stored as an int: it is a set, and a
/// string column can't hold a combination without inventing a separator format. DTOs expose it as
/// a string array (["Oneway", "NoGps"]) so the plugin and web app never see the bit values.
/// </summary>
[Flags]
public enum RoadEdgeFlags
{
    None = 0,
    Oneway = 1,
    NoGps = 2,
    Closed = 4
}
