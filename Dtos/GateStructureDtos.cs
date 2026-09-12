using System;
using System.Text.Json.Serialization;
using knkwebapi_v2.Json;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Dtos
{
    // GateStructureDto - full read/write shape for the structure-level entity. Per-door fields
    // (geometry, animation, health, block snapshots, etc.) moved to GateDoorDto by item 5's
    // multi-door support - see docs/features/gate-structure-animation/
    // GATESTRUCTURE_QOL_IMPLEMENTATION_PLAN.md. Structure-level cascading overrides (decision
    // 5.0-B) live here as nullable fields alongside the embedded GateDoors list.
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

        [JsonPropertyName("iconMaterialRefId")]
        [JsonConverter(typeof(NullableIntConverter))]
        public int? IconMaterialRefId { get; set; }

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

        // === Structure-level cascading overrides (decision 5.0-B) ===
        // Null means "no override, each door uses its own value". Set/cleared via
        // PATCH /api/GateStructures/{id}/overrides, not via this general read/write DTO.
        [JsonPropertyName("isActiveOverride")]
        public bool? IsActiveOverride { get; set; }

        [JsonPropertyName("canRespawnOverride")]
        public bool? CanRespawnOverride { get; set; }

        [JsonPropertyName("isDestroyedOverride")]
        public bool? IsDestroyedOverride { get; set; }

        [JsonPropertyName("isInvincibleOverride")]
        public bool? IsInvincibleOverride { get; set; }

        [JsonPropertyName("openedStateOverride")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GateDoorOpenState? OpenedStateOverride { get; set; }

        [JsonPropertyName("allowPassThroughOverride")]
        public bool? AllowPassThroughOverride { get; set; }

        [JsonPropertyName("passThroughDurationSecondsOverride")]
        public int? PassThroughDurationSecondsOverride { get; set; }

        [JsonPropertyName("showHealthDisplayOverride")]
        public bool? ShowHealthDisplayOverride { get; set; }

        [JsonPropertyName("healthDisplayModeOverride")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public HealthDisplayMode? HealthDisplayModeOverride { get; set; }

        [JsonPropertyName("healthDisplayYOffsetOverride")]
        public int? HealthDisplayYOffsetOverride { get; set; }

        [JsonPropertyName("gateNameDisplayModeOverride")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GateInfoDisplayMode? GateNameDisplayModeOverride { get; set; }

        [JsonPropertyName("statusDisplayModeOverride")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GateInfoDisplayMode? StatusDisplayModeOverride { get; set; }

        [JsonPropertyName("allowContinuousDamageOverride")]
        public bool? AllowContinuousDamageOverride { get; set; }

        [JsonPropertyName("continuousDamageMultiplierOverride")]
        public double? ContinuousDamageMultiplierOverride { get; set; }

        // === Navigation Properties ===
        [JsonPropertyName("gateDoors")]
        public List<GateDoorDto>? GateDoors { get; set; }

        [JsonPropertyName("street")]
        public GateStructureStreetDto? Street { get; set; }

        [JsonPropertyName("district")]
        public GateStructureDistrictDto? District { get; set; }

        [JsonPropertyName("iconMaterialRef")]
        public MinecraftMaterialRefDto? IconMaterialRef { get; set; }
    }

    // Sets/clears the structure-level cascading overrides (decision 5.0-B). A field left null
    // is NOT changed - to clear an override explicitly, pass its "Clear" flag instead, since
    // "field absent" and "field explicitly cleared" both serialize the same way as null.
    public class GateStructureOverridesUpdateDto
    {
        [JsonPropertyName("isActiveOverride")]
        public bool? IsActiveOverride { get; set; }
        [JsonPropertyName("clearIsActiveOverride")]
        public bool ClearIsActiveOverride { get; set; }

        [JsonPropertyName("canRespawnOverride")]
        public bool? CanRespawnOverride { get; set; }
        [JsonPropertyName("clearCanRespawnOverride")]
        public bool ClearCanRespawnOverride { get; set; }

        [JsonPropertyName("isDestroyedOverride")]
        public bool? IsDestroyedOverride { get; set; }
        [JsonPropertyName("clearIsDestroyedOverride")]
        public bool ClearIsDestroyedOverride { get; set; }

        [JsonPropertyName("isInvincibleOverride")]
        public bool? IsInvincibleOverride { get; set; }
        [JsonPropertyName("clearIsInvincibleOverride")]
        public bool ClearIsInvincibleOverride { get; set; }

        [JsonPropertyName("openedStateOverride")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GateDoorOpenState? OpenedStateOverride { get; set; }
        [JsonPropertyName("clearOpenedStateOverride")]
        public bool ClearOpenedStateOverride { get; set; }

        [JsonPropertyName("allowPassThroughOverride")]
        public bool? AllowPassThroughOverride { get; set; }
        [JsonPropertyName("clearAllowPassThroughOverride")]
        public bool ClearAllowPassThroughOverride { get; set; }

        [JsonPropertyName("passThroughDurationSecondsOverride")]
        public int? PassThroughDurationSecondsOverride { get; set; }
        [JsonPropertyName("clearPassThroughDurationSecondsOverride")]
        public bool ClearPassThroughDurationSecondsOverride { get; set; }

        [JsonPropertyName("showHealthDisplayOverride")]
        public bool? ShowHealthDisplayOverride { get; set; }
        [JsonPropertyName("clearShowHealthDisplayOverride")]
        public bool ClearShowHealthDisplayOverride { get; set; }

        [JsonPropertyName("healthDisplayModeOverride")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public HealthDisplayMode? HealthDisplayModeOverride { get; set; }
        [JsonPropertyName("clearHealthDisplayModeOverride")]
        public bool ClearHealthDisplayModeOverride { get; set; }

        [JsonPropertyName("healthDisplayYOffsetOverride")]
        public int? HealthDisplayYOffsetOverride { get; set; }
        [JsonPropertyName("clearHealthDisplayYOffsetOverride")]
        public bool ClearHealthDisplayYOffsetOverride { get; set; }

        [JsonPropertyName("gateNameDisplayModeOverride")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GateInfoDisplayMode? GateNameDisplayModeOverride { get; set; }
        [JsonPropertyName("clearGateNameDisplayModeOverride")]
        public bool ClearGateNameDisplayModeOverride { get; set; }

        [JsonPropertyName("statusDisplayModeOverride")]
        [JsonConverter(typeof(JsonStringEnumConverter))]
        public GateInfoDisplayMode? StatusDisplayModeOverride { get; set; }
        [JsonPropertyName("clearStatusDisplayModeOverride")]
        public bool ClearStatusDisplayModeOverride { get; set; }

        [JsonPropertyName("allowContinuousDamageOverride")]
        public bool? AllowContinuousDamageOverride { get; set; }
        [JsonPropertyName("clearAllowContinuousDamageOverride")]
        public bool ClearAllowContinuousDamageOverride { get; set; }

        [JsonPropertyName("continuousDamageMultiplierOverride")]
        public double? ContinuousDamageMultiplierOverride { get; set; }
        [JsonPropertyName("clearContinuousDamageMultiplierOverride")]
        public bool ClearContinuousDamageMultiplierOverride { get; set; }
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

        // Per-door fields (isActive, gateType, healthCurrent, etc.) no longer have a single
        // well-defined structure-level value now that a structure can have multiple doors -
        // see GATESTRUCTURE_QOL_IMPLEMENTATION_PLAN.md item 5. doorCount replaces them here;
        // per-door detail is available via GET /api/GateStructures/{id} -> gateDoors.
        [JsonPropertyName("doorCount")]
        public int doorCount { get; set; }
    }

    // GateBlockSnapshot DTO
    public class GateBlockSnapshotDto
    {
        [JsonPropertyName("id")]
        public int? Id { get; set; }

        [JsonPropertyName("gateDoorId")]
        public int GateDoorId { get; set; }

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

        [JsonPropertyName("gateDoorId")]
        public int GateDoorId { get; set; }

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
