using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

// Introduced by item 5 (docs/features/gate-structure-animation/
// GATESTRUCTURE_QOL_IMPLEMENTATION_PLAN.md) to support multiple independently-animating doors
// per GateStructure. Holds every field that used to live directly on GateStructure and is
// meaningful per-door (geometry, animation, health, block snapshots). A handful of these
// fields can additionally be cascaded-overridden from the parent GateStructure - see the
// *Override columns on GateStructure and GetEffective below (decision 5.0-B).
[FormConfigurableEntity("GateDoor")]
public class GateDoor
{
    public int Id { get; set; }

    [NavigationPair(nameof(GateStructure))]
    [RelatedEntityField(typeof(GateStructure))]
    public int GateStructureId { get; set; }

    [RelatedEntityField(typeof(GateStructure))]
    public virtual GateStructure GateStructure { get; set; } = null!;

    // === Identity (decision 5.0-D) ===
    // Unique within the parent GateStructure (not globally) - enforced in GateDoorService.
    [MaxLength(191)]
    public string Name { get; set; } = null!;

    // === Health/Lifecycle (per-door only, no structure-level override) ===
    public double HealthCurrent { get; set; } = 500.0;
    public double HealthMax { get; set; } = 500.0;
    public int RespawnRateSeconds { get; set; } = 300;

    // === Core Door State (per-door, cascade-overridable via GateStructure.*Override) ===
    public bool IsActive { get; set; } = false;
    public bool CanRespawn { get; set; } = true;
    public bool IsDestroyed { get; set; } = false;
    public bool IsInvincible { get; set; } = true;

    // Replaces the old IsOpened + IsJammed boolean pair (decision 5.0-C).
    public GateDoorOpenState OpenedState { get; set; } = GateDoorOpenState.CLOSED;

    // === Gate Type & Animation Configuration (per-door only) ===
    public GateType GateType { get; set; } = GateType.SLIDING;

    public GeometryDefinitionMode GeometryDefinitionMode { get; set; } = GeometryDefinitionMode.PLANE_GRID;

    public MotionType MotionType { get; set; } = MotionType.VERTICAL;

    public int AnimationDurationTicks { get; set; } = 60;  // Default 3 seconds @ 20 TPS
    public int AnimationTickRate { get; set; } = 1;  // Frames per tick

    public GateFaceDirection FaceDirection { get; set; } = GateFaceDirection.NORTH;

    // === Geometry Definition (PLANE_GRID mode) ===
    [NavigationPair(nameof(AnchorPoint))]
    [RelatedEntityField(typeof(Location))]
    public int? AnchorPointId { get; set; }

    [RelatedEntityField(typeof(Location))]
    public Location? AnchorPoint { get; set; }

    // Optional second physical anchor: a separately-built, separately-scanned open state.
    // Its presence (via GateBlockSnapshot rows with State=OPEN) overrides the procedurally
    // derived open animation - see docs/features/gate-structure-animation/
    // ROTATION_GAP_FILL_DESIGN.md. Uses the same ReferencePoint1/ReferencePoint2 basis as
    // AnchorPoint, just a different physical origin for the scan.
    [NavigationPair(nameof(OpenAnchorPoint))]
    [RelatedEntityField(typeof(Location))]
    public int? OpenAnchorPointId { get; set; }

    [RelatedEntityField(typeof(Location))]
    public Location? OpenAnchorPoint { get; set; }

    [NavigationPair(nameof(ReferencePoint1))]
    [RelatedEntityField(typeof(Location))]
    public int? ReferencePoint1Id { get; set; }

    [RelatedEntityField(typeof(Location))]
    public Location? ReferencePoint1 { get; set; }

    [NavigationPair(nameof(ReferencePoint2))]
    [RelatedEntityField(typeof(Location))]
    public int? ReferencePoint2Id { get; set; }

    [RelatedEntityField(typeof(Location))]
    public Location? ReferencePoint2 { get; set; }

    public int GeometryWidth { get; set; } = 0;
    public int GeometryHeight { get; set; } = 0;
    public int GeometryDepth { get; set; } = 0;

    // Blocks travelled between closed and open. 0 falls back to the axis matching MotionType.
    public int MotionDistanceBlocks { get; set; } = 0;

    // Hides blocks that animate outside the Width/Height/Depth box, so a door can retract
    // into a housing and appear smaller when open than when closed.
    public bool ClipToGeometryBounds { get; set; } = false;

    // === Geometry Definition (FLOOD_FILL mode) ===
    [MaxLength(2000)]
    public string SeedBlocks { get; set; } = string.Empty;  // JSON array: [{x,y,z}, ...]

    public int ScanMaxBlocks { get; set; } = 500;
    public int ScanMaxRadius { get; set; } = 20;

    [MaxLength(1000)]
    public string ScanMaterialWhitelist { get; set; } = string.Empty;  // JSON: [materialIds]

    [MaxLength(1000)]
    public string ScanMaterialBlacklist { get; set; } = string.Empty;  // JSON: [materialIds]

    public bool ScanPlaneConstraint { get; set; } = false;

    // === Block Management (per-door only) ===
    [RelatedEntityField(typeof(MinecraftMaterialRef))]
    public int? FallbackMaterialRefId { get; set; }

