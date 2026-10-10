using System;

namespace knkwebapi_v2.Models;

/// <summary>
/// The pending proposal of a Curated tile and its rejected list (docs/specs/navigation/
/// IMPLEMENTATION_PLAN.md §5.7, decisions D4/D5): what a rebuild would add, remove, change or move,
/// computed by the plugin, reviewed in game (and later in the web app). One row per tile. The item
/// format belongs to the plugin (knk-core <c>TileDiff</c>); the API stores it as JSON and keeps the
/// counts for listings. An upsert clears the pending items; the rejected list stays until deleted.
/// </summary>
public class RoadTileProposal
{
    public int Id { get; set; }

    public int TileId { get; set; }
    public RoadTile Tile { get; set; } = null!;

    /// <summary>The tile Version the proposal was computed against (informational: the plugin checks
    /// each item against the current graph when it is accepted).</summary>
    public int BaseVersion { get; set; }

    /// <summary>The builder version that made the proposal.</summary>
    public int BuilderVersion { get; set; }

    /// <summary>Who asked for the build (a player name), or null.</summary>
    public string? CreatedBy { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    /// <summary>The build's mask size and levels, and its warnings (JSON string[]): uploaded with the
    /// last accepted items, so the tile then counts as built with <see cref="BuilderVersion"/>.</summary>
    public int CellCount { get; set; }
    public int LevelCount { get; set; }
    public string WarningsJson { get; set; } = "[]";

    /// <summary>JSON array of pending items.</summary>
    public string ItemsJson { get; set; } = "[]";

    /// <summary>JSON array of rejected items: they only filter later proposals.</summary>
    public string RejectedJson { get; set; } = "[]";

    public int AddedCount { get; set; }
    public int RemovedCount { get; set; }
    public int ChangedCount { get; set; }
    public int MovedCount { get; set; }
    public int RejectedCount { get; set; }
}
