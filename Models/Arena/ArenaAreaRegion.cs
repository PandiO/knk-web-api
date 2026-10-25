using System;
using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

/// <summary>
/// One WorldGuard region of an <see cref="ArenaArea"/> (arena duels DESIGN.md §4.1, rev. 6). Drawn through a world task
/// (<c>tempregion_worldtask_&lt;n&gt;</c>) or adopted with "select", then renamed on save to
/// <c>arena_&lt;arenaId&gt;_&lt;kind&gt;_&lt;id&gt;</c> (see <c>IDomainRegionNameFinalizer.FinalizeArenaAreaRegionAsync</c>).
/// The area is the union of its regions. Removing a row leaves the region as an orphan the game server deletes after
/// the retention period.
/// </summary>
[FormConfigurableEntity("ArenaAreaRegion")]
public class ArenaAreaRegion
{
    public int Id { get; set; }

    [NavigationPair(nameof(ArenaArea))]
    [RelatedEntityField(typeof(ArenaArea))]
    public int ArenaAreaId { get; set; }
    [RelatedEntityField(typeof(ArenaArea))]
    public ArenaArea ArenaArea { get; set; } = null!;

    public string WgRegionId { get; set; } = null!;

    /// <summary>The region's world: always the arena's world (a WorldGuard parent can't be in another world).</summary>
    public string WorldName { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
