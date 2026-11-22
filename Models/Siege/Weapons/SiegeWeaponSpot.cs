using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// A reserved place for a siege weapon in a scenario (knk-workspace docs/specs/siege-minigame/SIEGE_WEAPONS.md § 4,
// § 9.3; W3-W5, W30, W44, W70). Owned by its scenario (cascade) and by one of the scenario's teams (cascade: a spot
// without its team has no owner). Where the spot is: its WorldGuard region only (developer decision 2026-11-22,
// PandiO/knk-workspace#988), drawn or picked with the form's region world task; the API works the size class out from the
// region's bounds, which it asks the game server for.
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

    // The spot's WorldGuard region (a box; a polygon counts as its bounding box). A region drawn in the form is renamed to
    // siege_spot_<id> on save (DomainRegionNameFinalizer, #988). Null only on rows from before #988 or imported data.
    public string? WgRegionId { get; set; }

    // W5 (#988): picked in the form. W70: the class is measured from the region's bounds as width across the facing x
    // length along it x height, and worked out again on every save, so a changed facing changes the class.
    public SiegeWeaponFacing Facing { get; set; } = SiegeWeaponFacing.North;
    public SiegeWeaponSizeClass SizeClass { get; set; } = SiegeWeaponSizeClass.Small;
}
