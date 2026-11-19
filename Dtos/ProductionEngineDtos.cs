using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    // Production engine P3 (PandiO/knk-workspace#86, PRODUCTION_KINDS.md § 2-5). Enums travel as their names. On create
    // and update a null field means "keep" (create: the default); child lists (levels' items, lines' inputs, zones' odds
    // and towns, weather types' effects) replace the stored list when sent and are kept when null.

    public class ProductionStructureNavDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    }

    /// <summary>A related row's id and name (for display seeds that read <c>item.itemBlueprint.name</c> and the like).</summary>
    public class NamedRefDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    }

    public class ProductionKindNavDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    }

    /// <summary>A production kind: <c>api/ProductionKinds</c>. Its ten levels are created with it.</summary>
    public class ProductionKindDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("icon")] public string? Icon { get; set; }

        /// <summary>BorderedField, ExclusiveVolume, SharedRadius, Pasture or HuntingGround.</summary>
        [JsonPropertyName("areaModel")] public string? AreaModel { get; set; }
        [JsonPropertyName("exclusiveAreas")] public bool? ExclusiveAreas { get; set; }
        [JsonPropertyName("allowsDerived")] public bool? AllowsDerived { get; set; }
        [JsonPropertyName("allowsConfigured")] public bool? AllowsConfigured { get; set; }
        [JsonPropertyName("allowsHybrid")] public bool? AllowsHybrid { get; set; }

        /// <summary>Crops, Wood, Stone, Ore, Fish, Flowers or Animals.</summary>
        [JsonPropertyName("resourceCategory")] public string? ResourceCategory { get; set; }
        [JsonPropertyName("defaultWorkers")] public int? DefaultWorkers { get; set; }
        [JsonPropertyName("cycleVariation")] public bool? CycleVariation { get; set; }

        /// <summary>JSON list of <c>{"purpose":"Input","name":"inputs","maxUnits":500}</c>; "" clears it.</summary>
        [JsonPropertyName("defaultStoragesJson")] public string? DefaultStoragesJson { get; set; }
        [JsonPropertyName("isActive")] public bool? IsActive { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }

        /// <summary>Read only: the ten levels.</summary>
        [JsonPropertyName("levels")] public List<ProductionKindLevelDto>? Levels { get; set; }

        // ---- P4 (#138) ----

        /// <summary>A JSON object the plugin's survey reads (e.g. <c>{"unitTypes":["TREE"]}</c>); a change raises the version.</summary>
        [JsonPropertyName("surveyProfileJson")] public string? SurveyProfileJson { get; set; }

        /// <summary>Read only.</summary>
        [JsonPropertyName("surveyProfileVersion")] public int SurveyProfileVersion { get; set; }

        /// <summary>The primary outputs' factor on Derived and Hybrid lines (default 1.5).</summary>
        [JsonPropertyName("primaryMultiplier")] public decimal? PrimaryMultiplier { get; set; }

        /// <summary>Replaces the stored yield table when sent.</summary>
        [JsonPropertyName("yieldTable")] public List<ProductionYieldRowDto>? YieldTable { get; set; }

        /// <summary>Replaces the stored primary outputs when sent.</summary>
        [JsonPropertyName("primaryOutputs")] public List<ProductionKindPrimaryOutputDto>? PrimaryOutputs { get; set; }
    }

    public class ProductionKindListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("areaModel")] public string AreaModel { get; set; } = string.Empty;
        [JsonPropertyName("resourceCategory")] public string ResourceCategory { get; set; } = string.Empty;
        [JsonPropertyName("defaultWorkers")] public int DefaultWorkers { get; set; }
        [JsonPropertyName("isActive")] public bool IsActive { get; set; }
        [JsonPropertyName("structureCount")] public int StructureCount { get; set; }
    }

    public class ProductionKindLevelItemDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }

        /// <summary>Unlock or UpgradeCost.</summary>
        [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("itemBlueprint")] public NamedRefDto? ItemBlueprint { get; set; }

        /// <summary>Units of an upgrade cost; ignored (0) for an unlock.</summary>
        [JsonPropertyName("amount")] public long Amount { get; set; }
    }

    /// <summary>A kind's level: <c>api/ProductionKindLevels</c> (update only; every kind has levels 1-10).</summary>
    public class ProductionKindLevelDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("productionKindId")] public int ProductionKindId { get; set; }
        [JsonPropertyName("productionKind")] public ProductionKindNavDto? ProductionKind { get; set; }
        [JsonPropertyName("level")] public int Level { get; set; }
        [JsonPropertyName("yieldMultiplier")] public decimal? YieldMultiplier { get; set; }
        [JsonPropertyName("cycleSpeedMultiplier")] public decimal? CycleSpeedMultiplier { get; set; }

        /// <summary>0 clears it (no cap).</summary>
        [JsonPropertyName("maxAreaSize")] public int? MaxAreaSize { get; set; }

        /// <summary>0 clears it (leave the storage as it is).</summary>
        [JsonPropertyName("outputMaxUnits")] public int? OutputMaxUnits { get; set; }

        /// <summary>0 clears it.</summary>
        [JsonPropertyName("unitsPerSlot")] public int? UnitsPerSlot { get; set; }

        /// <summary>0 clears it.</summary>
        [JsonPropertyName("transportCapacity")] public int? TransportCapacity { get; set; }
        [JsonPropertyName("upkeepReduction")] public decimal? UpkeepReduction { get; set; }
        [JsonPropertyName("upgradeCoins")] public long? UpgradeCoins { get; set; }

        /// <summary>0 clears it.</summary>
        [JsonPropertyName("upgradeDurationMinutes")] public int? UpgradeDurationMinutes { get; set; }

        [JsonPropertyName("items")] public List<ProductionKindLevelItemDto>? Items { get; set; }
    }

    public class ProductionKindLevelListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("productionKindId")] public int ProductionKindId { get; set; }
        [JsonPropertyName("productionKindName")] public string ProductionKindName { get; set; } = string.Empty;
        [JsonPropertyName("level")] public int Level { get; set; }
        [JsonPropertyName("yieldMultiplier")] public decimal YieldMultiplier { get; set; }
        [JsonPropertyName("cycleSpeedMultiplier")] public decimal CycleSpeedMultiplier { get; set; }
        [JsonPropertyName("upgradeCoins")] public long UpgradeCoins { get; set; }
    }

    public class ProductionLineInputDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("itemBlueprint")] public NamedRefDto? ItemBlueprint { get; set; }
        [JsonPropertyName("amountPerHour")] public decimal AmountPerHour { get; set; }
        [JsonPropertyName("accrued")] public decimal Accrued { get; set; }
    }

    /// <summary>A production line: <c>api/ProductionLines</c>. Status, accrual and due times are read only.</summary>
    public class ProductionLineDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("productionStructureId")] public int? ProductionStructureId { get; set; }
        [JsonPropertyName("productionStructure")] public ProductionStructureNavDto? ProductionStructure { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int? ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprint")] public ItemBlueprintNavDto? ItemBlueprint { get; set; }

        /// <summary>Configured in P3 (Derived and Hybrid come with P4).</summary>
        [JsonPropertyName("rateSource")] public string? RateSource { get; set; }
        [JsonPropertyName("configuredRatePerHour")] public decimal? ConfiguredRatePerHour { get; set; }

        /// <summary>Overrides the kind's category; "" clears it.</summary>
        [JsonPropertyName("resourceCategory")] public string? ResourceCategory { get; set; }
        [JsonPropertyName("enabled")] public bool? Enabled { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("statusReason")] public string? StatusReason { get; set; }
        [JsonPropertyName("accrued")] public decimal Accrued { get; set; }
        [JsonPropertyName("anchorAt")] public DateTime AnchorAt { get; set; }
        [JsonPropertyName("lastSettledAt")] public DateTime LastSettledAt { get; set; }
        [JsonPropertyName("nextSettleAt")] public DateTime NextSettleAt { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("inputs")] public List<ProductionLineInputDto>? Inputs { get; set; }
    }

    public class ProductionLineListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("productionStructureId")] public int ProductionStructureId { get; set; }
        [JsonPropertyName("productionStructureName")] public string ProductionStructureName { get; set; } = string.Empty;
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string ItemBlueprintName { get; set; } = string.Empty;
        [JsonPropertyName("rateSource")] public string RateSource { get; set; } = string.Empty;
        [JsonPropertyName("configuredRatePerHour")] public decimal ConfiguredRatePerHour { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
        [JsonPropertyName("nextSettleAt")] public DateTime NextSettleAt { get; set; }
    }

    /// <summary>A stored modifier: <c>api/ProductionModifiers</c>.</summary>
    public class ProductionModifierDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }

        /// <summary>Read only for now: staff rows are "Admin".</summary>
        [JsonPropertyName("source")] public string? Source { get; set; }

        /// <summary>Global, Kingdom, Town, Territory, MapArea, Structure or Output.</summary>
        [JsonPropertyName("scope")] public string? Scope { get; set; }

        /// <summary>The kingdom, town, structure or production line id (0 clears it).</summary>
        [JsonPropertyName("scopeRefId")] public int? ScopeRefId { get; set; }

        /// <summary>Any, Blueprint, Category or Kind.</summary>
        [JsonPropertyName("target")] public string? Target { get; set; }
        [JsonPropertyName("targetItemBlueprintId")] public int? TargetItemBlueprintId { get; set; }
        [JsonPropertyName("targetCategory")] public string? TargetCategory { get; set; }
        [JsonPropertyName("targetProductionKindId")] public int? TargetProductionKindId { get; set; }

        /// <summary>Add or Multiply.</summary>
        [JsonPropertyName("effect")] public string? Effect { get; set; }
        [JsonPropertyName("value")] public decimal? Value { get; set; }
        [JsonPropertyName("activeFrom")] public DateTime? ActiveFrom { get; set; }
        [JsonPropertyName("activeUntil")] public DateTime? ActiveUntil { get; set; }

        /// <summary>Set true to clear <see cref="ActiveFrom"/> and <see cref="ActiveUntil"/> (always active).</summary>
        [JsonPropertyName("clearWindow")] public bool? ClearWindow { get; set; }
        [JsonPropertyName("enabled")] public bool? Enabled { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
    }

    public class ProductionModifierListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
        [JsonPropertyName("scope")] public string Scope { get; set; } = string.Empty;
        [JsonPropertyName("scopeRefId")] public int? ScopeRefId { get; set; }
        [JsonPropertyName("target")] public string Target { get; set; } = string.Empty;
        [JsonPropertyName("effect")] public string Effect { get; set; } = string.Empty;
        [JsonPropertyName("value")] public decimal Value { get; set; }
        [JsonPropertyName("activeFrom")] public DateTime? ActiveFrom { get; set; }
        [JsonPropertyName("activeUntil")] public DateTime? ActiveUntil { get; set; }
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
    }

    public class ClimateZoneWeatherDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("weatherTypeId")] public int WeatherTypeId { get; set; }
        [JsonPropertyName("weatherTypeName")] public string? WeatherTypeName { get; set; }
        [JsonPropertyName("weatherType")] public NamedRefDto? WeatherType { get; set; }
        [JsonPropertyName("chancePercent")] public decimal ChancePercent { get; set; }
    }

    public class ClimateZoneTownDto
    {
        [JsonPropertyName("townId")] public int TownId { get; set; }
        [JsonPropertyName("townName")] public string? TownName { get; set; }
        [JsonPropertyName("town")] public NamedRefDto? Town { get; set; }
    }

    /// <summary>A climate zone: <c>api/ClimateZones</c>.</summary>
    public class ClimateZoneDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }

        /// <summary>Setting true makes this the default zone (the previous default stops being it); a default zone can't
        /// be unset directly, make another zone the default instead.</summary>
        [JsonPropertyName("isDefault")] public bool? IsDefault { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }

        /// <summary>Weather odds in percent; their sum is at most 100 (the rest is clear).</summary>
        [JsonPropertyName("weatherOdds")] public List<ClimateZoneWeatherDto>? WeatherOdds { get; set; }

        /// <summary>The towns in this zone; a town moves here from its previous zone.</summary>
        [JsonPropertyName("towns")] public List<ClimateZoneTownDto>? Towns { get; set; }

        /// <summary>Read only: the latest stored rolls (newest first; all of them: <c>POST api/ClimateZones/rolls/search</c>).</summary>
        [JsonPropertyName("rolls")] public List<WeatherRollDto>? Rolls { get; set; }
    }

    public class ClimateZoneListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("isDefault")] public bool IsDefault { get; set; }
        [JsonPropertyName("townCount")] public int TownCount { get; set; }
        [JsonPropertyName("weatherTypeCount")] public int WeatherTypeCount { get; set; }
    }

    public class WeatherTypeEffectDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("category")] public string Category { get; set; } = string.Empty;
        [JsonPropertyName("effect")] public string Effect { get; set; } = string.Empty;
        [JsonPropertyName("value")] public decimal Value { get; set; }
    }

    /// <summary>A weather type: <c>api/WeatherTypes</c>.</summary>
    public class WeatherTypeDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("effects")] public List<WeatherTypeEffectDto>? Effects { get; set; }
    }

    public class WeatherTypeListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("effectCount")] public int EffectCount { get; set; }
    }

    public class WeatherRollDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("climateZoneId")] public int ClimateZoneId { get; set; }
        [JsonPropertyName("climateZoneName")] public string? ClimateZoneName { get; set; }
        [JsonPropertyName("day")] public long Day { get; set; }
        [JsonPropertyName("dayStartsAt")] public DateTime DayStartsAt { get; set; }
        [JsonPropertyName("dayEndsAt")] public DateTime DayEndsAt { get; set; }
        [JsonPropertyName("weatherTypeId")] public int? WeatherTypeId { get; set; }

        /// <summary>"Clear" when no weather type was rolled.</summary>
        [JsonPropertyName("weatherTypeName")] public string WeatherTypeName { get; set; } = "Clear";
        /// <summary>Null when clear.</summary>
        [JsonPropertyName("weatherType")] public NamedRefDto? WeatherType { get; set; }
        [JsonPropertyName("rollValue")] public decimal RollValue { get; set; }
        [JsonPropertyName("rolledAt")] public DateTime RolledAt { get; set; }
    }

    /// <summary>One applied modifier in a cycle trace, in the order applied.</summary>
    public class AppliedModifierDto
    {
        [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("scope")] public string Scope { get; set; } = string.Empty;
        [JsonPropertyName("effect")] public string Effect { get; set; } = string.Empty;
        [JsonPropertyName("value")] public decimal Value { get; set; }
        [JsonPropertyName("modifierId")] public int? ModifierId { get; set; }
    }

    public class ConsumedInputDto
    {
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("consumed")] public long Consumed { get; set; }
    }

    /// <summary>One settle of one line: <c>api/ProductionCycleRecords</c> (read only).</summary>
    public class ProductionCycleRecordDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("productionLineId")] public int ProductionLineId { get; set; }
        [JsonPropertyName("productionStructureId")] public int ProductionStructureId { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("settledAt")] public DateTime SettledAt { get; set; }
        [JsonPropertyName("periodStart")] public DateTime PeriodStart { get; set; }
        [JsonPropertyName("periodEnd")] public DateTime PeriodEnd { get; set; }
        [JsonPropertyName("elapsedSeconds")] public long ElapsedSeconds { get; set; }
        [JsonPropertyName("skippedSeconds")] public long SkippedSeconds { get; set; }
        [JsonPropertyName("rateSource")] public string RateSource { get; set; } = string.Empty;
        [JsonPropertyName("baseRatePerHour")] public decimal BaseRatePerHour { get; set; }
        [JsonPropertyName("modifiers")] public List<AppliedModifierDto> Modifiers { get; set; } = new();
        [JsonPropertyName("unlockFactor")] public decimal UnlockFactor { get; set; }
        [JsonPropertyName("workersPresent")] public int WorkersPresent { get; set; }
        [JsonPropertyName("workersTotal")] public int WorkersTotal { get; set; }
        [JsonPropertyName("workerFactor")] public decimal WorkerFactor { get; set; }
        [JsonPropertyName("effectiveRatePerHour")] public decimal EffectiveRatePerHour { get; set; }
        [JsonPropertyName("accruedBefore")] public decimal AccruedBefore { get; set; }
        [JsonPropertyName("accruedAfter")] public decimal AccruedAfter { get; set; }
        [JsonPropertyName("emitted")] public long Emitted { get; set; }
        [JsonPropertyName("accepted")] public long Accepted { get; set; }
        [JsonPropertyName("inputs")] public List<ConsumedInputDto> Inputs { get; set; } = new();
        [JsonPropertyName("statusAfter")] public string StatusAfter { get; set; } = string.Empty;
        [JsonPropertyName("shortfallReason")] public string? ShortfallReason { get; set; }
        [JsonPropertyName("stockTransactionId")] public long? StockTransactionId { get; set; }
    }

    /// <summary>
    /// A production structure's engine view (<c>GET api/ProductionStructures/{id}/production</c>): its lines with the
    /// rate they would run at now, the reasons a line is not producing, and the latest cycles.
    /// </summary>
    public class ProductionOverviewDto
    {
        [JsonPropertyName("productionStructureId")] public int ProductionStructureId { get; set; }
        [JsonPropertyName("featureEnabled")] public bool FeatureEnabled { get; set; }
        [JsonPropertyName("productionEnabled")] public bool ProductionEnabled { get; set; }
        [JsonPropertyName("productionKind")] public ProductionKindNavDto? ProductionKind { get; set; }
        [JsonPropertyName("level")] public int Level { get; set; }
        [JsonPropertyName("workersPresent")] public int WorkersPresent { get; set; }
        [JsonPropertyName("workersTotal")] public int WorkersTotal { get; set; }
        [JsonPropertyName("outputStorageId")] public int? OutputStorageId { get; set; }
        [JsonPropertyName("inputStorageId")] public int? InputStorageId { get; set; }
        [JsonPropertyName("climateZone")] public string? ClimateZone { get; set; }
        [JsonPropertyName("weatherToday")] public string? WeatherToday { get; set; }

        /// <summary>Structure-wide reasons nothing is produced (feature off, switched off, no kind, no output storage).</summary>
        [JsonPropertyName("notProducingReasons")] public List<string> NotProducingReasons { get; set; } = new();
        [JsonPropertyName("lines")] public List<ProductionLineOverviewDto> Lines { get; set; } = new();
        [JsonPropertyName("recentCycles")] public List<ProductionCycleRecordDto> RecentCycles { get; set; } = new();
    }

    public class ProductionLineOverviewDto
    {
        [JsonPropertyName("line")] public ProductionLineDto Line { get; set; } = new();

        /// <summary>The rate the line would run at now: base, then each modifier, then the built-in factors.</summary>
        [JsonPropertyName("baseRatePerHour")] public decimal BaseRatePerHour { get; set; }
        [JsonPropertyName("modifiers")] public List<AppliedModifierDto> Modifiers { get; set; } = new();
        [JsonPropertyName("unlockFactor")] public decimal UnlockFactor { get; set; }
        [JsonPropertyName("workerFactor")] public decimal WorkerFactor { get; set; }
        [JsonPropertyName("effectiveRatePerHour")] public decimal EffectiveRatePerHour { get; set; }

        /// <summary>Why this line produces nothing (or less) right now; empty = producing at the rate shown.</summary>
        [JsonPropertyName("notProducingReasons")] public List<string> NotProducingReasons { get; set; } = new();
    }

    /// <summary><c>PUT api/ProductionStructures/{id}/workers</c>: the workers at the structure now (P5, the plugin).</summary>
    public class WorkersPresentUpdateDto
    {
        [JsonPropertyName("workersPresent")] public int WorkersPresent { get; set; }
    }

    /// <summary><c>POST api/ProductionStructures/{id}/upgrade</c>: raise the level by one, paying the next level's cost.</summary>
    public class ProductionUpgradeRequestDto
    {
        /// <summary>The storage the resource cost is taken from; null = the structure's Output storage.</summary>
        [JsonPropertyName("payFromStorageId")] public int? PayFromStorageId { get; set; }

        /// <summary>The player who pays the coin cost; required when the level costs coins.</summary>
        [JsonPropertyName("payerUserId")] public int? PayerUserId { get; set; }

        /// <summary>Makes a retry safe: the same key applies once. Null = a key from the structure and target level.</summary>
        [JsonPropertyName("idempotencyKey")] public string? IdempotencyKey { get; set; }
    }

    public class ProductionUpgradeResultDto
    {
        [JsonPropertyName("productionStructureId")] public int ProductionStructureId { get; set; }
        [JsonPropertyName("fromLevel")] public int FromLevel { get; set; }
        [JsonPropertyName("toLevel")] public int ToLevel { get; set; }
        [JsonPropertyName("coinsPaid")] public long CoinsPaid { get; set; }
        [JsonPropertyName("stockTransactionId")] public long? StockTransactionId { get; set; }
        [JsonPropertyName("currencyTransactionId")] public string? CurrencyTransactionId { get; set; }
    }

    /// <summary>What a sweep (or <c>POST api/Production/run-now</c>) did.</summary>
    public class ProductionSweepResultDto
    {
        [JsonPropertyName("featureEnabled")] public bool FeatureEnabled { get; set; }
        [JsonPropertyName("ranAt")] public DateTime RanAt { get; set; }
        [JsonPropertyName("weatherRolled")] public int WeatherRolled { get; set; }
        [JsonPropertyName("linesSettled")] public int LinesSettled { get; set; }
        [JsonPropertyName("unitsEmitted")] public long UnitsEmitted { get; set; }
        [JsonPropertyName("unitsAccepted")] public long UnitsAccepted { get; set; }
        [JsonPropertyName("linesFailed")] public int LinesFailed { get; set; }
    }
}
