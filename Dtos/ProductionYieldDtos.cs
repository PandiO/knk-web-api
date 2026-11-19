using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    // Yield areas and surveys P4 (PandiO/knk-workspace#138, PRODUCTION_KINDS.md § 2-4). Enums travel as their names. On
    // create and update a null field means "keep" (create: the default).

    /// <summary>One row of a kind's yield table.</summary>
    public class ProductionYieldRowDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }

        /// <summary>A survey unit key (<c>BLOCK:COAL_ORE</c>) or a prefix ending in <c>*</c> (<c>TREE:OAK:*</c>).</summary>
        [JsonPropertyName("unitKey")] public string? UnitKey { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprint")] public NamedRefDto? ItemBlueprint { get; set; }
        [JsonPropertyName("ratePerUnitPerHour")] public decimal RatePerUnitPerHour { get; set; }

        /// <summary>0-1; 1 (the default) is a main output.</summary>
        [JsonPropertyName("byproductChance")] public decimal? ByproductChance { get; set; }

        /// <summary>1-10 (default 1).</summary>
        [JsonPropertyName("minLevel")] public int? MinLevel { get; set; }
    }

    public class ProductionKindPrimaryOutputDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprint")] public NamedRefDto? ItemBlueprint { get; set; }
    }

    public class AreaPointDto
    {
        [JsonPropertyName("x")] public int X { get; set; }
        [JsonPropertyName("z")] public int Z { get; set; }
    }

    /// <summary>
    /// A production area: <c>api/ProductionAreas</c>. Shapes: <c>Region</c> (wgRegionId and its bounding box
    /// minX..maxZ), <c>Cuboid</c> (two corners as minX..maxZ, any order), <c>Polygon</c> (points plus minY/maxY) and
    /// <c>Radius</c> (radius around centerX/Y/Z, default the structure's Location; minY/maxY default to the world height).
    /// </summary>
    public class ProductionAreaDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("productionStructureId")] public int? ProductionStructureId { get; set; }
        [JsonPropertyName("productionStructure")] public ProductionStructureNavDto? ProductionStructure { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }

        /// <summary>Read only: the kind's area model.</summary>
        [JsonPropertyName("model")] public string? Model { get; set; }

        /// <summary>Region, Cuboid, Polygon or Radius.</summary>
        [JsonPropertyName("shape")] public string? Shape { get; set; }

        /// <summary>Default: the kind's (exclusive areas may not overlap).</summary>
        [JsonPropertyName("exclusive")] public bool? Exclusive { get; set; }
        [JsonPropertyName("worldName")] public string? WorldName { get; set; }
        [JsonPropertyName("wgRegionId")] public string? WgRegionId { get; set; }
        [JsonPropertyName("points")] public List<AreaPointDto>? Points { get; set; }
        [JsonPropertyName("centerX")] public int? CenterX { get; set; }
        [JsonPropertyName("centerY")] public int? CenterY { get; set; }
        [JsonPropertyName("centerZ")] public int? CenterZ { get; set; }
        [JsonPropertyName("radius")] public int? Radius { get; set; }
        [JsonPropertyName("minX")] public int? MinX { get; set; }
        [JsonPropertyName("minY")] public int? MinY { get; set; }
        [JsonPropertyName("minZ")] public int? MinZ { get; set; }
        [JsonPropertyName("maxX")] public int? MaxX { get; set; }
        [JsonPropertyName("maxY")] public int? MaxY { get; set; }
        [JsonPropertyName("maxZ")] public int? MaxZ { get; set; }

        /// <summary>Read only: blocks, volume or radius by the model (what the level's maxAreaSize caps).</summary>
        [JsonPropertyName("size")] public long Size { get; set; }

        /// <summary>Read only: "blocks", "volume" or "radius".</summary>
        [JsonPropertyName("sizeUnit")] public string? SizeUnit { get; set; }

        /// <summary>Read only: the cap of the structure's level (null = none).</summary>
        [JsonPropertyName("maxSize")] public long? MaxSize { get; set; }
        [JsonPropertyName("enabled")] public bool? Enabled { get; set; }
        [JsonPropertyName("surveyRequestedAt")] public DateTime? SurveyRequestedAt { get; set; }
        [JsonPropertyName("surveyRequestTrigger")] public string? SurveyRequestTrigger { get; set; }
        [JsonPropertyName("lastSurveyId")] public long? LastSurveyId { get; set; }
        [JsonPropertyName("lastSurveyedAt")] public DateTime? LastSurveyedAt { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
    }

    public class ProductionAreaListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("productionStructureId")] public int ProductionStructureId { get; set; }
        [JsonPropertyName("productionStructureName")] public string ProductionStructureName { get; set; } = string.Empty;
        [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
        [JsonPropertyName("shape")] public string Shape { get; set; } = string.Empty;
        [JsonPropertyName("exclusive")] public bool Exclusive { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
        [JsonPropertyName("lastSurveyedAt")] public DateTime? LastSurveyedAt { get; set; }
    }

    /// <summary>Another area whose bounding box overlaps this one (shared areas: their units are split, G7).</summary>
    public class ProductionAreaOverlapDto
    {
        [JsonPropertyName("productionAreaId")] public int ProductionAreaId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("productionStructureId")] public int ProductionStructureId { get; set; }
        [JsonPropertyName("productionStructureName")] public string ProductionStructureName { get; set; } = string.Empty;
        [JsonPropertyName("model")] public string Model { get; set; } = string.Empty;
        [JsonPropertyName("shape")] public string Shape { get; set; } = string.Empty;
        [JsonPropertyName("exclusive")] public bool Exclusive { get; set; }
        [JsonPropertyName("wgRegionId")] public string? WgRegionId { get; set; }
        [JsonPropertyName("points")] public List<AreaPointDto>? Points { get; set; }
        [JsonPropertyName("centerX")] public int? CenterX { get; set; }
        [JsonPropertyName("centerY")] public int? CenterY { get; set; }
        [JsonPropertyName("centerZ")] public int? CenterZ { get; set; }
        [JsonPropertyName("radius")] public int? Radius { get; set; }
        [JsonPropertyName("minX")] public int MinX { get; set; }
        [JsonPropertyName("minY")] public int MinY { get; set; }
        [JsonPropertyName("minZ")] public int MinZ { get; set; }
        [JsonPropertyName("maxX")] public int MaxX { get; set; }
        [JsonPropertyName("maxY")] public int MaxY { get; set; }
        [JsonPropertyName("maxZ")] public int MaxZ { get; set; }
    }

    /// <summary>
    /// An area the game server should survey now (<c>GET api/ProductionAreas/survey-due</c>): its shape, the kind's
    /// survey profile and version, why it is due, and the shared areas of other structures that overlap it (the plugin
    /// counts per unit how many structures contain it, G7).
    /// </summary>
    public class SurveyDueAreaDto
    {
        [JsonPropertyName("area")] public ProductionAreaDto Area { get; set; } = new();
        [JsonPropertyName("productionKindId")] public int? ProductionKindId { get; set; }
        [JsonPropertyName("productionKindName")] public string? ProductionKindName { get; set; }
        [JsonPropertyName("surveyProfileJson")] public string? SurveyProfileJson { get; set; }
        [JsonPropertyName("surveyProfileVersion")] public int SurveyProfileVersion { get; set; }

        /// <summary>Creation, Admin, Scheduled or NotableChange (the plugin decides notable changes itself).</summary>
        [JsonPropertyName("trigger")] public string Trigger { get; set; } = string.Empty;
        [JsonPropertyName("overlappingSharedAreas")] public List<ProductionAreaOverlapDto> OverlappingSharedAreas { get; set; } = new();
    }

    public class AreaSurveyUnitDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("unitKey")] public string? UnitKey { get; set; }
        [JsonPropertyName("count")] public long Count { get; set; }

        /// <summary>How many structures' areas contain these units (default 1).</summary>
        [JsonPropertyName("sharedBy")] public int? SharedBy { get; set; }
        [JsonPropertyName("extra")] public string? Extra { get; set; }
    }

    /// <summary>A survey: posted by the game server to <c>api/ProductionAreas/{id}/surveys</c>, read from <c>api/AreaSurveys</c>.</summary>
    public class AreaSurveyDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("productionAreaId")] public int ProductionAreaId { get; set; }
        [JsonPropertyName("productionArea")] public NamedRefDto? ProductionArea { get; set; }
        [JsonPropertyName("productionStructureId")] public int ProductionStructureId { get; set; }

        /// <summary>Default: now. At most an hour in the future.</summary>
        [JsonPropertyName("surveyedAt")] public DateTime? SurveyedAt { get; set; }
        [JsonPropertyName("receivedAt")] public DateTime ReceivedAt { get; set; }
        [JsonPropertyName("profileVersion")] public int? ProfileVersion { get; set; }

        /// <summary>Creation, Admin, Scheduled or NotableChange; default the area's pending request, else Scheduled.</summary>
        [JsonPropertyName("trigger")] public string? Trigger { get; set; }
        [JsonPropertyName("contentHash")] public string? ContentHash { get; set; }
        [JsonPropertyName("totalUnits")] public long TotalUnits { get; set; }
        [JsonPropertyName("note")] public string? Note { get; set; }
        [JsonPropertyName("units")] public List<AreaSurveyUnitDto>? Units { get; set; }

        /// <summary>Read only, on a report: what the materialization did.</summary>
        [JsonPropertyName("materialization")] public YieldMaterializationDto? Materialization { get; set; }
    }

    public class AreaSurveyListDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("productionAreaId")] public int ProductionAreaId { get; set; }
        [JsonPropertyName("productionAreaName")] public string ProductionAreaName { get; set; } = string.Empty;
        [JsonPropertyName("productionStructureId")] public int ProductionStructureId { get; set; }
        [JsonPropertyName("surveyedAt")] public DateTime SurveyedAt { get; set; }
        [JsonPropertyName("trigger")] public string Trigger { get; set; } = string.Empty;
        [JsonPropertyName("profileVersion")] public int ProfileVersion { get; set; }
        [JsonPropertyName("contentHash")] public string? ContentHash { get; set; }
        [JsonPropertyName("totalUnits")] public long TotalUnits { get; set; }
    }

    /// <summary>One unit key's count over an area's surveys (the trend, oldest first).</summary>
    public class AreaSurveyTrendDto
    {
        [JsonPropertyName("productionAreaId")] public int ProductionAreaId { get; set; }
        [JsonPropertyName("surveys")] public List<AreaSurveyListDto> Surveys { get; set; } = new();

        /// <summary>Unit key → count per survey, in the order of <see cref="Surveys"/> (0 when absent).</summary>
        [JsonPropertyName("series")] public Dictionary<string, List<long>> Series { get; set; } = new();
    }

    /// <summary>A Hybrid override: <c>api/ProductionYieldOverrides</c>.</summary>
    public class ProductionYieldOverrideDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("productionStructureId")] public int? ProductionStructureId { get; set; }
        [JsonPropertyName("productionStructure")] public ProductionStructureNavDto? ProductionStructure { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int? ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprint")] public NamedRefDto? ItemBlueprint { get; set; }

        /// <summary>Pin, Exclude or Scale.</summary>
        [JsonPropertyName("kind")] public string? Kind { get; set; }

        /// <summary>Pin: the base rate per hour; Scale: the factor (0-100); Exclude: ignored.</summary>
        [JsonPropertyName("value")] public decimal? Value { get; set; }
        [JsonPropertyName("note")] public string? Note { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
    }

    public class ProductionYieldOverrideListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("productionStructureId")] public int ProductionStructureId { get; set; }
        [JsonPropertyName("productionStructureName")] public string ProductionStructureName { get; set; } = string.Empty;
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string ItemBlueprintName { get; set; } = string.Empty;
        [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;
        [JsonPropertyName("value")] public decimal Value { get; set; }
    }

    /// <summary>One survey unit's part of an output's derived base rate.</summary>
    public class YieldContributionDto
    {
        [JsonPropertyName("productionAreaId")] public int ProductionAreaId { get; set; }
        [JsonPropertyName("areaName")] public string AreaName { get; set; } = string.Empty;
        [JsonPropertyName("areaSurveyId")] public long AreaSurveyId { get; set; }
        [JsonPropertyName("unitKey")] public string UnitKey { get; set; } = string.Empty;
        [JsonPropertyName("yieldRowUnitKey")] public string YieldRowUnitKey { get; set; } = string.Empty;
        [JsonPropertyName("count")] public long Count { get; set; }
        [JsonPropertyName("sharedBy")] public int SharedBy { get; set; }
        [JsonPropertyName("share")] public decimal Share { get; set; }
        [JsonPropertyName("ratePerUnitPerHour")] public decimal RatePerUnitPerHour { get; set; }
        [JsonPropertyName("byproductChance")] public decimal ByproductChance { get; set; }

        /// <summary>count × share × rate × chance.</summary>
        [JsonPropertyName("ratePerHour")] public decimal RatePerHour { get; set; }
    }

    /// <summary>One output of a structure and how its base rate comes about (the web app's "explain this rate").</summary>
    public class YieldOutputDto
    {
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }

        /// <summary>Σ contributions (before overrides).</summary>
        [JsonPropertyName("derivedRatePerHour")] public decimal DerivedRatePerHour { get; set; }

        /// <summary>Pin, Exclude or Scale when a Hybrid override applies.</summary>
        [JsonPropertyName("override")] public string? Override { get; set; }
        [JsonPropertyName("overrideValue")] public decimal? OverrideValue { get; set; }

        /// <summary>The base rate the engine uses (before modifiers).</summary>
        [JsonPropertyName("baseRatePerHour")] public decimal BaseRatePerHour { get; set; }

        /// <summary>A primary output of the kind (it gets the primary multiplier as a modifier).</summary>
        [JsonPropertyName("isPrimary")] public bool IsPrimary { get; set; }

        /// <summary>The line that produces it, if any.</summary>
        [JsonPropertyName("productionLineId")] public int? ProductionLineId { get; set; }
        [JsonPropertyName("lineRateSource")] public string? LineRateSource { get; set; }
        [JsonPropertyName("contributions")] public List<YieldContributionDto> Contributions { get; set; } = new();
    }

    /// <summary>A structure's yield (<c>GET api/ProductionStructures/{id}/yield</c>).</summary>
    public class ProductionYieldDto
    {
        [JsonPropertyName("productionStructureId")] public int ProductionStructureId { get; set; }
        [JsonPropertyName("yieldSource")] public string YieldSource { get; set; } = string.Empty;
        [JsonPropertyName("productionKind")] public ProductionKindNavDto? ProductionKind { get; set; }
        [JsonPropertyName("level")] public int Level { get; set; }
        [JsonPropertyName("surveysRun")] public bool SurveysRun { get; set; }
        [JsonPropertyName("areas")] public List<ProductionAreaDto> Areas { get; set; } = new();
        [JsonPropertyName("outputs")] public List<YieldOutputDto> Outputs { get; set; } = new();

        /// <summary>Survey units that no yield table row matches (they produce nothing).</summary>
        [JsonPropertyName("unmatchedUnitKeys")] public List<string> UnmatchedUnitKeys { get; set; } = new();
        [JsonPropertyName("notes")] public List<string> Notes { get; set; } = new();
    }

    /// <summary>What one materialization did to a structure's lines.</summary>
    public class YieldMaterializationDto
    {
        [JsonPropertyName("productionStructureId")] public int ProductionStructureId { get; set; }
        [JsonPropertyName("yieldSource")] public string YieldSource { get; set; } = string.Empty;
        [JsonPropertyName("linesCreated")] public List<int> LinesCreated { get; set; } = new();
        [JsonPropertyName("linesUpdated")] public List<int> LinesUpdated { get; set; } = new();

        /// <summary>Lines whose output the survey no longer yields (kept; their base rate is 0).</summary>
        [JsonPropertyName("linesWithoutYield")] public List<int> LinesWithoutYield { get; set; } = new();

        /// <summary>Outputs left to an existing Configured line of the same blueprint.</summary>
        [JsonPropertyName("skippedConfigured")] public List<int> SkippedConfigured { get; set; } = new();
        [JsonPropertyName("notes")] public List<string> Notes { get; set; } = new();
    }

    public class SurveyRequestDto
    {
        /// <summary>Optional: only this area (default: every enabled area of the structure).</summary>
        [JsonPropertyName("productionAreaId")] public int? ProductionAreaId { get; set; }
    }
}
