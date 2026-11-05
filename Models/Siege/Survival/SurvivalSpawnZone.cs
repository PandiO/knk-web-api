using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// Siege Survival phase 1 (DESIGN §6, §10 "Spawnzone", S10, S22, S50, S63; PandiO/knk-workspace#41): an NPC
// spawnzone of a scenario. Active from the start when it has no prerequisite objectives, otherwise once the
// listed objectives are NPC-held per the ALL/ANY policy (ownership-driven only: an open or destroyed door never
// activates a zone). Its spawnpoints and the optional ordered lane override are SurvivalSpawnZonePoint rows; a
// zone may instead (or also) be a bounded area around its anchor (AreaRadius). Owned by the scenario (cascade).
[FormConfigurableEntity("SurvivalSpawnZone")]
public class SurvivalSpawnZone
{
    public int Id { get; set; }

    [NavigationPair(nameof(SiegeScenario))]
    [RelatedEntityField(typeof(SiegeScenario))]
    public int SiegeScenarioId { get; set; }
    [RelatedEntityField(typeof(SiegeScenario))]
    public SiegeScenario SiegeScenario { get; set; } = null!;

    public string Name { get; set; } = null!;
    public int SortOrder { get; set; }

    // Spawn weight among the active zones (> 0).
    public double Weight { get; set; } = 1;

    // The zone's representative position (distance to the players, centre of the area). World-bound; Restrict.
    [NavigationPair(nameof(AnchorLocation))]
    [RelatedEntityField(typeof(Location))]
    public int AnchorLocationId { get; set; }
    [RelatedEntityField(typeof(Location))]
    public Location AnchorLocation { get; set; } = null!;

    // Null = spawn only at the zone's spawnpoints; a radius makes the zone a bounded area around the anchor.
    public double? AreaRadius { get; set; }

    // Prerequisites (S22): the objectives that must be NPC-held, combined per the policy; none = initially active.
    public SurvivalPrerequisitePolicy PrerequisitePolicy { get; set; } = SurvivalPrerequisitePolicy.All;

    [RelatedEntityField(typeof(SurvivalSpawnZoneObjective))]
    public ICollection<SurvivalSpawnZoneObjective> PrerequisiteObjectives { get; set; } = new List<SurvivalSpawnZoneObjective>();

    // Wave / profile eligibility: the waves the zone may spawn in (MaxWave null = no upper bound) and whether it
    // serves normal and/or special waves.
    public int MinWave { get; set; } = 1;
    public int? MaxWave { get; set; }
    public bool ForNormalWaves { get; set; } = true;
    public bool ForSpecialWaves { get; set; } = true;

    // Optional lane override (DESIGN §6): automatic alternative routing after the bounded retries is off by default
    // and opt-in per override.
    public bool LaneAutoReroute { get; set; } = false;

    [RelatedEntityField(typeof(SurvivalSpawnZonePoint))]
    public ICollection<SurvivalSpawnZonePoint> Points { get; set; } = new List<SurvivalSpawnZonePoint>();
}

// A spawnpoint of a zone, or one point of its ordered lane override (Kind). Cascades with the zone; the Location
// row is Restrict (it may be reused).
[FormConfigurableEntity("SurvivalSpawnZonePoint")]
public class SurvivalSpawnZonePoint
{
    public int Id { get; set; }

    [NavigationPair(nameof(SurvivalSpawnZone))]
    [RelatedEntityField(typeof(SurvivalSpawnZone))]
    public int SurvivalSpawnZoneId { get; set; }
    [RelatedEntityField(typeof(SurvivalSpawnZone))]
    public SurvivalSpawnZone SurvivalSpawnZone { get; set; } = null!;

    public SurvivalSpawnZonePointKind Kind { get; set; } = SurvivalSpawnZonePointKind.Spawn;
    public int SortOrder { get; set; }
    public string? Name { get; set; }

    [NavigationPair(nameof(Location))]
    [RelatedEntityField(typeof(Location))]
    public int LocationId { get; set; }
    [RelatedEntityField(typeof(Location))]
    public Location Location { get; set; } = null!;
}

// Join row: an objective that must be NPC-held before the zone activates. Cascades with the zone and with the
// objective (a deleted objective simply drops out of the prerequisites).
[FormConfigurableEntity("SurvivalSpawnZoneObjective")]
public class SurvivalSpawnZoneObjective
{
    [NavigationPair(nameof(SurvivalSpawnZone))]
    [RelatedEntityField(typeof(SurvivalSpawnZone))]
    public int SurvivalSpawnZoneId { get; set; }
    [RelatedEntityField(typeof(SurvivalSpawnZone))]
    public SurvivalSpawnZone SurvivalSpawnZone { get; set; } = null!;

    [NavigationPair(nameof(SiegeObjective))]
    [RelatedEntityField(typeof(SiegeObjective))]
    public int SiegeObjectiveId { get; set; }
    [RelatedEntityField(typeof(SiegeObjective))]
    public SiegeObjective SiegeObjective { get; set; } = null!;
}
