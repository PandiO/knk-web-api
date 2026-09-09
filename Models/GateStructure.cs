using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using knkwebapi_v2.Attributes;

namespace knkwebapi_v2.Models;

[FormConfigurableEntity("GateStructure")]
public class GateStructure : Structure
{
    // === Core Gate State & Health System ===
    public bool IsActive { get; set; } = false;
    public bool CanRespawn { get; set; } = true;
    public bool IsDestroyed { get; set; } = false;
    public bool IsInvincible { get; set; } = true;
    public bool IsOpened { get; set; } = false;
    public bool IsJammed { get; set; } = false;
    public double HealthCurrent { get; set; } = 500.0;
    public double HealthMax { get; set; } = 500.0;
    public string FaceDirection { get; set; } = "north";
    public int RespawnRateSeconds { get; set; } = 300;
    
    [RelatedEntityField(typeof(MinecraftMaterialRef))]
    public int? IconMaterialRefId { get; set; }
    
    [RelatedEntityField(typeof(MinecraftMaterialRef))]
    public MinecraftMaterialRef? IconMaterial { get; set; } = null;
    
    [DefaultValue("")]
    public string RegionClosedId { get; set; } = string.Empty;

    [DefaultValue("")]
    public string RegionOpenedId { get; set; } = string.Empty;

    // === Gate Type & Animation Configuration ===
    public GateType GateType { get; set; } = GateType.SLIDING;
    
    public GeometryDefinitionMode GeometryDefinitionMode { get; set; } = GeometryDefinitionMode.PLANE_GRID;
    
    public MotionType MotionType { get; set; } = MotionType.VERTICAL;
    
    public int AnimationDurationTicks { get; set; } = 60;  // Default 3 seconds @ 20 TPS
    public int AnimationTickRate { get; set; } = 1;  // Frames per tick

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

    // === Block Management ===
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
    
    public bool MirrorRotation { get; set; } = true;

    // === Pass-Through System ===
    public bool AllowPassThrough { get; set; } = false;
    public int PassThroughDurationSeconds { get; set; } = 4;
    
    [MaxLength(2000)]
    public string PassThroughConditionsJson { get; set; } = string.Empty;  // Complex conditions

    // === Guard & Defense System (Future Feature) ===
    [RelatedEntityField(typeof(Location))]
    public virtual ICollection<Location> GuardSpawnLocations { get; set; } = new List<Location>();
    
    public int GuardCount { get; set; } = 0;
    public int? GuardNpcTemplateId { get; set; }  // FK to NpcTemplate (future)

    // === Health Display Configuration ===
    public bool ShowHealthDisplay { get; set; } = true;
    
    public HealthDisplayMode HealthDisplayMode { get; set; } = HealthDisplayMode.ALWAYS;

    public int HealthDisplayYOffset { get; set; } = 2;

    // Optional manual override for where the info hover (name/health/status) renders in the world.
    // When set, the plugin uses this position as-is instead of computing one from the gate's
    // geometry/FaceDirection.
    [NavigationPair(nameof(InfoDisplayLocation))]
    [RelatedEntityField(typeof(Location))]
    public int? InfoDisplayLocationId { get; set; }

    [RelatedEntityField(typeof(Location))]
    public Location? InfoDisplayLocation { get; set; }

    public GateInfoDisplayMode GateNameDisplayMode { get; set; } = GateInfoDisplayMode.ALWAYS;

    public GateInfoDisplayMode StatusDisplayMode { get; set; } = GateInfoDisplayMode.ALWAYS;

    // === Siege Integration ===
    public bool IsOverridable { get; set; } = true;
    public bool AnimateDuringSiege { get; set; } = true;
    public int? CurrentSiegeId { get; set; }  // FK to Siege (future)
    public bool IsSiegeObjective { get; set; } = false;

    // === Combat System: Continuous Damage ===
    public bool AllowContinuousDamage { get; set; } = true;
    public double ContinuousDamageMultiplier { get; set; } = 1.0;
    public int ContinuousDamageDurationSeconds { get; set; } = 5;

    // === Navigation Properties ===
    public virtual ICollection<GateBlockSnapshot> BlockSnapshots { get; set; } = new List<GateBlockSnapshot>();

    // Optional, separately-scanned fully-open shape - see OpenAnchorPointId above and
    // docs/features/gate-structure-animation/ROTATION_GAP_FILL_DESIGN.md.
    public virtual ICollection<GateOpenedBlockSnapshot> OpenedBlockSnapshots { get; set; } = new List<GateOpenedBlockSnapshot>();
}
