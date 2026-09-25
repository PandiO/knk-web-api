using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

// A spawnpoint of a SiegeTeam (docs/specs/siege-minigame/DESIGN.md §3.5). Owned by its team
// (cascade). SortOrder 0 is the team's default spawn (§6.5 / §6.6 fallback).
[FormConfigurableEntity("SiegeSpawnpoint")]
public class SiegeSpawnpoint
{
    public int Id { get; set; }

    [NavigationPair(nameof(SiegeTeam))]
    [RelatedEntityField(typeof(SiegeTeam))]
    public int SiegeTeamId { get; set; }
    [RelatedEntityField(typeof(SiegeTeam))]
    public SiegeTeam SiegeTeam { get; set; } = null!;

    public int SortOrder { get; set; }
    public string Name { get; set; } = null!;

    // World-bound, captured in-game. Restrict: the Location row survives the spawnpoint.
    [NavigationPair(nameof(Location))]
    [RelatedEntityField(typeof(Location))]
    public int LocationId { get; set; }
    [RelatedEntityField(typeof(Location))]
    public Location Location { get; set; } = null!;

    // v2 made it per-point; v1 hardcoded 4.
    public double SafeZoneRadius { get; set; } = 4;
}
