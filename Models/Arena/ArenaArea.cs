using System;
using System.Collections.Generic;
using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>What an <see cref="ArenaArea"/> is for (arena duels DESIGN.md §4.1, rev. 6). Stored as a string.</summary>
public enum ArenaAreaKind
{
    /// <summary>A fighting area: one match at a time, entered only by its two duelists (DESIGN §8.2).</summary>
    BATTLEGROUND,
    /// <summary>The stands: free on foot; the spectate teleport lands at the arrival location (§9.3).</summary>
    SPECTATOR,
    /// <summary>Where duelists warm up (§5); duelists-only while a match is in warmup there (§8.2).</summary>
    LOBBY
}

/// <summary>
/// A battleground, spectator area or lobby of an Arena (arena duels DESIGN.md §4.1, rev. 6, developer 2026-10-24): the
/// arena's own sub-structure, a plain owned row and NOT a Domain, so it has no enter/leave messages, discovery,
/// navigation or entry/exit flags. Its WorldGuard regions (<see cref="Regions"/>) are drawn through world tasks; the
/// duel runtime enforces access over their union.
/// </summary>
[FormConfigurableEntity("ArenaArea")]
public class ArenaArea
{
    public int Id { get; set; }

    [NavigationPair(nameof(Arena))]
    [RelatedEntityField(typeof(Arena))]
    public int ArenaId { get; set; }
    [RelatedEntityField(typeof(Arena))]
    public Arena Arena { get; set; } = null!;

    public ArenaAreaKind Kind { get; set; } = ArenaAreaKind.BATTLEGROUND;

    /// <summary>Unique within the arena (e.g. "bg1", "stands", "lobby").</summary>
    public string Key { get; set; } = null!;

    /// <summary>Label for menus and the boss bar; null = <see cref="Key"/>.</summary>
    public string? Name { get; set; }

    /// <summary>Battleground allocation order; the first enabled spectator area is the spectate target.</summary>
    public int SortOrder { get; set; }

    /// <summary>Off = not used (never allocated, not spectated, not the lobby). A battleground needs both spawns and a
    /// region to be on.</summary>
    public bool IsEnabled { get; set; }

    // ===== BATTLEGROUND =====
    [NavigationPair(nameof(SpawnALocation))]
    [RelatedEntityField(typeof(Location))]
    public int? SpawnALocationId { get; set; }
    [RelatedEntityField(typeof(Location))]
    public Location? SpawnALocation { get; set; }

    [NavigationPair(nameof(SpawnBLocation))]
    [RelatedEntityField(typeof(Location))]
    public int? SpawnBLocationId { get; set; }
    [RelatedEntityField(typeof(Location))]
    public Location? SpawnBLocation { get; set; }

    // ===== SPECTATOR and LOBBY =====
    /// <summary>Where a teleport to this area lands: the lobby teleport (D6) or the spectate teleport (§9.3).</summary>
    [NavigationPair(nameof(ArrivalLocation))]
    [RelatedEntityField(typeof(Location))]
    public int? ArrivalLocationId { get; set; }
    [RelatedEntityField(typeof(Location))]
    public Location? ArrivalLocation { get; set; }

    // ===== SPECTATOR =====
    /// <summary>Minimum title for the spectate teleport (not for walking in); null = none.</summary>
    [NavigationPair(nameof(TeleportMinTitleBracket))]
    [RelatedEntityField(typeof(TitleBracket))]
    public int? TeleportMinTitleBracketId { get; set; }
    [RelatedEntityField(typeof(TitleBracket))]
    public TitleBracket? TeleportMinTitleBracket { get; set; }

    /// <summary>Gem price of the spectate teleport; null or 0 = free.</summary>
    public int? TeleportPriceGems { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [RelatedEntityField(typeof(ArenaAreaRegion))]
    public ICollection<ArenaAreaRegion> Regions { get; set; } = new List<ArenaAreaRegion>();
}