    [RelatedEntityField(typeof(MinecraftMaterialRef))]
    public MinecraftMaterialRef? FallbackMaterial { get; set; } = null;

    public TileEntityPolicy TileEntityPolicy { get; set; } = TileEntityPolicy.DECORATIVE_ONLY;

    // === Rotation-Specific Fields (Drawbridge, Double Doors) ===
    public int RotationMaxAngleDegrees { get; set; } = 90;

    [NavigationPair(nameof(HingeAxis))]
    [RelatedEntityField(typeof(Location))]
    public int? HingeAxisId { get; set; }

    [RelatedEntityField(typeof(Location))]
    public Location? HingeAxis { get; set; }

    public bool MirrorRotation { get; set; } = true;

    // === Double Doors Specific ===
    [NavigationPair(nameof(LeftDoorSeedBlock))]
    [RelatedEntityField(typeof(Location))]
    public int? LeftDoorSeedBlockId { get; set; }

    [RelatedEntityField(typeof(Location))]
    public Location? LeftDoorSeedBlock { get; set; }

    [NavigationPair(nameof(RightDoorSeedBlock))]
    [RelatedEntityField(typeof(Location))]
    public int? RightDoorSeedBlockId { get; set; }

    [RelatedEntityField(typeof(Location))]
    public Location? RightDoorSeedBlock { get; set; }

    // === Region-based geometry (GeometryDefinitionMode.REGION; item 6) ===
    // Captured WorldEdit polygon/cuboid vertex data (JSON, shape documented in
    // WORLDGUARD_REGION_FEASIBILITY.md §9.1) for the door's closed/open footprint. Renamed and
    // repurposed from the legacy-project WorldGuard-region-name fields RegionClosedId/
    // RegionOpenedId (item 6.2) - this column no longer names a WorldGuard region, so the old
    // WG-entry-flag sync in the plugin's GateAnimationTask was removed rather than fed JSON.
    [DefaultValue("")]
    public string ClosedRegionData { get; set; } = string.Empty;

    [DefaultValue("")]
    public string OpenedRegionData { get; set; } = string.Empty;

    // === Pass-Through System (per-door value; cascade-overridable) ===
    public bool AllowPassThrough { get; set; } = false;
    public int PassThroughDurationSeconds { get; set; } = 4;

    [MaxLength(2000)]
    public string PassThroughConditionsJson { get; set; } = string.Empty;  // Complex conditions

    // === Display Position (per-door only) ===
    // Optional manual override for where the info hover (name/health/status) renders in the
    // world. When set, the plugin uses this position as-is instead of computing one from the
    // door's geometry/FaceDirection.
    [NavigationPair(nameof(InfoDisplayLocation))]
    [RelatedEntityField(typeof(Location))]
    public int? InfoDisplayLocationId { get; set; }

    [RelatedEntityField(typeof(Location))]
    public Location? InfoDisplayLocation { get; set; }

    // === Health/Name/Status Display Configuration (cascade-overridable) ===
    public bool ShowHealthDisplay { get; set; } = true;

    public HealthDisplayMode HealthDisplayMode { get; set; } = HealthDisplayMode.ALWAYS;

    public int HealthDisplayYOffset { get; set; } = 2;

    public GateInfoDisplayMode GateNameDisplayMode { get; set; } = GateInfoDisplayMode.ALWAYS;

    public GateInfoDisplayMode StatusDisplayMode { get; set; } = GateInfoDisplayMode.ALWAYS;

    // === Door-Name Display (decision 5.0-D; per-door only, no structure-level override -
    // a structure-level override doesn't make sense for a field named after the door itself) ===
    public GateInfoDisplayMode DoorNameDisplayMode { get; set; } = GateInfoDisplayMode.ALWAYS;

    // === Combat System: Continuous Damage ===
    public bool AllowContinuousDamage { get; set; } = true;
    public double ContinuousDamageMultiplier { get; set; } = 1.0;
    public int ContinuousDamageDurationSeconds { get; set; } = 5;

    // === Navigation Properties (moved from GateStructure) ===
    public virtual ICollection<GateBlockSnapshot> BlockSnapshots { get; set; } = new List<GateBlockSnapshot>();

    // Optional, separately-scanned fully-open shape - see OpenAnchorPointId above and
    // docs/features/gate-structure-animation/ROTATION_GAP_FILL_DESIGN.md.
    public virtual ICollection<GateOpenedBlockSnapshot> OpenedBlockSnapshots { get; set; } = new List<GateOpenedBlockSnapshot>();

    /// <summary>
    /// Resolves a cascade-overridable field's effective value (decision 5.0-B): the structure-level
    /// override wins when set, otherwise the door's own value applies. A single shared helper so
    /// every call site (services, animation, display) derives "effective" the same way instead of
    /// re-deriving <c>structureOverride ?? door.OwnValue</c> inline.
    /// </summary>
    public static T GetEffective<T>(GateDoor door, GateStructure structure, Func<GateDoor, T> doorSelector, Func<GateStructure, T?> overrideSelector)
        where T : struct
        => overrideSelector(structure) ?? doorSelector(door);
}
