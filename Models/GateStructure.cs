using System;
using System.Collections.Generic;
using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

[FormConfigurableEntity("GateStructure")]
public class GateStructure : Structure
{
    [RelatedEntityField(typeof(MinecraftMaterialRef))]
    public int? IconMaterialRefId { get; set; }

    [RelatedEntityField(typeof(MinecraftMaterialRef))]
    public MinecraftMaterialRef? IconMaterial { get; set; } = null;

    // === Guard & Defense System (Future Feature) ===
    [RelatedEntityField(typeof(Location))]
    public virtual ICollection<Location> GuardSpawnLocations { get; set; } = new List<Location>();

    public int GuardCount { get; set; } = 0;
    public int? GuardNpcTemplateId { get; set; }  // FK to NpcTemplate (future)

    // === Siege Integration ===
    // Siege capture is a whole-structure event that cascades to every door (not a per-door
    // concept), so these stay on GateStructure rather than moving to GateDoor - see
    // docs/features/gate-structure-animation/GATESTRUCTURE_QOL_IMPLEMENTATION_PLAN.md item 5.0-A.
    public bool IsOverridable { get; set; } = true;
    public bool AnimateDuringSiege { get; set; } = true;
    public int? CurrentSiegeId { get; set; }  // FK to Siege (future)
    public bool IsSiegeObjective { get; set; } = false;

    // === Structure-level cascading overrides (decision 5.0-B) ===
    // Nullable: null means "no override, each door uses its own value"; a non-null value is
    // applied as every door's effective value regardless of what that door has stored, without
    // writing to each door row. See GateDoor.GetEffective. Set/cleared via a dedicated
    // GateStructuresController override endpoint (item 5.3), not by looping over doors.
    public bool? IsActiveOverride { get; set; }
    public bool? CanRespawnOverride { get; set; }
    public bool? IsDestroyedOverride { get; set; }
    public bool? IsInvincibleOverride { get; set; }
    public GateDoorOpenState? OpenedStateOverride { get; set; }
    public bool? AllowPassThroughOverride { get; set; }
    public int? PassThroughDurationSecondsOverride { get; set; }
    public bool? ShowHealthDisplayOverride { get; set; }
    public HealthDisplayMode? HealthDisplayModeOverride { get; set; }
    public int? HealthDisplayYOffsetOverride { get; set; }
    public GateInfoDisplayMode? GateNameDisplayModeOverride { get; set; }
    public GateInfoDisplayMode? StatusDisplayModeOverride { get; set; }
    public bool? AllowContinuousDamageOverride { get; set; }
    public double? ContinuousDamageMultiplierOverride { get; set; }

    // === Navigation Properties ===
    [RelatedEntityField(typeof(GateDoor))]
    public virtual ICollection<GateDoor> GateDoors { get; set; } = new List<GateDoor>();
}
