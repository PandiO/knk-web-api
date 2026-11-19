using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>A territory holder kind (territory DESIGN § 2). Crown land is held by its kingdom.</summary>
public enum TerritoryHolderKind
{
    Town = 0,
    Kingdom = 1,
}

public enum TerritoryConflictStatus
{
    Open = 0,
    Resolved = 1,
}

/// <summary>How an admin resolved a conflict (territory DESIGN § 3: no automatic resolution in V1).</summary>
public enum TerritoryConflictResolution
{
    None = 0,
    /// <summary>The area was given to one of the two holders; the game server's cache gives it that holder.</summary>
    Awarded = 1,
    /// <summary>The admin changed the inputs (power, regions, ranges) so the lines no longer overlap.</summary>
    Redrawn = 2,
}

/// <summary>
/// A contested zone (territory DESIGN § 3, PandiO/knk-workspace#326): where two territories of equal rank overlap, the
/// overlap belongs to neither. Written by the game server's territory report (<c>PUT api/Territory/report</c>), which
/// computes the overlap from its chunk cache; admins resolve it by awarding the area to one holder or by redrawing the
/// lines. Holders are stored with their kind, so a deleted town leaves the conflict readable.
/// The pair is normalised: (A kind, A id) sorts before (B kind, B id).
/// </summary>
[FormConfigurableEntity("TerritoryConflict")]
public class TerritoryConflict
{
    public const int WorldMaxLength = 100;
    public const int NoteMaxLength = 1000;

    public int Id { get; set; }

    [MaxLength(WorldMaxLength)]
    public string World { get; set; } = null!;

    public TerritoryHolderKind HolderAKind { get; set; }
    public int HolderAId { get; set; }
    public TerritoryHolderKind HolderBKind { get; set; }
    public int HolderBId { get; set; }

    public TerritoryConflictStatus Status { get; set; } = TerritoryConflictStatus.Open;

    /// <summary>False once the last report no longer shows the overlap (the conflict stays open until an admin resolves it).</summary>
    public bool StillOverlapping { get; set; } = true;

    /// <summary>The contested chunks as a JSON array of <c>[x, z]</c> chunk coordinates.</summary>
    public string ChunksJson { get; set; } = "[]";

    public int ChunkCount { get; set; }
    public int MinChunkX { get; set; }
    public int MinChunkZ { get; set; }
    public int MaxChunkX { get; set; }
    public int MaxChunkZ { get; set; }

    public DateTime DetectedAt { get; set; } = DateTime.UtcNow;
    public DateTime LastReportedAt { get; set; } = DateTime.UtcNow;

    public TerritoryConflictResolution Resolution { get; set; } = TerritoryConflictResolution.None;
    public TerritoryHolderKind? AwardedHolderKind { get; set; }
    public int? AwardedHolderId { get; set; }
    public DateTime? ResolvedAt { get; set; }
    public int? ResolvedByUserId { get; set; }

    [MaxLength(NoteMaxLength)]
    public string? ResolutionNote { get; set; }

    /// <summary>Structures standing in the contested area (territory DESIGN § 4): they keep their town and are flagged.</summary>
    [RelatedEntityField(typeof(Structure))]
    public ICollection<Structure> FlaggedStructures { get; set; } = new List<Structure>();
}

/// <summary>Why a structure is flagged by the territory report (territory DESIGN § 4).</summary>
public enum StructureTerritoryFlag
{
    None = 0,
    /// <summary>Its land is outside its town's territory (the territory shrank, or another holder owns it).</summary>
    OutsideTerritory = 1,
    /// <summary>Its land is in a contested zone; <see cref="Structure.TerritoryConflictId"/> names the conflict.</summary>
    Contested = 2,
}
