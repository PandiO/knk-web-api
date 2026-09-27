using System;
using System.Collections.Generic;

namespace knkwebapi_v2.Models;

/// <summary>
/// A 512x512-block column of the world, the unit the plugin builds and downloads
/// (docs/specs/navigation/DESIGN.md §3.3). Tile = floor(x / 512), floor(z / 512); covers all
/// heights. <see cref="Version"/> is the ETag of the tile's graph download (plan D4): +1 per
/// upsert, and +1 when a neighbour's upsert replaces a stitch edge this tile owned (plan D7).
/// </summary>
public class RoadTile
{
    public const int Size = 512;

    public int Id { get; set; }

    public string World { get; set; } = null!;
    public int TileX { get; set; }
    public int TileZ { get; set; }

    /// <summary>ETag of the graph download; bumped by every change to the tile's nodes or edges.</summary>
    public int Version { get; set; }

    /// <summary>Last successful build; null for a tile that only exists because it was marked dirty
    /// or holds manual nodes.</summary>
    public DateTime? BuiltAt { get; set; }

    /// <summary>A builder change forces rebuilds.</summary>
    public int BuilderVersion { get; set; }

    /// <summary>Set when road blocks changed in the tile (DESIGN §5.9); cleared by the next upsert.</summary>
    public bool Dirty { get; set; }

    public int CellCount { get; set; }
    public int NodeCount { get; set; }
    public int EdgeCount { get; set; }

    /// <summary>Max road cells stacked in one column (tunnels, bridges).</summary>
    public int LevelCount { get; set; }

    /// <summary>JSON string[]: cell cap hit, suspected leak, coverage gaps, unmatched seeds,
    /// street-label conflicts.</summary>
    public string? WarningsJson { get; set; }

    public ICollection<RoadNode> Nodes { get; set; } = new List<RoadNode>();
    public ICollection<RoadEdge> Edges { get; set; } = new List<RoadEdge>();

    public static int TileCoordinate(int blockCoordinate) =>
        (int)Math.Floor(blockCoordinate / (double)Size);
}
