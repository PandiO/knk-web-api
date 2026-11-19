using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// A reserved place for a siege weapon in a scenario (knk-workspace docs/specs/siege-minigame/SIEGE_WEAPONS.md § 4,
// § 9.3; W3-W5, W30, W44, W70). Owned by its scenario (cascade) and by one of the scenario's teams (cascade: a spot
// without its team has no owner). Bounds, facing and size class are captured by a world task (phase 2,
// PandiO/knk-workspace#783); until then they can be typed in the form.
[FormConfigurableEntity("SiegeWeaponSpot")]
public class SiegeWeaponSpot
{
    public int Id { get; set; }

    [NavigationPair(nameof(SiegeScenario))]
    [RelatedEntityField(typeof(SiegeScenario))]
    public int SiegeScenarioId { get; set; }
    [RelatedEntityField(typeof(SiegeScenario))]
    public SiegeScenario SiegeScenario { get; set; } = null!;

    [NavigationPair(nameof(SiegeTeam))]
    [RelatedEntityField(typeof(SiegeTeam))]
    public int SiegeTeamId { get; set; }
    [RelatedEntityField(typeof(SiegeTeam))]
    public SiegeTeam SiegeTeam { get; set; } = null!;

    public int SortOrder { get; set; }
    public string Name { get; set; } = null!;

    // Bounds: the min/max corner of the selection (all six or none) and/or the WorldGuard region it came from.
    public int? MinX { get; set; }
    public int? MinY { get; set; }
    public int? MinZ { get; set; }
    public int? MaxX { get; set; }
    public int? MaxY { get; set; }
    public int? MaxZ { get; set; }
    public string? WgRegionId { get; set; }

    // W5: pre-filled from the admin's look direction, overridable. W70: the class is measured as width across the
    // facing x length along it, and worked out again when the facing changes (phase 2).
    public SiegeWeaponFacing Facing { get; set; } = SiegeWeaponFacing.North;
    public SiegeWeaponSizeClass SizeClass { get; set; } = SiegeWeaponSizeClass.Small;
}
