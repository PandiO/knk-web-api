using System;
using System.Text.Json.Serialization;
using knkwebapi_v2.Json;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Dtos
{
    // GateDoorDto - full read/write shape for a single door of a GateStructure. Holds every
    // field that used to live directly on GateStructureDto and is meaningful per-door - see
    // docs/features/gate-structure-animation/GATESTRUCTURE_QOL_IMPLEMENTATION_PLAN.md item 5.
    // Follows GateStructureDto's shape/converter conventions (nullable scalars via the Nullable*
    // Converter types so a null in JSON round-trips distinctly from an explicit false/0/"").
    public class GateDoorDto
    {
        [JsonPropertyName("id")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? Id { get; set; }

        [JsonPropertyName("gateStructureId")]
        public int GateStructureId { get; set; }

        // === Identity ===
        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        // === Health/Lifecycle ===
        [JsonPropertyName("healthCurrent")]
        [JsonConverter(typeof(NullableDoubleConverter))]
        public double? HealthCurrent { get; set; }

        [JsonPropertyName("healthMax")]
        [JsonConverter(typeof(NullableDoubleConverter))]
        public double? HealthMax { get; set; }

        [JsonPropertyName("respawnRateSeconds")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? RespawnRateSeconds { get; set; }

        // === Core Door State (cascade-overridable from GateStructure) ===
        [JsonPropertyName("isActive")]
        [JsonConverter(typeof(NullableBoolConverter))]
        public bool? IsActive { get; set; }

        [JsonPropertyName("canRespawn")]
        [JsonConverter(typeof(NullableBoolConverter))]
        public bool? CanRespawn { get; set; }

        [JsonPropertyName("isDestroyed")]
        [JsonConverter(typeof(NullableBoolConverter))]
        public bool? IsDestroyed { get; set; }

        [JsonPropertyName("isInvincible")]
        [JsonConverter(typeof(NullableBoolConverter))]
        public bool? IsInvincible { get; set; }

        [JsonPropertyName("openedState")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GateDoorOpenState OpenedState { get; set; } = GateDoorOpenState.CLOSED;

        // === Gate Type & Animation Configuration ===
        [JsonPropertyName("gateType")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GateType GateType { get; set; } = GateType.SLIDING;

        [JsonPropertyName("geometryDefinitionMode")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GeometryDefinitionMode GeometryDefinitionMode { get; set; } = GeometryDefinitionMode.PLANE_GRID;

        [JsonPropertyName("motionType")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public MotionType MotionType { get; set; } = MotionType.VERTICAL;

        [JsonPropertyName("animationDurationTicks")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? AnimationDurationTicks { get; set; }

        [JsonPropertyName("animationTickRate")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? AnimationTickRate { get; set; }

        [JsonPropertyName("faceDirection")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GateFaceDirection FaceDirection { get; set; } = GateFaceDirection.NORTH;

        // === Geometry Definition (PLANE_GRID mode) ===
        [JsonPropertyName("anchorPointId")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? AnchorPointId { get; set; }

        [JsonPropertyName("anchorPoint")]
        public LocationDto? AnchorPoint { get; set; }

        [JsonPropertyName("openAnchorPointId")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? OpenAnchorPointId { get; set; }

        [JsonPropertyName("openAnchorPoint")]
        public LocationDto? OpenAnchorPoint { get; set; }

        [JsonPropertyName("referencePoint1Id")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? ReferencePoint1Id { get; set; }

        [JsonPropertyName("referencePoint1")]
        public LocationDto? ReferencePoint1 { get; set; }

        [JsonPropertyName("referencePoint2Id")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? ReferencePoint2Id { get; set; }

        [JsonPropertyName("referencePoint2")]
        public LocationDto? ReferencePoint2 { get; set; }

        [JsonPropertyName("geometryWidth")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? GeometryWidth { get; set; }

        [JsonPropertyName("geometryHeight")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? GeometryHeight { get; set; }

        [JsonPropertyName("geometryDepth")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? GeometryDepth { get; set; }

        [JsonPropertyName("motionDistanceBlocks")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? MotionDistanceBlocks { get; set; }

        [JsonPropertyName("clipToGeometryBounds")]
        [JsonConverter(typeof(NullableBoolConverter))]
        public bool? ClipToGeometryBounds { get; set; }

        // === Geometry Definition (FLOOD_FILL mode) ===
        [JsonPropertyName("seedBlocks")]
        public string SeedBlocks { get; set; } = string.Empty;

        [JsonPropertyName("scanMaxBlocks")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? ScanMaxBlocks { get; set; }

        [JsonPropertyName("scanMaxRadius")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? ScanMaxRadius { get; set; }

        [JsonPropertyName("scanMaterialWhitelist")]
        public string ScanMaterialWhitelist { get; set; } = string.Empty;

        [JsonPropertyName("scanMaterialBlacklist")]
        public string ScanMaterialBlacklist { get; set; } = string.Empty;

        [JsonPropertyName("scanPlaneConstraint")]
        [JsonConverter(typeof(NullableBoolConverter))]
        public bool? ScanPlaneConstraint { get; set; }

        // === Block Management ===
        [JsonPropertyName("fallbackMaterialRefId")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? FallbackMaterialRefId { get; set; }

        [JsonPropertyName("fallbackMaterialRef")]
        public MinecraftMaterialRefDto? FallbackMaterialRef { get; set; }

        [JsonPropertyName("tileEntityPolicy")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TileEntityPolicy TileEntityPolicy { get; set; } = TileEntityPolicy.DECORATIVE_ONLY;

        // === Rotation-Specific Fields ===
        [JsonPropertyName("rotationMaxAngleDegrees")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? RotationMaxAngleDegrees { get; set; }

        [JsonPropertyName("hingeAxisId")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? HingeAxisId { get; set; }

        [JsonPropertyName("hingeAxis")]
        public LocationDto? HingeAxis { get; set; }

        [JsonPropertyName("mirrorRotation")]
        [JsonConverter(typeof(NullableBoolConverter))]
        public bool? MirrorRotation { get; set; }

        // === Double Doors Specific ===
        [JsonPropertyName("leftDoorSeedBlockId")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? LeftDoorSeedBlockId { get; set; }

        [JsonPropertyName("leftDoorSeedBlock")]
        public LocationDto? LeftDoorSeedBlock { get; set; }

        [JsonPropertyName("rightDoorSeedBlockId")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? RightDoorSeedBlockId { get; set; }

        [JsonPropertyName("rightDoorSeedBlock")]
        public LocationDto? RightDoorSeedBlock { get; set; }

        // === Region-based geometry (GeometryDefinitionMode.REGION; item 6) ===
        [JsonPropertyName("closedRegionData")]
        public string ClosedRegionData { get; set; } = string.Empty;

        [JsonPropertyName("openedRegionData")]
        public string OpenedRegionData { get; set; } = string.Empty;

        // === Pass-Through System (cascade-overridable) ===
        [JsonPropertyName("allowPassThrough")]
        [JsonConverter(typeof(NullableBoolConverter))]
        public bool? AllowPassThrough { get; set; }

        [JsonPropertyName("passThroughDurationSeconds")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? PassThroughDurationSeconds { get; set; }

        [JsonPropertyName("passThroughConditionsJson")]
        public string PassThroughConditionsJson { get; set; } = string.Empty;

        // === Display Position ===
        [JsonPropertyName("infoDisplayLocationId")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? InfoDisplayLocationId { get; set; }

        [JsonPropertyName("infoDisplayLocation")]
        public LocationDto? InfoDisplayLocation { get; set; }

        // === Health/Name/Status Display Configuration (cascade-overridable) ===
        [JsonPropertyName("showHealthDisplay")]
        [JsonConverter(typeof(NullableBoolConverter))]
        public bool? ShowHealthDisplay { get; set; }

        [JsonPropertyName("healthDisplayMode")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public HealthDisplayMode HealthDisplayMode { get; set; } = HealthDisplayMode.ALWAYS;

        [JsonPropertyName("healthDisplayYOffset")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? HealthDisplayYOffset { get; set; }

        [JsonPropertyName("gateNameDisplayMode")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GateInfoDisplayMode GateNameDisplayMode { get; set; } = GateInfoDisplayMode.ALWAYS;

        [JsonPropertyName("statusDisplayMode")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GateInfoDisplayMode StatusDisplayMode { get; set; } = GateInfoDisplayMode.ALWAYS;

        // === Door-Name Display (decision 5.0-D; per-door only) ===
        [JsonPropertyName("doorNameDisplayMode")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GateInfoDisplayMode DoorNameDisplayMode { get; set; } = GateInfoDisplayMode.ALWAYS;

        // === Combat System: Continuous Damage (cascade-overridable) ===
        [JsonPropertyName("allowContinuousDamage")]
        [JsonConverter(typeof(NullableBoolConverter))]
        public bool? AllowContinuousDamage { get; set; }

        [JsonPropertyName("continuousDamageMultiplier")]
        [JsonConverter(typeof(NullableDoubleConverter))]
        public double? ContinuousDamageMultiplier { get; set; }

        [JsonPropertyName("continuousDamageDurationSeconds")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? ContinuousDamageDurationSeconds { get; set; }

        // === Navigation Properties ===
        [JsonPropertyName("blockSnapshots")]
        public List<GateBlockSnapshotDto>? BlockSnapshots { get; set; }

        [JsonPropertyName("openedBlockSnapshots")]
        public List<GateOpenedBlockSnapshotDto>? OpenedBlockSnapshots { get; set; }
    }

    // Lightweight nav DTO for embedding a door reference elsewhere (e.g. command/event payloads)
    // without pulling the full per-door field set - mirrors GateStructureNavDto's old role.
    public class GateDoorNavDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("gateStructureId")]
        public int GateStructureId { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("gateType")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GateType GateType { get; set; }

        [JsonPropertyName("openedState")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GateDoorOpenState OpenedState { get; set; }

        [JsonPropertyName("healthCurrent")]
        public double HealthCurrent { get; set; }
    }

    // Replaces GateStateUpdateDto (moved from GateStructure - IsOpened/IsJammed are now the
    // single GateDoorOpenState enum on GateDoor, decision 5.0-C).
    public class GateDoorStateUpdateDto
    {
        [JsonPropertyName("openedState")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GateDoorOpenState OpenedState { get; set; }

        [JsonPropertyName("isDestroyed")]
        public bool IsDestroyed { get; set; }
    }

    // Replaces GateOperationalSettingsUpdateDto (moved from GateStructure).
    public class GateDoorOperationalSettingsUpdateDto
    {
        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }

        [JsonPropertyName("isInvincible")]
        public bool IsInvincible { get; set; }
    }

    // Replaces GateHealthUpdateDto (moved from GateStructure).
    public class GateDoorHealthUpdateDto
    {
        [JsonPropertyName("healthCurrent")]
        public double HealthCurrent { get; set; }
    }
}
