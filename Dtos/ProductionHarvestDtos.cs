using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    // Player harvesting P8a (PandiO/knk-workspace#685, PRODUCTION_KINDS.md § 6-7). Enums travel as their names. On create
    // and update a null field means "keep" (create: the default).

    /// <summary>Player harvesting settings (game settings section <c>production.harvest</c>).</summary>
    public class ProductionHarvestSettingsDto
    {
        public const decimal MaxPoolCapacity = 1_000_000_000m;
        public const int MaxReportEntries = 500;

        /// <summary>The capacity of a pool made for an area that has no survey yet (on its first harvest).</summary>
        [JsonPropertyName("defaultPoolCapacity")]
        public decimal DefaultPoolCapacity { get; set; } = 1000m;

        /// <summary>A new pool regenerates its whole capacity in this many hours (<c>RegenPerHour = Capacity ÷ hours</c>).</summary>
        [JsonPropertyName("defaultRegenHoursToFull")]
        public decimal DefaultRegenHoursToFull { get; set; } = 12m;

        /// <summary>The most entries one harvest report may carry.</summary>
        [JsonPropertyName("maxEntriesPerReport")]
        public int MaxEntriesPerReport { get; set; } = 200;
    }

    /// <summary>A harvest rule: <c>api/HarvestRules</c>.</summary>
    public class HarvestRuleDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("productionKindId")] public int? ProductionKindId { get; set; }
        [JsonPropertyName("productionKind")] public ProductionKindNavDto? ProductionKind { get; set; }
        [JsonPropertyName("enabled")] public bool? Enabled { get; set; }

        /// <summary>Block or Entity.</summary>
        [JsonPropertyName("targetKind")] public string? TargetKind { get; set; }

        /// <summary>Material or entity type names; one may start or end with <c>*</c> (<c>*_LOG</c>).</summary>
        [JsonPropertyName("targets")] public List<string>? Targets { get; set; }
        [JsonPropertyName("minBlockAge")] public int? MinBlockAge { get; set; }

        /// <summary>"" clears it.</summary>
        [JsonPropertyName("requiredPermission")] public string? RequiredPermission { get; set; }

        /// <summary>0 clears it.</summary>
        [JsonPropertyName("minTitleBracketId")] public int? MinTitleBracketId { get; set; }
        [JsonPropertyName("minTitleBracket")] public NamedRefDto? MinTitleBracket { get; set; }

        /// <summary>Any, Pickaxe, Axe, Shovel, Hoe, Shears, Sword or FishingRod.</summary>
        [JsonPropertyName("toolClass")] public string? ToolClass { get; set; }

        /// <summary>Any, Wood, Stone, Iron, Gold, Diamond or Netherite.</summary>
        [JsonPropertyName("minToolTier")] public string? MinToolTier { get; set; }
        [JsonPropertyName("yieldItemBlueprintId")] public int? YieldItemBlueprintId { get; set; }
        [JsonPropertyName("yieldItemBlueprint")] public NamedRefDto? YieldItemBlueprint { get; set; }
        [JsonPropertyName("yieldPerBreak")] public decimal? YieldPerBreak { get; set; }
        [JsonPropertyName("fortuneBonusPerLevel")] public decimal? FortuneBonusPerLevel { get; set; }

        /// <summary>0-1 (default 0.25).</summary>
        [JsonPropertyName("workerShare")] public decimal? WorkerShare { get; set; }
        [JsonPropertyName("poolCost")] public decimal? PoolCost { get; set; }

        /// <summary>0 clears it.</summary>
        [JsonPropertyName("cooldownSeconds")] public int? CooldownSeconds { get; set; }

        /// <summary>DepletedBlock, Replant, Air, Sapling or None.</summary>
        [JsonPropertyName("placeholder")] public string? Placeholder { get; set; }

        /// <summary>"" clears it.</summary>
        [JsonPropertyName("placeholderMaterial")] public string? PlaceholderMaterial { get; set; }
        [JsonPropertyName("regenSeconds")] public int? RegenSeconds { get; set; }
        [JsonPropertyName("regenScalesWithPool")] public bool? RegenScalesWithPool { get; set; }
        [JsonPropertyName("note")] public string? Note { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
    }

    public class HarvestRuleListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("productionKindId")] public int ProductionKindId { get; set; }
        [JsonPropertyName("productionKindName")] public string ProductionKindName { get; set; } = string.Empty;
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
        [JsonPropertyName("targetKind")] public string TargetKind { get; set; } = string.Empty;
        [JsonPropertyName("yieldItemBlueprintId")] public int YieldItemBlueprintId { get; set; }
        [JsonPropertyName("yieldItemBlueprintName")] public string YieldItemBlueprintName { get; set; } = string.Empty;
        [JsonPropertyName("yieldPerBreak")] public decimal YieldPerBreak { get; set; }
        [JsonPropertyName("workerShare")] public decimal WorkerShare { get; set; }
        [JsonPropertyName("poolCost")] public decimal PoolCost { get; set; }
    }

    /// <summary>
    /// The net-output check of a harvest rule (PRODUCTION_KINDS § 6.3, F9), per area of its kind:
    /// <c>townShare = Yield × (1 − WorkerShare)</c> and
    /// <c>lost ≈ Σo base(o) × (1 − floor) × (PoolCost / Capacity) × (PoolCost / RegenPerHour) / 2</c>; the rule is
    /// net-positive where <c>lost &lt; townShare</c>. The web app shows both numbers and warns.
    /// </summary>
    public class HarvestRuleBalanceDto
    {
        [JsonPropertyName("harvestRuleId")] public int HarvestRuleId { get; set; }
        [JsonPropertyName("harvestRuleName")] public string HarvestRuleName { get; set; } = string.Empty;

        /// <summary>The town's units from one break without fortune.</summary>
        [JsonPropertyName("townSharePerBreak")] public decimal TownSharePerBreak { get; set; }

        /// <summary>True when every area is net-positive (also true when the kind has no areas yet).</summary>
        [JsonPropertyName("netPositive")] public bool NetPositive { get; set; }

        /// <summary>One line per area that is not net-positive, for the rule editor.</summary>
        [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = new();
        [JsonPropertyName("areas")] public List<HarvestAreaBalanceDto> Areas { get; set; } = new();
    }

    public class HarvestAreaBalanceDto
    {
        [JsonPropertyName("productionAreaId")] public int ProductionAreaId { get; set; }
        [JsonPropertyName("productionAreaName")] public string ProductionAreaName { get; set; } = string.Empty;
        [JsonPropertyName("productionStructureId")] public int ProductionStructureId { get; set; }
        [JsonPropertyName("productionStructureName")] public string ProductionStructureName { get; set; } = string.Empty;

        /// <summary>Σ over the structure's lines of the base rate per hour (before modifiers and built-in steps).</summary>
        [JsonPropertyName("baseRatePerHour")] public decimal BaseRatePerHour { get; set; }
        [JsonPropertyName("poolFloor")] public decimal PoolFloor { get; set; }
        [JsonPropertyName("poolCapacity")] public decimal PoolCapacity { get; set; }
        [JsonPropertyName("regenPerHour")] public decimal RegenPerHour { get; set; }

        /// <summary>False when the area has no pool yet: the numbers are those a new pool would get.</summary>
        [JsonPropertyName("poolExists")] public bool PoolExists { get; set; }
        [JsonPropertyName("townSharePerBreak")] public decimal TownSharePerBreak { get; set; }

        /// <summary>The production lost per break until the pool refills; null when the pool never refills (no regeneration
        /// or no capacity).</summary>
        [JsonPropertyName("lostPerBreak")] public decimal? LostPerBreak { get; set; }
        [JsonPropertyName("netPositive")] public bool NetPositive { get; set; }
    }

    /// <summary>A resource pool: <c>api/ResourcePools</c>.</summary>
    public class ResourcePoolDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("productionAreaId")] public int? ProductionAreaId { get; set; }
        [JsonPropertyName("productionArea")] public NamedRefDto? ProductionArea { get; set; }
        [JsonPropertyName("productionStructureId")] public int ProductionStructureId { get; set; }
        [JsonPropertyName("productionStructure")] public ProductionStructureNavDto? ProductionStructure { get; set; }

        /// <summary>Create: null = the latest survey's total units, else the default capacity.</summary>
        [JsonPropertyName("capacity")] public decimal? Capacity { get; set; }

        /// <summary>Read: the units left now (regeneration included). Write: set the units left (create: null = full).</summary>
        [JsonPropertyName("current")] public decimal? Current { get; set; }

        /// <summary>Create: null = capacity ÷ the default hours to full.</summary>
        [JsonPropertyName("regenPerHour")] public decimal? RegenPerHour { get; set; }
        [JsonPropertyName("capacityFromSurvey")] public bool? CapacityFromSurvey { get; set; }
        [JsonPropertyName("capacitySurveyId")] public long? CapacitySurveyId { get; set; }

        /// <summary>Read only: Current ÷ Capacity (0-1).</summary>
        [JsonPropertyName("fill")] public decimal Fill { get; set; }

        /// <summary>Read only: the pool factor the structure's production gets from this pool alone.</summary>
        [JsonPropertyName("poolFactor")] public decimal PoolFactor { get; set; }

        /// <summary>Read only: the kind's floor.</summary>
        [JsonPropertyName("poolFloor")] public decimal PoolFloor { get; set; }

        /// <summary>Read only: hours until full at the regeneration rate; null when full or not regenerating.</summary>
        [JsonPropertyName("hoursToFull")] public decimal? HoursToFull { get; set; }
        [JsonPropertyName("lastRegenAt")] public DateTime LastRegenAt { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("updatedAt")] public DateTime UpdatedAt { get; set; }
    }

    public class ResourcePoolListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("productionAreaId")] public int ProductionAreaId { get; set; }
        [JsonPropertyName("productionAreaName")] public string ProductionAreaName { get; set; } = string.Empty;
        [JsonPropertyName("productionStructureId")] public int ProductionStructureId { get; set; }
        [JsonPropertyName("productionStructureName")] public string ProductionStructureName { get; set; } = string.Empty;
        [JsonPropertyName("capacity")] public decimal Capacity { get; set; }

        /// <summary>As stored (regeneration since <c>lastRegenAt</c> not added; the detail view adds it).</summary>
        [JsonPropertyName("current")] public decimal Current { get; set; }
        [JsonPropertyName("regenPerHour")] public decimal RegenPerHour { get; set; }
        [JsonPropertyName("lastRegenAt")] public DateTime LastRegenAt { get; set; }
    }

    /// <summary><c>POST api/Harvest/reports</c>: what the game server saw players harvest (batched).</summary>
    public class HarvestReportDto
    {
        [JsonPropertyName("entries")] public List<HarvestReportEntryDto>? Entries { get; set; }
    }

    /// <summary>One player's breaks under one rule in one area. The game server may group breaks of the same player, rule,
    /// area and fortune level into one entry.</summary>
    public class HarvestReportEntryDto
    {
        /// <summary>One per entry, so a retried report books each entry once (letters, digits and <c>: _ . -</c>).</summary>
        [JsonPropertyName("idempotencyKey")] public string? IdempotencyKey { get; set; }
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("harvestRuleId")] public int HarvestRuleId { get; set; }
        [JsonPropertyName("productionAreaId")] public int ProductionAreaId { get; set; }

        /// <summary>1 or more (default 1).</summary>
        [JsonPropertyName("breaks")] public int? Breaks { get; set; }

        /// <summary>The tool's fortune level (0 without; counted up to IV).</summary>
        [JsonPropertyName("fortuneLevel")] public int? FortuneLevel { get; set; }

        /// <summary>When the harvest happened; null = now.</summary>
        [JsonPropertyName("harvestedAt")] public DateTime? HarvestedAt { get; set; }
    }

    public class HarvestReportResultDto
    {
        [JsonPropertyName("featureEnabled")] public bool FeatureEnabled { get; set; }
        [JsonPropertyName("entries")] public List<HarvestEntryResultDto> Entries { get; set; } = new();
    }

    /// <summary>What happened to one entry. The game server drops <see cref="WorkerUnits"/> of the yield blueprint for the
    /// player (the vanilla drops are replaced), puts the placeholder down and schedules the block to come back after
    /// <see cref="RegenSeconds"/>.</summary>
    public class HarvestEntryResultDto
    {
        [JsonPropertyName("idempotencyKey")] public string IdempotencyKey { get; set; } = string.Empty;

        /// <summary>Applied, Replayed or Rejected.</summary>
        [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;

        /// <summary>Why a Rejected entry was not booked.</summary>
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("playerHarvestId")] public long? PlayerHarvestId { get; set; }
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("harvestRuleId")] public int HarvestRuleId { get; set; }
        [JsonPropertyName("productionAreaId")] public int ProductionAreaId { get; set; }
        [JsonPropertyName("productionStructureId")] public int? ProductionStructureId { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int? ItemBlueprintId { get; set; }
        [JsonPropertyName("yieldUnits")] public decimal YieldUnits { get; set; }
        [JsonPropertyName("workerUnits")] public long WorkerUnits { get; set; }
        [JsonPropertyName("townUnits")] public long TownUnits { get; set; }
        [JsonPropertyName("townAccepted")] public long TownAccepted { get; set; }
        [JsonPropertyName("townLost")] public long TownLost { get; set; }
        [JsonPropertyName("poolBefore")] public decimal PoolBefore { get; set; }
        [JsonPropertyName("poolAfter")] public decimal PoolAfter { get; set; }
        [JsonPropertyName("poolCapacity")] public decimal PoolCapacity { get; set; }
        [JsonPropertyName("regenSeconds")] public int RegenSeconds { get; set; }
        [JsonPropertyName("shortfallReason")] public string? ShortfallReason { get; set; }
    }

    /// <summary><c>GET api/Harvest/catalog</c>: what the game server needs to allow breaks and regenerate blocks.</summary>
    public class HarvestCatalogDto
    {
        /// <summary>False while <c>Production:Enabled</c> is off: the lists are empty and every break stays denied.</summary>
        [JsonPropertyName("featureEnabled")] public bool FeatureEnabled { get; set; }
        [JsonPropertyName("generatedAt")] public DateTime GeneratedAt { get; set; }

        /// <summary>The enabled rules of every kind.</summary>
        [JsonPropertyName("rules")] public List<HarvestRuleDto> Rules { get; set; } = new();

        /// <summary>The areas of structures whose kind has an enabled rule.</summary>
        [JsonPropertyName("areas")] public List<HarvestCatalogAreaDto> Areas { get; set; } = new();
    }

    public class HarvestCatalogAreaDto
    {
        [JsonPropertyName("productionAreaId")] public int ProductionAreaId { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("productionStructureId")] public int ProductionStructureId { get; set; }
        [JsonPropertyName("productionStructureName")] public string ProductionStructureName { get; set; } = string.Empty;
        [JsonPropertyName("productionKindId")] public int ProductionKindId { get; set; }

        /// <summary>False when the structure's production is switched off: its reports are refused.</summary>
        [JsonPropertyName("productionEnabled")] public bool ProductionEnabled { get; set; }
        [JsonPropertyName("worldName")] public string WorldName { get; set; } = string.Empty;

        /// <summary>Region, Cuboid, Polygon or Radius.</summary>
        [JsonPropertyName("shape")] public string Shape { get; set; } = string.Empty;
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

        /// <summary>The pool's fill now (1 without a pool).</summary>
        [JsonPropertyName("poolFill")] public decimal PoolFill { get; set; }
    }

    /// <summary>A booked harvest: <c>api/PlayerHarvests</c> (read only).</summary>
    public class PlayerHarvestDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("idempotencyKey")] public string IdempotencyKey { get; set; } = string.Empty;
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("user")] public NamedRefDto? User { get; set; }
        [JsonPropertyName("harvestRuleId")] public int HarvestRuleId { get; set; }
        [JsonPropertyName("harvestRule")] public NamedRefDto? HarvestRule { get; set; }
        [JsonPropertyName("productionAreaId")] public int ProductionAreaId { get; set; }
        [JsonPropertyName("productionAreaName")] public string? ProductionAreaName { get; set; }
        [JsonPropertyName("productionStructureId")] public int ProductionStructureId { get; set; }
        [JsonPropertyName("productionStructureName")] public string? ProductionStructureName { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprint")] public NamedRefDto? ItemBlueprint { get; set; }
        [JsonPropertyName("breaks")] public int Breaks { get; set; }
        [JsonPropertyName("fortuneLevel")] public int FortuneLevel { get; set; }
        [JsonPropertyName("harvestedAt")] public DateTime HarvestedAt { get; set; }
        [JsonPropertyName("receivedAt")] public DateTime ReceivedAt { get; set; }
        [JsonPropertyName("yieldUnits")] public decimal YieldUnits { get; set; }
        [JsonPropertyName("workerShare")] public decimal WorkerShare { get; set; }
        [JsonPropertyName("workerUnits")] public long WorkerUnits { get; set; }
        [JsonPropertyName("townUnits")] public long TownUnits { get; set; }
        [JsonPropertyName("townAccepted")] public long TownAccepted { get; set; }
        [JsonPropertyName("poolBefore")] public decimal PoolBefore { get; set; }
        [JsonPropertyName("poolAfter")] public decimal PoolAfter { get; set; }
        [JsonPropertyName("poolCapacity")] public decimal PoolCapacity { get; set; }
        [JsonPropertyName("regenSeconds")] public int RegenSeconds { get; set; }
        [JsonPropertyName("workerTransactionId")] public long? WorkerTransactionId { get; set; }
        [JsonPropertyName("townTransactionId")] public long? TownTransactionId { get; set; }
        [JsonPropertyName("shortfallReason")] public string? ShortfallReason { get; set; }
    }

    public class PlayerHarvestListDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("userId")] public int UserId { get; set; }
        [JsonPropertyName("username")] public string Username { get; set; } = string.Empty;
        [JsonPropertyName("harvestRuleId")] public int HarvestRuleId { get; set; }
        [JsonPropertyName("harvestRuleName")] public string HarvestRuleName { get; set; } = string.Empty;
        [JsonPropertyName("productionStructureId")] public int ProductionStructureId { get; set; }
        [JsonPropertyName("productionAreaId")] public int ProductionAreaId { get; set; }
        [JsonPropertyName("breaks")] public int Breaks { get; set; }
        [JsonPropertyName("workerUnits")] public long WorkerUnits { get; set; }
        [JsonPropertyName("townUnits")] public long TownUnits { get; set; }
        [JsonPropertyName("townAccepted")] public long TownAccepted { get; set; }
        [JsonPropertyName("receivedAt")] public DateTime ReceivedAt { get; set; }
    }
}
