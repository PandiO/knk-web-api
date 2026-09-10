using System;
using System.Text.Json.Serialization;
using knkwebapi_v2.Json;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Dtos
{
    public class GateStructureReadDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("createdAt")]
        public DateTime CreatedAt { get; set; }

        [JsonPropertyName("allowEntry")]
        public bool AllowEntry { get; set; }

        [JsonPropertyName("allowExit")]
        public bool AllowExit { get; set; }

        [JsonPropertyName("wgRegionId")]
        public string WgRegionId { get; set; } = string.Empty;

        [JsonPropertyName("locationId")]
        public int? LocationId { get; set; }

        [JsonPropertyName("streetId")]
        public int StreetId { get; set; }

        [JsonPropertyName("districtId")]
        public int DistrictId { get; set; }

        [JsonPropertyName("houseNumber")]
        public int HouseNumber { get; set; }

        // === Core Gate State & Health System ===
        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }

        [JsonPropertyName("canRespawn")]
        public bool CanRespawn { get; set; }

        [JsonPropertyName("isDestroyed")]
        public bool IsDestroyed { get; set; }

        [JsonPropertyName("isInvincible")]
        public bool IsInvincible { get; set; }

        [JsonPropertyName("isOpened")]
        public bool IsOpened { get; set; }

        [JsonPropertyName("isJammed")]
        public bool IsJammed { get; set; }

        [JsonPropertyName("healthCurrent")]
        public double HealthCurrent { get; set; }

        [JsonPropertyName("healthMax")]
        public double HealthMax { get; set; }

        [JsonPropertyName("faceDirection")]
        public string FaceDirection { get; set; } = string.Empty;

        [JsonPropertyName("respawnRateSeconds")]
        public int RespawnRateSeconds { get; set; }

        [JsonPropertyName("iconMaterialRefId")]
        public int? IconMaterialRefId { get; set; }

        [JsonPropertyName("regionClosedId")]
        public string RegionClosedId { get; set; } = string.Empty;

        [JsonPropertyName("regionOpenedId")]
        public string RegionOpenedId { get; set; } = string.Empty;

        // === Gate Type & Animation Configuration ===
        [JsonPropertyName("gateType")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GateType GateType { get; set; }

        [JsonPropertyName("geometryDefinitionMode")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GeometryDefinitionMode GeometryDefinitionMode { get; set; }

        [JsonPropertyName("motionType")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public MotionType MotionType { get; set; }

        [JsonPropertyName("animationDurationTicks")]
        public int AnimationDurationTicks { get; set; }

        [JsonPropertyName("animationTickRate")]
        public int AnimationTickRate { get; set; }

        // === Geometry Definition (PLANE_GRID mode) ===
        [JsonPropertyName("anchorPointId")]
        public int? AnchorPointId { get; set; }

        [JsonPropertyName("anchorPoint")]
        public LocationDto? AnchorPoint { get; set; }

        [JsonPropertyName("referencePoint1Id")]
        public int? ReferencePoint1Id { get; set; }

        [JsonPropertyName("referencePoint1")]
        public LocationDto? ReferencePoint1 { get; set; }

        [JsonPropertyName("referencePoint2Id")]
        public int? ReferencePoint2Id { get; set; }

        [JsonPropertyName("referencePoint2")]
        public LocationDto? ReferencePoint2 { get; set; }

        [JsonPropertyName("geometryWidth")]
        public int GeometryWidth { get; set; }

        [JsonPropertyName("geometryHeight")]
        public int GeometryHeight { get; set; }

        [JsonPropertyName("geometryDepth")]
        public int GeometryDepth { get; set; }

        [JsonPropertyName("motionDistanceBlocks")]
        public int MotionDistanceBlocks { get; set; }

        [JsonPropertyName("clipToGeometryBounds")]
        public bool ClipToGeometryBounds { get; set; }

        // === Geometry Definition (FLOOD_FILL mode) ===
        [JsonPropertyName("seedBlocks")]
        public string SeedBlocks { get; set; } = string.Empty;

        [JsonPropertyName("scanMaxBlocks")]
        public int ScanMaxBlocks { get; set; }

        [JsonPropertyName("scanMaxRadius")]
        public int ScanMaxRadius { get; set; }

        [JsonPropertyName("scanMaterialWhitelist")]
        public string ScanMaterialWhitelist { get; set; } = string.Empty;

        [JsonPropertyName("scanMaterialBlacklist")]
        public string ScanMaterialBlacklist { get; set; } = string.Empty;

        [JsonPropertyName("scanPlaneConstraint")]
        public bool ScanPlaneConstraint { get; set; }

        // === Block Management ===
        [JsonPropertyName("fallbackMaterialRefId")]
        public int? FallbackMaterialRefId { get; set; }

        [JsonPropertyName("tileEntityPolicy")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TileEntityPolicy TileEntityPolicy { get; set; }

        // === Rotation-Specific Fields ===
        [JsonPropertyName("rotationMaxAngleDegrees")]
        public int RotationMaxAngleDegrees { get; set; }

        [JsonPropertyName("hingeAxisId")]
        public int? HingeAxisId { get; set; }

        [JsonPropertyName("hingeAxis")]
        public LocationDto? HingeAxis { get; set; }

        // === Double Doors Specific ===
        [JsonPropertyName("leftDoorSeedBlockId")]
        public int? LeftDoorSeedBlockId { get; set; }

        [JsonPropertyName("leftDoorSeedBlock")]
        public LocationDto? LeftDoorSeedBlock { get; set; }

        [JsonPropertyName("rightDoorSeedBlockId")]
        public int? RightDoorSeedBlockId { get; set; }

        [JsonPropertyName("rightDoorSeedBlock")]
        public LocationDto? RightDoorSeedBlock { get; set; }

        [JsonPropertyName("mirrorRotation")]
        public bool MirrorRotation { get; set; }

        // === Pass-Through System ===
        [JsonPropertyName("allowPassThrough")]
        public bool AllowPassThrough { get; set; }

        [JsonPropertyName("passThroughDurationSeconds")]
        public int PassThroughDurationSeconds { get; set; }

        [JsonPropertyName("passThroughConditionsJson")]
        public string PassThroughConditionsJson { get; set; } = string.Empty;

        // === Guard & Defense System ===
        [JsonPropertyName("guardSpawnLocationIds")]
        public List<int>? GuardSpawnLocationIds { get; set; }

        [JsonPropertyName("guardSpawnLocations")]
        public List<LocationDto>? GuardSpawnLocations { get; set; }

        [JsonPropertyName("guardCount")]
        public int GuardCount { get; set; }

        [JsonPropertyName("guardNpcTemplateId")]
        public int? GuardNpcTemplateId { get; set; }

        // === Health Display Configuration ===
        [JsonPropertyName("showHealthDisplay")]
        public bool ShowHealthDisplay { get; set; }

        [JsonPropertyName("healthDisplayMode")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public HealthDisplayMode HealthDisplayMode { get; set; }

        [JsonPropertyName("healthDisplayYOffset")]
        public int HealthDisplayYOffset { get; set; }

        [JsonPropertyName("infoDisplayLocationId")]
        public int? InfoDisplayLocationId { get; set; }

        [JsonPropertyName("infoDisplayLocation")]
        public LocationDto? InfoDisplayLocation { get; set; }

        [JsonPropertyName("gateNameDisplayMode")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GateInfoDisplayMode GateNameDisplayMode { get; set; }

        [JsonPropertyName("statusDisplayMode")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GateInfoDisplayMode StatusDisplayMode { get; set; }

        // === Siege Integration ===
        [JsonPropertyName("isOverridable")]
        public bool IsOverridable { get; set; }

        [JsonPropertyName("animateDuringSiege")]
        public bool AnimateDuringSiege { get; set; }

        [JsonPropertyName("currentSiegeId")]
        public int? CurrentSiegeId { get; set; }

        [JsonPropertyName("isSiegeObjective")]
        public bool IsSiegeObjective { get; set; }

        // === Combat System: Continuous Damage ===
        [JsonPropertyName("allowContinuousDamage")]
        public bool AllowContinuousDamage { get; set; }

        [JsonPropertyName("continuousDamageMultiplier")]
        public double ContinuousDamageMultiplier { get; set; }

        [JsonPropertyName("continuousDamageDurationSeconds")]
        public int ContinuousDamageDurationSeconds { get; set; }

        // === Navigation Properties ===
        [JsonPropertyName("blockSnapshots")]
        public List<GateBlockSnapshotDto>? BlockSnapshots { get; set; }

        // Optional, separately-scanned fully-open shape - its presence overrides the
        // procedurally-derived open animation. See ROTATION_GAP_FILL_DESIGN.md.
        [JsonPropertyName("openedBlockSnapshots")]
        public List<GateOpenedBlockSnapshotDto>? OpenedBlockSnapshots { get; set; }

        [JsonPropertyName("street")]
        public GateStructureStreetDto? Street { get; set; }

        [JsonPropertyName("district")]
        public GateStructureDistrictDto? District { get; set; }

        [JsonPropertyName("iconMaterialRef")]
        public MinecraftMaterialRefDto? IconMaterialRef { get; set; }

        [JsonPropertyName("fallbackMaterialRef")]
        public MinecraftMaterialRefDto? FallbackMaterialRef { get; set; }
    }

    public class GateStructureCreateDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("allowEntry")]
        public bool AllowEntry { get; set; } = true;

        [JsonPropertyName("allowExit")]
        public bool AllowExit { get; set; } = true;

        [JsonPropertyName("wgRegionId")]
        public string WgRegionId { get; set; } = string.Empty;

        [JsonPropertyName("locationId")]
        public int? LocationId { get; set; }

        [JsonPropertyName("streetId")]
        public int StreetId { get; set; }

        [JsonPropertyName("districtId")]
        public int DistrictId { get; set; }

        [JsonPropertyName("houseNumber")]
        public int HouseNumber { get; set; }

        [JsonPropertyName("iconMaterialRefId")]
        public int? IconMaterialRefId { get; set; }

        [JsonPropertyName("gateType")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GateType GateType { get; set; } = knkwebapi_v2.Models.GateType.SLIDING;

        [JsonPropertyName("geometryDefinitionMode")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GeometryDefinitionMode GeometryDefinitionMode { get; set; } = knkwebapi_v2.Models.GeometryDefinitionMode.PLANE_GRID;

        [JsonPropertyName("motionType")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public MotionType MotionType { get; set; } = knkwebapi_v2.Models.MotionType.VERTICAL;

        [JsonPropertyName("faceDirection")]
        public string FaceDirection { get; set; } = "north";

        [JsonPropertyName("anchorPointId")]
        public int? AnchorPointId { get; set; }

        [JsonPropertyName("anchorPoint")]
        public LocationDto? AnchorPoint { get; set; }

        [JsonPropertyName("referencePoint1Id")]
        public int? ReferencePoint1Id { get; set; }

        [JsonPropertyName("referencePoint1")]
        public LocationDto? ReferencePoint1 { get; set; }

        [JsonPropertyName("referencePoint2Id")]
        public int? ReferencePoint2Id { get; set; }

        [JsonPropertyName("referencePoint2")]
        public LocationDto? ReferencePoint2 { get; set; }

        [JsonPropertyName("geometryWidth")]
        public int GeometryWidth { get; set; }

        [JsonPropertyName("geometryHeight")]
        public int GeometryHeight { get; set; }

        [JsonPropertyName("geometryDepth")]
        public int GeometryDepth { get; set; }

        [JsonPropertyName("motionDistanceBlocks")]
        public int MotionDistanceBlocks { get; set; }

        [JsonPropertyName("clipToGeometryBounds")]
        public bool ClipToGeometryBounds { get; set; }

        [JsonPropertyName("seedBlocks")]
        public string SeedBlocks { get; set; } = string.Empty;

        [JsonPropertyName("scanMaxBlocks")]
        public int ScanMaxBlocks { get; set; } = 500;

        [JsonPropertyName("scanMaxRadius")]
        public int ScanMaxRadius { get; set; } = 20;

        [JsonPropertyName("scanMaterialWhitelist")]
        public string ScanMaterialWhitelist { get; set; } = string.Empty;

        [JsonPropertyName("scanMaterialBlacklist")]
        public string ScanMaterialBlacklist { get; set; } = string.Empty;

        [JsonPropertyName("scanPlaneConstraint")]
        public bool ScanPlaneConstraint { get; set; }

        [JsonPropertyName("animationDurationTicks")]
        public int AnimationDurationTicks { get; set; } = 60;

        [JsonPropertyName("animationTickRate")]
        public int AnimationTickRate { get; set; } = 1;

        [JsonPropertyName("fallbackMaterialRefId")]
        public int? FallbackMaterialRefId { get; set; }

        [JsonPropertyName("tileEntityPolicy")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TileEntityPolicy TileEntityPolicy { get; set; } = knkwebapi_v2.Models.TileEntityPolicy.DECORATIVE_ONLY;

        [JsonPropertyName("rotationMaxAngleDegrees")]
        public int RotationMaxAngleDegrees { get; set; } = 90;

        [JsonPropertyName("hingeAxisId")]
        public int? HingeAxisId { get; set; }

        [JsonPropertyName("hingeAxis")]
        public LocationDto? HingeAxis { get; set; }

        [JsonPropertyName("leftDoorSeedBlockId")]
        public int? LeftDoorSeedBlockId { get; set; }

        [JsonPropertyName("leftDoorSeedBlock")]
        public LocationDto? LeftDoorSeedBlock { get; set; }

        [JsonPropertyName("rightDoorSeedBlockId")]
        public int? RightDoorSeedBlockId { get; set; }

        [JsonPropertyName("rightDoorSeedBlock")]
        public LocationDto? RightDoorSeedBlock { get; set; }

        [JsonPropertyName("mirrorRotation")]
        public bool MirrorRotation { get; set; } = true;

        [JsonPropertyName("healthMax")]
        public double HealthMax { get; set; } = 500.0;

        [JsonPropertyName("isInvincible")]
        public bool IsInvincible { get; set; } = true;

        [JsonPropertyName("canRespawn")]
        public bool CanRespawn { get; set; } = true;

        [JsonPropertyName("respawnRateSeconds")]
        public int RespawnRateSeconds { get; set; } = 300;

        [JsonPropertyName("regionClosedId")]
        public string RegionClosedId { get; set; } = string.Empty;

        [JsonPropertyName("regionOpenedId")]
        public string RegionOpenedId { get; set; } = string.Empty;
    }

    public class GateStructureUpdateDto
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("description")]
        public string Description { get; set; } = string.Empty;

        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }

        [JsonPropertyName("healthMax")]
        public double HealthMax { get; set; }

        [JsonPropertyName("isInvincible")]
        public bool IsInvincible { get; set; }

        [JsonPropertyName("canRespawn")]
        public bool CanRespawn { get; set; }

        [JsonPropertyName("respawnRateSeconds")]
        public int RespawnRateSeconds { get; set; }

        [JsonPropertyName("animationDurationTicks")]
        public int AnimationDurationTicks { get; set; }

        [JsonPropertyName("animationTickRate")]
        public int AnimationTickRate { get; set; }

        [JsonPropertyName("regionClosedId")]
        public string RegionClosedId { get; set; } = string.Empty;

        [JsonPropertyName("regionOpenedId")]
        public string RegionOpenedId { get; set; } = string.Empty;
    }

    public class GateStateUpdateDto
    {
        [JsonPropertyName("isOpened")]
        public bool IsOpened { get; set; }

        [JsonPropertyName("isDestroyed")]
        public bool IsDestroyed { get; set; }

        [JsonPropertyName("isJammed")]
        public bool IsJammed { get; set; }
    }

    public class GateOperationalSettingsUpdateDto
    {
        [JsonPropertyName("isActive")]
        public bool IsActive { get; set; }

        [JsonPropertyName("isInvincible")]
        public bool IsInvincible { get; set; }
    }

    public class GateHealthUpdateDto
    {
        [JsonPropertyName("healthCurrent")]
        public double HealthCurrent { get; set; }
    }

    public class GateStructureNavDto
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("gateType")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GateType GateType { get; set; }

        [JsonPropertyName("isOpened")]
        public bool IsOpened { get; set; }

        [JsonPropertyName("isJammed")]
        public bool IsJammed { get; set; }

        [JsonPropertyName("healthCurrent")]
        public double HealthCurrent { get; set; }
    }

    public class GateStructureDto
    {
        [JsonPropertyName("id")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? Id { get; set; }

        [JsonPropertyName("name")]
        public string Name { get; set; } = null!;

        [JsonPropertyName("description")]
        public string Description { get; set; } = null!;

        [JsonPropertyName("createdAt")]
        public DateTime? CreatedAt { get; set; }

        [JsonPropertyName("allowEntry")]
        [JsonConverter(typeof(NullableBoolConverter))]
        public bool? AllowEntry { get; set; }

        [JsonPropertyName("allowExit")]
        [JsonConverter(typeof(NullableBoolConverter))]
        public bool? AllowExit { get; set; }

        [JsonPropertyName("wgRegionId")]
        public string WgRegionId { get; set; } = null!;

        [JsonPropertyName("locationId")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? LocationId { get; set; }

        [JsonPropertyName("location")]
        public LocationDto? Location { get; set; }

        [JsonPropertyName("streetId")]
        public int StreetId { get; set; }

        [JsonPropertyName("districtId")]
        public int DistrictId { get; set; }

        [JsonPropertyName("houseNumber")]
        public int HouseNumber { get; set; }

        // === Core Gate State & Health System ===
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

        [JsonPropertyName("isOpened")]
        [JsonConverter(typeof(NullableBoolConverter))]
        public bool? IsOpened { get; set; }

        [JsonPropertyName("isJammed")]
        [JsonConverter(typeof(NullableBoolConverter))]
        public bool? IsJammed { get; set; }

        [JsonPropertyName("healthCurrent")]
        [JsonConverter(typeof(NullableDoubleConverter))]
        public double? HealthCurrent { get; set; }

        [JsonPropertyName("healthMax")]
        [JsonConverter(typeof(NullableDoubleConverter))]
        public double? HealthMax { get; set; }

        [JsonPropertyName("faceDirection")]
        public string FaceDirection { get; set; } = "north";

        [JsonPropertyName("respawnRateSeconds")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? RespawnRateSeconds { get; set; }

        [JsonPropertyName("iconMaterialRefId")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? IconMaterialRefId { get; set; }

        [JsonPropertyName("regionClosedId")]
        public string RegionClosedId { get; set; } = string.Empty;

        [JsonPropertyName("regionOpenedId")]
        public string RegionOpenedId { get; set; } = string.Empty;

        // === Gate Type & Animation Configuration ===
        [JsonPropertyName("gateType")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GateType GateType { get; set; } = knkwebapi_v2.Models.GateType.SLIDING;

        [JsonPropertyName("geometryDefinitionMode")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GeometryDefinitionMode GeometryDefinitionMode { get; set; } = knkwebapi_v2.Models.GeometryDefinitionMode.PLANE_GRID;

        [JsonPropertyName("motionType")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public MotionType MotionType { get; set; } = knkwebapi_v2.Models.MotionType.VERTICAL;

        [JsonPropertyName("animationDurationTicks")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? AnimationDurationTicks { get; set; }

        [JsonPropertyName("animationTickRate")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? AnimationTickRate { get; set; }

        // === Geometry Definition (PLANE_GRID mode) ===
        [JsonPropertyName("anchorPointId")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? AnchorPointId { get; set; }

        [JsonPropertyName("anchorPoint")]
        public LocationDto? AnchorPoint { get; set; }

        // Optional second physical anchor for a separately-scanned open state - see
        // docs/features/gate-structure-animation/ROTATION_GAP_FILL_DESIGN.md.
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

        [JsonPropertyName("tileEntityPolicy")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public TileEntityPolicy TileEntityPolicy { get; set; } = knkwebapi_v2.Models.TileEntityPolicy.DECORATIVE_ONLY;

        // === Rotation-Specific Fields ===
        [JsonPropertyName("rotationMaxAngleDegrees")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? RotationMaxAngleDegrees { get; set; }

        [JsonPropertyName("hingeAxisId")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? HingeAxisId { get; set; }

        [JsonPropertyName("hingeAxis")]
        public LocationDto? HingeAxis { get; set; }

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

        [JsonPropertyName("mirrorRotation")]
        [JsonConverter(typeof(NullableBoolConverter))]
        public bool? MirrorRotation { get; set; }

        // === Pass-Through System ===
        [JsonPropertyName("allowPassThrough")]
        [JsonConverter(typeof(NullableBoolConverter))]
        public bool? AllowPassThrough { get; set; }

        [JsonPropertyName("passThroughDurationSeconds")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? PassThroughDurationSeconds { get; set; }

        [JsonPropertyName("passThroughConditionsJson")]
        public string PassThroughConditionsJson { get; set; } = string.Empty;

        // === Guard & Defense System ===
        [JsonPropertyName("guardSpawnLocationIds")]
        public List<int>? GuardSpawnLocationIds { get; set; }

        [JsonPropertyName("guardSpawnLocations")]
        public List<LocationDto>? GuardSpawnLocations { get; set; }

        [JsonPropertyName("guardCount")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? GuardCount { get; set; }

        [JsonPropertyName("guardNpcTemplateId")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? GuardNpcTemplateId { get; set; }

        // === Health Display Configuration ===
        [JsonPropertyName("showHealthDisplay")]
        [JsonConverter(typeof(NullableBoolConverter))]
        public bool? ShowHealthDisplay { get; set; }

        [JsonPropertyName("healthDisplayMode")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public HealthDisplayMode HealthDisplayMode { get; set; } = knkwebapi_v2.Models.HealthDisplayMode.ALWAYS;

        [JsonPropertyName("healthDisplayYOffset")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? HealthDisplayYOffset { get; set; }

        [JsonPropertyName("infoDisplayLocationId")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? InfoDisplayLocationId { get; set; }

        [JsonPropertyName("infoDisplayLocation")]
        public LocationDto? InfoDisplayLocation { get; set; }

        [JsonPropertyName("gateNameDisplayMode")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GateInfoDisplayMode GateNameDisplayMode { get; set; } = knkwebapi_v2.Models.GateInfoDisplayMode.ALWAYS;

        [JsonPropertyName("statusDisplayMode")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GateInfoDisplayMode StatusDisplayMode { get; set; } = knkwebapi_v2.Models.GateInfoDisplayMode.ALWAYS;

        // === Siege Integration ===
        [JsonPropertyName("isOverridable")]
        [JsonConverter(typeof(NullableBoolConverter))]
        public bool? IsOverridable { get; set; }

        [JsonPropertyName("animateDuringSiege")]
        [JsonConverter(typeof(NullableBoolConverter))]
        public bool? AnimateDuringSiege { get; set; }

        [JsonPropertyName("currentSiegeId")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? CurrentSiegeId { get; set; }

        [JsonPropertyName("isSiegeObjective")]
        [JsonConverter(typeof(NullableBoolConverter))]
        public bool? IsSiegeObjective { get; set; }

        // === Combat System: Continuous Damage ===
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

        // Optional, separately-scanned fully-open shape - its presence overrides the
        // procedurally-derived open animation. See ROTATION_GAP_FILL_DESIGN.md.
        [JsonPropertyName("openedBlockSnapshots")]
        public List<GateOpenedBlockSnapshotDto>? OpenedBlockSnapshots { get; set; }

        [JsonPropertyName("street")]
        public GateStructureStreetDto? Street { get; set; }

        [JsonPropertyName("district")]
        public GateStructureDistrictDto? District { get; set; }

        [JsonPropertyName("iconMaterialRef")]
        public MinecraftMaterialRefDto? IconMaterialRef { get; set; }

        [JsonPropertyName("fallbackMaterialRef")]
        public MinecraftMaterialRefDto? FallbackMaterialRef { get; set; }
    }

    public class GateStructureListDto
    {
        [JsonPropertyName("id")]
        public int? id { get; set; }

        [JsonPropertyName("name")]
        public string name { get; set; } = null!;

        [JsonPropertyName("description")]
        public string description { get; set; } = null!;

        [JsonPropertyName("wgRegionId")]
        public string wgRegionId { get; set; } = null!;

        [JsonPropertyName("houseNumber")]
        public int houseNumber { get; set; }

        [JsonPropertyName("streetId")]
        public int streetId { get; set; }

        [JsonPropertyName("streetName")]
        public string? streetName { get; set; }

        [JsonPropertyName("districtId")]
        public int districtId { get; set; }

        [JsonPropertyName("districtName")]
        public string? districtName { get; set; }

        [JsonPropertyName("isActive")]
        public bool isActive { get; set; }

        [JsonPropertyName("healthCurrent")]
        public double healthCurrent { get; set; }

        [JsonPropertyName("healthMax")]
        public double healthMax { get; set; }

        [JsonPropertyName("isDestroyed")]
        public bool isDestroyed { get; set; }

        [JsonPropertyName("isOpened")]
        public bool isOpened { get; set; }

        [JsonPropertyName("isJammed")]
        public bool isJammed { get; set; }

        [JsonPropertyName("gateType")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GateType gateType { get; set; } = knkwebapi_v2.Models.GateType.SLIDING;

        [JsonPropertyName("faceDirection")]
        public string faceDirection { get; set; } = "north";
    }

    // GateBlockSnapshot DTO
    public class GateBlockSnapshotDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("gateStructureId")]
        public int GateStructureId { get; set; }

        [JsonPropertyName("relativeX")]
        public int RelativeX { get; set; }

        [JsonPropertyName("relativeY")]
        public int RelativeY { get; set; }

        [JsonPropertyName("relativeZ")]
        public int RelativeZ { get; set; }

        [JsonPropertyName("worldX")]
        public int WorldX { get; set; }

        [JsonPropertyName("worldY")]
        public int WorldY { get; set; }

        [JsonPropertyName("worldZ")]
        public int WorldZ { get; set; }

        [JsonPropertyName("materialName")]
        public string MaterialName { get; set; } = null!;

        [JsonPropertyName("blockDataJson")]
        public string BlockDataJson { get; set; } = "{}";

        [JsonPropertyName("tileEntityJson")]
        public string TileEntityJson { get; set; } = "{}";

        [JsonPropertyName("sortOrder")]
        public int SortOrder { get; set; }
    }

    public class GateBlockSnapshotCreateDto
    {
        [JsonPropertyName("relativeX")]
        public int RelativeX { get; set; }

        [JsonPropertyName("relativeY")]
        public int RelativeY { get; set; }

        [JsonPropertyName("relativeZ")]
        public int RelativeZ { get; set; }

        [JsonPropertyName("worldX")]
        public int WorldX { get; set; }

        [JsonPropertyName("worldY")]
        public int WorldY { get; set; }

        [JsonPropertyName("worldZ")]
        public int WorldZ { get; set; }

        [JsonPropertyName("materialName")]
        public string MaterialName { get; set; } = null!;

        [JsonPropertyName("blockDataJson")]
        public string BlockDataJson { get; set; } = "{}";

        [JsonPropertyName("tileEntityJson")]
        public string TileEntityJson { get; set; } = "{}";

        [JsonPropertyName("sortOrder")]
        public int SortOrder { get; set; }
    }

    // GateOpenedBlockSnapshot DTO - mirrors GateBlockSnapshotDto exactly, for the separately-
    // scanned fully-open shape. See ROTATION_GAP_FILL_DESIGN.md.
    public class GateOpenedBlockSnapshotDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("gateStructureId")]
        public int GateStructureId { get; set; }

        [JsonPropertyName("relativeX")]
        public int RelativeX { get; set; }

        [JsonPropertyName("relativeY")]
        public int RelativeY { get; set; }

        [JsonPropertyName("relativeZ")]
        public int RelativeZ { get; set; }

        [JsonPropertyName("worldX")]
        public int WorldX { get; set; }

        [JsonPropertyName("worldY")]
        public int WorldY { get; set; }

        [JsonPropertyName("worldZ")]
        public int WorldZ { get; set; }

        [JsonPropertyName("materialName")]
        public string MaterialName { get; set; } = null!;

        [JsonPropertyName("blockDataJson")]
        public string BlockDataJson { get; set; } = "{}";

        [JsonPropertyName("tileEntityJson")]
        public string TileEntityJson { get; set; } = "{}";

        [JsonPropertyName("sortOrder")]
        public int SortOrder { get; set; }
    }

    public class GateOpenedBlockSnapshotCreateDto
    {
        [JsonPropertyName("relativeX")]
        public int RelativeX { get; set; }

        [JsonPropertyName("relativeY")]
        public int RelativeY { get; set; }

        [JsonPropertyName("relativeZ")]
        public int RelativeZ { get; set; }

        [JsonPropertyName("worldX")]
        public int WorldX { get; set; }

        [JsonPropertyName("worldY")]
        public int WorldY { get; set; }

        [JsonPropertyName("worldZ")]
        public int WorldZ { get; set; }

        [JsonPropertyName("materialName")]
        public string MaterialName { get; set; } = null!;

        [JsonPropertyName("blockDataJson")]
        public string BlockDataJson { get; set; } = "{}";

        [JsonPropertyName("tileEntityJson")]
        public string TileEntityJson { get; set; } = "{}";

        [JsonPropertyName("sortOrder")]
        public int SortOrder { get; set; }
    }
}

namespace knkwebapi_v2.Dtos
{
    // Lightweight Street DTO for embedding in GateStructure payloads
    public class GateStructureStreetDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }
    }

    // Lightweight District DTO for embedding in GateStructure payloads
    public class GateStructureDistrictDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }

        [JsonPropertyName("allowEntry")]
        public bool? AllowEntry { get; set; }

        [JsonPropertyName("allowExit")]
        public bool? AllowExit { get; set; }

        [JsonPropertyName("wgRegionId")]
        public string? WgRegionId { get; set; }
    }
}
