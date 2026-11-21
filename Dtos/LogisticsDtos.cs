using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    // Town logistics P9 (PandiO/knk-workspace#127, TOWN_LOGISTICS.md § 2-5). Enums travel as their names.

    /// <summary>
    /// The planner's timing and defaults (game setting <c>production.logistics</c>, TOWN_LOGISTICS § 5, D-L2, F11). A
    /// consumer storage without a replenishment setting or rule gets reorder point = line inputs per hour ×
    /// <see cref="DefaultReorderHours"/> and target level = × <see cref="DefaultTargetHours"/>.
    /// </summary>
    public class ProductionLogisticsSettingsDto
    {
        /// <summary>Minutes between two planner runs of a town (D-L2: 10).</summary>
        [JsonPropertyName("plannerIntervalMinutes")] public int PlannerIntervalMinutes { get; set; } = 10;

        /// <summary>A reservation not collected within this is released at the next run (F11).</summary>
        [JsonPropertyName("reservationMinutes")] public int ReservationMinutes { get; set; } = 30;

        /// <summary>Days of outflow averaged for "days of cover".</summary>
        [JsonPropertyName("consumptionWindowDays")] public int ConsumptionWindowDays { get; set; } = 7;

        [JsonPropertyName("defaultReorderHours")] public decimal DefaultReorderHours { get; set; } = 4m;

        [JsonPropertyName("defaultTargetHours")] public decimal DefaultTargetHours { get; set; } = 12m;

        /// <summary>Planner runs (and their explanations) older than this are deleted by the sweep.</summary>
        [JsonPropertyName("runRetentionDays")] public int RunRetentionDays { get; set; } = 14;
    }

    // ================= Policies and rules =================

    public class LogisticsPolicyDto
    {
        [JsonPropertyName("id")] public int? Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        /// <summary>Town, Warehouse or Template.</summary>
        [JsonPropertyName("scope")] public string? Scope { get; set; }
        [JsonPropertyName("townId")] public int? TownId { get; set; }
        [JsonPropertyName("town")] public NamedRefDto? Town { get; set; }
        [JsonPropertyName("warehouseId")] public int? WarehouseId { get; set; }
        [JsonPropertyName("warehouse")] public NamedRefDto? Warehouse { get; set; }
        /// <summary>Templates: a resource category (Crops, Wood, Stone, Ore, Fish, Flowers, Animals) or null for general.</summary>
        [JsonPropertyName("specialization")] public string? Specialization { get; set; }
        /// <summary>Templates: Village, Town or City.</summary>
        [JsonPropertyName("townSize")] public string? TownSize { get; set; }
        [JsonPropertyName("isDefaultTemplate")] public bool? IsDefaultTemplate { get; set; }
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("lineageId")] public int LineageId { get; set; }
        [JsonPropertyName("isCurrent")] public bool IsCurrent { get; set; }
        [JsonPropertyName("basedOnPolicyId")] public int? BasedOnPolicyId { get; set; }
        [JsonPropertyName("supersededByPolicyId")] public int? SupersededByPolicyId { get; set; }
        [JsonPropertyName("supersededAt")] public DateTime? SupersededAt { get; set; }
        [JsonPropertyName("author")] public string? Author { get; set; }
        /// <summary>Why this version was made (written on save).</summary>
        [JsonPropertyName("reason")] public string? Reason { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("createdByUserId")] public int? CreatedByUserId { get; set; }
        /// <summary>The whole rule list of the version; a save replaces it (as a new version).</summary>
        [JsonPropertyName("rules")] public List<LogisticsRuleDto>? Rules { get; set; }
    }

    public class LogisticsPolicyListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("scope")] public string Scope { get; set; } = string.Empty;
        [JsonPropertyName("townId")] public int? TownId { get; set; }
        [JsonPropertyName("townName")] public string? TownName { get; set; }
        [JsonPropertyName("warehouseId")] public int? WarehouseId { get; set; }
        [JsonPropertyName("warehouseName")] public string? WarehouseName { get; set; }
        [JsonPropertyName("specialization")] public string? Specialization { get; set; }
        [JsonPropertyName("townSize")] public string? TownSize { get; set; }
        [JsonPropertyName("isDefaultTemplate")] public bool IsDefaultTemplate { get; set; }
        [JsonPropertyName("version")] public int Version { get; set; }
        [JsonPropertyName("lineageId")] public int LineageId { get; set; }
        [JsonPropertyName("isCurrent")] public bool IsCurrent { get; set; }
        [JsonPropertyName("ruleCount")] public int RuleCount { get; set; }
        [JsonPropertyName("reason")] public string? Reason { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
    }

    public class LogisticsRuleDto
    {
        [JsonPropertyName("id")] public int? Id { get; set; }
        [JsonPropertyName("logisticsPolicyId")] public int? LogisticsPolicyId { get; set; }
        [JsonPropertyName("sequence")] public int Sequence { get; set; }
        /// <summary>Reserve, PriorityClass, ServiceArea or Replenishment.</summary>
        [JsonPropertyName("type")] public string? Type { get; set; }
        /// <summary>Town or Warehouse.</summary>
        [JsonPropertyName("scope")] public string? Scope { get; set; }
        [JsonPropertyName("warehouseId")] public int? WarehouseId { get; set; }
        [JsonPropertyName("warehouse")] public NamedRefDto? Warehouse { get; set; }
        /// <summary>Any, Blueprint, Category or Tag.</summary>
        [JsonPropertyName("targetKind")] public string? TargetKind { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int? ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprint")] public NamedRefDto? ItemBlueprint { get; set; }
        [JsonPropertyName("categoryId")] public int? CategoryId { get; set; }
        [JsonPropertyName("category")] public NamedRefDto? Category { get; set; }
        [JsonPropertyName("tagId")] public int? TagId { get; set; }
        [JsonPropertyName("tag")] public NamedRefDto? Tag { get; set; }
        /// <summary>Any, Structure or ProductionKind.</summary>
        [JsonPropertyName("consumerKind")] public string? ConsumerKind { get; set; }
        [JsonPropertyName("structureId")] public int? StructureId { get; set; }
        [JsonPropertyName("structure")] public NamedRefDto? Structure { get; set; }
        [JsonPropertyName("productionKindId")] public int? ProductionKindId { get; set; }
        [JsonPropertyName("productionKind")] public NamedRefDto? ProductionKind { get; set; }
        /// <summary>ServiceArea: District, Rural or Structure.</summary>
        [JsonPropertyName("areaKind")] public string? AreaKind { get; set; }
        [JsonPropertyName("districtId")] public int? DistrictId { get; set; }
        [JsonPropertyName("district")] public NamedRefDto? District { get; set; }
        [JsonPropertyName("units")] public long? Units { get; set; }
        [JsonPropertyName("daysOfCover")] public decimal? DaysOfCover { get; set; }
        [JsonPropertyName("reorderPoint")] public long? ReorderPoint { get; set; }
        [JsonPropertyName("targetLevel")] public long? TargetLevel { get; set; }
        /// <summary>Critical, High, Normal or Low.</summary>
        [JsonPropertyName("priorityClass")] public string? PriorityClass { get; set; }
        [JsonPropertyName("activeFrom")] public DateTime? ActiveFrom { get; set; }
        [JsonPropertyName("activeUntil")] public DateTime? ActiveUntil { get; set; }
        [JsonPropertyName("author")] public string? Author { get; set; }
        [JsonPropertyName("reason")] public string? Reason { get; set; }
        /// <summary>Read only: one line for lists and explanations ("Reserve 14 days of Bread, town-wide").</summary>
        [JsonPropertyName("summary")] public string? Summary { get; set; }
    }

    public class LogisticsRuleListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("logisticsPolicyId")] public int LogisticsPolicyId { get; set; }
        [JsonPropertyName("policyName")] public string? PolicyName { get; set; }
        [JsonPropertyName("policyVersion")] public int PolicyVersion { get; set; }
        [JsonPropertyName("policyIsCurrent")] public bool PolicyIsCurrent { get; set; }
        [JsonPropertyName("sequence")] public int Sequence { get; set; }
        [JsonPropertyName("type")] public string Type { get; set; } = string.Empty;
        [JsonPropertyName("scope")] public string Scope { get; set; } = string.Empty;
        [JsonPropertyName("summary")] public string Summary { get; set; } = string.Empty;
        [JsonPropertyName("reason")] public string? Reason { get; set; }
    }

    /// <summary>Copies a template's rules into a town's policy (a new version of the town's policy).</summary>
    public class LogisticsApplyTemplateDto
    {
        [JsonPropertyName("townId")] public int TownId { get; set; }
        [JsonPropertyName("reason")] public string? Reason { get; set; }
    }

    // ================= Consumer replenishment =================

    public class ConsumerReplenishmentDto
    {
        [JsonPropertyName("id")] public int? Id { get; set; }
        [JsonPropertyName("storageId")] public int? StorageId { get; set; }
        [JsonPropertyName("storage")] public NamedRefDto? Storage { get; set; }
        /// <summary>Read only: the structure that holds the storage.</summary>
        [JsonPropertyName("structureId")] public int? StructureId { get; set; }
        [JsonPropertyName("structure")] public NamedRefDto? Structure { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int? ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprint")] public NamedRefDto? ItemBlueprint { get; set; }
        [JsonPropertyName("reorderPoint")] public long? ReorderPoint { get; set; }
        [JsonPropertyName("targetLevel")] public long? TargetLevel { get; set; }
        /// <summary>Critical, High, Normal, Low, or null = the policy's priority rules.</summary>
        [JsonPropertyName("priorityClass")] public string? PriorityClass { get; set; }
        [JsonPropertyName("enabled")] public bool? Enabled { get; set; }
        [JsonPropertyName("note")] public string? Note { get; set; }
        [JsonPropertyName("createdAt")] public DateTime? CreatedAt { get; set; }
        [JsonPropertyName("updatedAt")] public DateTime? UpdatedAt { get; set; }
    }

    public class ConsumerReplenishmentListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("storageId")] public int StorageId { get; set; }
        [JsonPropertyName("storageName")] public string? StorageName { get; set; }
        [JsonPropertyName("structureId")] public int StructureId { get; set; }
        [JsonPropertyName("structureName")] public string? StructureName { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("reorderPoint")] public long ReorderPoint { get; set; }
        [JsonPropertyName("targetLevel")] public long TargetLevel { get; set; }
        [JsonPropertyName("priorityClass")] public string? PriorityClass { get; set; }
        [JsonPropertyName("enabled")] public bool Enabled { get; set; }
        [JsonPropertyName("updatedAt")] public DateTime UpdatedAt { get; set; }
    }

    // ================= Reservations =================

    public class LogisticsReservationDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("townId")] public int TownId { get; set; }
        [JsonPropertyName("town")] public NamedRefDto? Town { get; set; }
        [JsonPropertyName("warehouseId")] public int WarehouseId { get; set; }
        [JsonPropertyName("warehouse")] public NamedRefDto? Warehouse { get; set; }
        [JsonPropertyName("warehouseStorageId")] public int WarehouseStorageId { get; set; }
        [JsonPropertyName("warehouseStorage")] public NamedRefDto? WarehouseStorage { get; set; }
        [JsonPropertyName("consumerStructureId")] public int ConsumerStructureId { get; set; }
        [JsonPropertyName("consumerStructure")] public NamedRefDto? ConsumerStructure { get; set; }
        [JsonPropertyName("consumerStorageId")] public int ConsumerStorageId { get; set; }
        [JsonPropertyName("consumerStorage")] public NamedRefDto? ConsumerStorage { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprint")] public NamedRefDto? ItemBlueprint { get; set; }
        [JsonPropertyName("amount")] public long Amount { get; set; }
        [JsonPropertyName("collected")] public long Collected { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
        [JsonPropertyName("priorityClass")] public string PriorityClass { get; set; } = string.Empty;
        [JsonPropertyName("transportOrderId")] public int? TransportOrderId { get; set; }
        [JsonPropertyName("transportOrderStatus")] public string? TransportOrderStatus { get; set; }
        [JsonPropertyName("plannerRunId")] public int? PlannerRunId { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("expiresAt")] public DateTime ExpiresAt { get; set; }
        [JsonPropertyName("closedAt")] public DateTime? ClosedAt { get; set; }
        [JsonPropertyName("explanation")] public string? Explanation { get; set; }
        [JsonPropertyName("statusReason")] public string? StatusReason { get; set; }
    }

    public class LogisticsReservationListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("townId")] public int TownId { get; set; }
        [JsonPropertyName("townName")] public string? TownName { get; set; }
        [JsonPropertyName("warehouseId")] public int WarehouseId { get; set; }
        [JsonPropertyName("warehouseName")] public string? WarehouseName { get; set; }
        [JsonPropertyName("consumerStructureId")] public int ConsumerStructureId { get; set; }
        [JsonPropertyName("consumerStructureName")] public string? ConsumerStructureName { get; set; }
        [JsonPropertyName("consumerStorageId")] public int ConsumerStorageId { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("amount")] public long Amount { get; set; }
        [JsonPropertyName("collected")] public long Collected { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
        [JsonPropertyName("priorityClass")] public string PriorityClass { get; set; } = string.Empty;
        [JsonPropertyName("transportOrderId")] public int? TransportOrderId { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("expiresAt")] public DateTime ExpiresAt { get; set; }
    }

    public class LogisticsReleaseDto
    {
        [JsonPropertyName("reason")] public string? Reason { get; set; }
    }

    // ================= Planner runs and plans =================

    public class LogisticsPlanEntryDto
    {
        [JsonPropertyName("consumerStructureId")] public int ConsumerStructureId { get; set; }
        [JsonPropertyName("consumerStructureName")] public string? ConsumerStructureName { get; set; }
        [JsonPropertyName("consumerStorageId")] public int ConsumerStorageId { get; set; }
        [JsonPropertyName("consumerStorageName")] public string? ConsumerStorageName { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("warehouseId")] public int? WarehouseId { get; set; }
        [JsonPropertyName("warehouseName")] public string? WarehouseName { get; set; }
        [JsonPropertyName("priorityClass")] public string PriorityClass { get; set; } = string.Empty;
        [JsonPropertyName("stock")] public long Stock { get; set; }
        [JsonPropertyName("reorderPoint")] public long ReorderPoint { get; set; }
        [JsonPropertyName("targetLevel")] public long TargetLevel { get; set; }
        [JsonPropertyName("shortfall")] public long Shortfall { get; set; }
        [JsonPropertyName("reserved")] public long Reserved { get; set; }
        [JsonPropertyName("outcome")] public string Outcome { get; set; } = string.Empty;
        /// <summary>Setting (the consumer's replenishment setting), Rule (a Replenishment rule) or Inputs (from its lines).</summary>
        [JsonPropertyName("settingSource")] public string? SettingSource { get; set; }
        [JsonPropertyName("ruleIds")] public List<int> RuleIds { get; set; } = new();
        [JsonPropertyName("reservationId")] public int? ReservationId { get; set; }
        [JsonPropertyName("transportOrderId")] public int? TransportOrderId { get; set; }
        [JsonPropertyName("explanation")] public string Explanation { get; set; } = string.Empty;
    }

    /// <summary>A collection order the plan makes (or would make): the consumer's worker carries the lines from one
    /// warehouse storage to the consumer storage.</summary>
    public class LogisticsPlannedOrderDto
    {
        [JsonPropertyName("consumerStructureId")] public int ConsumerStructureId { get; set; }
        [JsonPropertyName("consumerStructureName")] public string? ConsumerStructureName { get; set; }
        [JsonPropertyName("consumerStorageId")] public int ConsumerStorageId { get; set; }
        [JsonPropertyName("warehouseId")] public int WarehouseId { get; set; }
        [JsonPropertyName("warehouseName")] public string? WarehouseName { get; set; }
        [JsonPropertyName("warehouseStorageId")] public int WarehouseStorageId { get; set; }
        [JsonPropertyName("lines")] public List<TransportLineRequestDto> Lines { get; set; } = new();
        [JsonPropertyName("carrierCapacity")] public long CarrierCapacity { get; set; }
        [JsonPropertyName("workersPresent")] public int WorkersPresent { get; set; }
        [JsonPropertyName("workersTotal")] public int WorkersTotal { get; set; }
        [JsonPropertyName("transportOrderId")] public int? TransportOrderId { get; set; }
        [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = new();
    }

    public class LogisticsReleasedDto
    {
        [JsonPropertyName("reservationId")] public int ReservationId { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
        [JsonPropertyName("reason")] public string Reason { get; set; } = string.Empty;
    }

    /// <summary>A planner run's result, or a dry run's plan (nothing written; works while Production:Enabled is off).</summary>
    public class LogisticsPlanDto
    {
        [JsonPropertyName("townId")] public int TownId { get; set; }
        [JsonPropertyName("townName")] public string? TownName { get; set; }
        [JsonPropertyName("featureEnabled")] public bool FeatureEnabled { get; set; }
        [JsonPropertyName("dryRun")] public bool DryRun { get; set; }
        [JsonPropertyName("at")] public DateTime At { get; set; }
        [JsonPropertyName("runId")] public int? RunId { get; set; }
        [JsonPropertyName("policyId")] public int? PolicyId { get; set; }
        [JsonPropertyName("policyName")] public string? PolicyName { get; set; }
        [JsonPropertyName("policyVersion")] public int? PolicyVersion { get; set; }
        /// <summary>Town (the town's own policy), DefaultTemplate, or None.</summary>
        [JsonPropertyName("policySource")] public string PolicySource { get; set; } = "None";
        [JsonPropertyName("entries")] public List<LogisticsPlanEntryDto> Entries { get; set; } = new();
        [JsonPropertyName("orders")] public List<LogisticsPlannedOrderDto> Orders { get; set; } = new();
        [JsonPropertyName("released")] public List<LogisticsReleasedDto> Released { get; set; } = new();
        [JsonPropertyName("collected")] public List<int> Collected { get; set; } = new();
        [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = new();
        [JsonPropertyName("summary")] public string Summary { get; set; } = string.Empty;
    }

    public class LogisticsPlannerRunDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("townId")] public int TownId { get; set; }
        [JsonPropertyName("town")] public NamedRefDto? Town { get; set; }
        [JsonPropertyName("logisticsPolicyId")] public int? LogisticsPolicyId { get; set; }
        [JsonPropertyName("policyVersion")] public int? PolicyVersion { get; set; }
        [JsonPropertyName("trigger")] public string Trigger { get; set; } = string.Empty;
        [JsonPropertyName("startedAt")] public DateTime StartedAt { get; set; }
        [JsonPropertyName("finishedAt")] public DateTime? FinishedAt { get; set; }
        [JsonPropertyName("demandLines")] public int DemandLines { get; set; }
        [JsonPropertyName("reservationsCreated")] public int ReservationsCreated { get; set; }
        [JsonPropertyName("ordersCreated")] public int OrdersCreated { get; set; }
        [JsonPropertyName("reservationsReleased")] public int ReservationsReleased { get; set; }
        [JsonPropertyName("reservationsCollected")] public int ReservationsCollected { get; set; }
        [JsonPropertyName("shortfalls")] public int Shortfalls { get; set; }
        [JsonPropertyName("summary")] public string? Summary { get; set; }
        [JsonPropertyName("createdByUserId")] public int? CreatedByUserId { get; set; }
        [JsonPropertyName("entries")] public List<LogisticsPlanEntryDto> Entries { get; set; } = new();
    }

    public class LogisticsPlannerRunListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("townId")] public int TownId { get; set; }
        [JsonPropertyName("townName")] public string? TownName { get; set; }
        [JsonPropertyName("trigger")] public string Trigger { get; set; } = string.Empty;
        [JsonPropertyName("startedAt")] public DateTime StartedAt { get; set; }
        [JsonPropertyName("policyVersion")] public int? PolicyVersion { get; set; }
        [JsonPropertyName("demandLines")] public int DemandLines { get; set; }
        [JsonPropertyName("reservationsCreated")] public int ReservationsCreated { get; set; }
        [JsonPropertyName("ordersCreated")] public int OrdersCreated { get; set; }
        [JsonPropertyName("reservationsReleased")] public int ReservationsReleased { get; set; }
        [JsonPropertyName("shortfalls")] public int Shortfalls { get; set; }
        [JsonPropertyName("summary")] public string? Summary { get; set; }
    }

    public class LogisticsSweepResultDto
    {
        [JsonPropertyName("featureEnabled")] public bool FeatureEnabled { get; set; }
        [JsonPropertyName("townsPlanned")] public int TownsPlanned { get; set; }
        [JsonPropertyName("reservationsCreated")] public int ReservationsCreated { get; set; }
        [JsonPropertyName("ordersCreated")] public int OrdersCreated { get; set; }
        [JsonPropertyName("reservationsReleased")] public int ReservationsReleased { get; set; }
        [JsonPropertyName("runsPruned")] public int RunsPruned { get; set; }
        [JsonPropertyName("failed")] public int Failed { get; set; }
    }

    // ================= Town views =================

    public class TownSupplyWarehouseLineDto
    {
        [JsonPropertyName("warehouseId")] public int WarehouseId { get; set; }
        [JsonPropertyName("warehouseName")] public string? WarehouseName { get; set; }
        [JsonPropertyName("stock")] public long Stock { get; set; }
        [JsonPropertyName("reserved")] public long Reserved { get; set; }
        [JsonPropertyName("reserveUnits")] public long ReserveUnits { get; set; }
        [JsonPropertyName("available")] public long Available { get; set; }
    }

    /// <summary>One blueprint across a town's warehouses (TOWN_LOGISTICS § 2 town view).</summary>
    public class TownSupplyLineDto
    {
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("stock")] public long Stock { get; set; }
        /// <summary>Held by open reservations.</summary>
        [JsonPropertyName("reserved")] public long Reserved { get; set; }
        /// <summary>The town-wide reserve (saving rule) in units.</summary>
        [JsonPropertyName("reserveUnits")] public long ReserveUnits { get; set; }
        /// <summary>stock − reserves − open reservations (town-wide, never below 0).</summary>
        [JsonPropertyName("available")] public long Available { get; set; }
        [JsonPropertyName("inflowPerDay")] public decimal InflowPerDay { get; set; }
        [JsonPropertyName("outflowPerDay")] public decimal OutflowPerDay { get; set; }
        /// <summary>stock ÷ average daily outflow; null without outflow.</summary>
        [JsonPropertyName("daysOfCover")] public decimal? DaysOfCover { get; set; }
        [JsonPropertyName("warehouses")] public List<TownSupplyWarehouseLineDto> Warehouses { get; set; } = new();
    }

    public class TownSupplyDto
    {
        [JsonPropertyName("townId")] public int TownId { get; set; }
        [JsonPropertyName("townName")] public string? TownName { get; set; }
        [JsonPropertyName("featureEnabled")] public bool FeatureEnabled { get; set; }
        [JsonPropertyName("at")] public DateTime At { get; set; }
        [JsonPropertyName("consumptionWindowDays")] public int ConsumptionWindowDays { get; set; }
        [JsonPropertyName("warehouses")] public List<NamedRefDto> Warehouses { get; set; } = new();
        [JsonPropertyName("lines")] public List<TownSupplyLineDto> Lines { get; set; } = new();
        [JsonPropertyName("openReservations")] public List<LogisticsReservationListDto> OpenReservations { get; set; } = new();
        [JsonPropertyName("policyId")] public int? PolicyId { get; set; }
        [JsonPropertyName("policySource")] public string PolicySource { get; set; } = "None";
        [JsonPropertyName("lastRun")] public LogisticsPlannerRunListDto? LastRun { get; set; }
        /// <summary>When the schedule plans the town next (null while Production:Enabled is off).</summary>
        [JsonPropertyName("nextRunAt")] public DateTime? NextRunAt { get; set; }
    }

    public class ServiceAreaEntryDto
    {
        /// <summary>District, Rural or Structure.</summary>
        [JsonPropertyName("areaKind")] public string AreaKind { get; set; } = string.Empty;
        [JsonPropertyName("districtId")] public int? DistrictId { get; set; }
        [JsonPropertyName("districtName")] public string? DistrictName { get; set; }
        [JsonPropertyName("structureId")] public int? StructureId { get; set; }
        [JsonPropertyName("structureName")] public string? StructureName { get; set; }
        [JsonPropertyName("warehouseId")] public int? WarehouseId { get; set; }
        [JsonPropertyName("warehouseName")] public string? WarehouseName { get; set; }
        /// <summary>Rule (a ServiceArea rule) or Nearest (the default: nearest warehouse by road).</summary>
        [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
        [JsonPropertyName("ruleId")] public int? RuleId { get; set; }
    }

    /// <summary>Which warehouse serves each district, the rural territory and every structure with a rule or a
    /// consumer storage (TOWN_LOGISTICS § 2).</summary>
    public class TownServiceAreasDto
    {
        [JsonPropertyName("townId")] public int TownId { get; set; }
        [JsonPropertyName("entries")] public List<ServiceAreaEntryDto> Entries { get; set; } = new();
    }

    public class ConsumerDemandLineDto
    {
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("stock")] public long Stock { get; set; }
        [JsonPropertyName("inbound")] public long Inbound { get; set; }
        [JsonPropertyName("reorderPoint")] public long ReorderPoint { get; set; }
        [JsonPropertyName("targetLevel")] public long TargetLevel { get; set; }
        [JsonPropertyName("priorityClass")] public string PriorityClass { get; set; } = string.Empty;
        /// <summary>Setting, Rule or Inputs.</summary>
        [JsonPropertyName("settingSource")] public string SettingSource { get; set; } = string.Empty;
        [JsonPropertyName("consumerReplenishmentId")] public int? ConsumerReplenishmentId { get; set; }
        [JsonPropertyName("ruleIds")] public List<int> RuleIds { get; set; } = new();
    }

    public class ConsumerStorageViewDto
    {
        [JsonPropertyName("storageId")] public int StorageId { get; set; }
        [JsonPropertyName("storageName")] public string? StorageName { get; set; }
        [JsonPropertyName("purpose")] public string Purpose { get; set; } = string.Empty;
        [JsonPropertyName("lines")] public List<ConsumerDemandLineDto> Lines { get; set; } = new();
    }

    /// <summary>A consumer as the planner sees it: storages with their min/max (and where they came from), its worker,
    /// the warehouse that serves it and its open reservations.</summary>
    public class ConsumerViewDto
    {
        [JsonPropertyName("structureId")] public int StructureId { get; set; }
        [JsonPropertyName("structureName")] public string? StructureName { get; set; }
        [JsonPropertyName("townId")] public int? TownId { get; set; }
        [JsonPropertyName("isConsumer")] public bool IsConsumer { get; set; }
        [JsonPropertyName("hasWorkers")] public bool HasWorkers { get; set; }
        [JsonPropertyName("workersTotal")] public int WorkersTotal { get; set; }
        [JsonPropertyName("workersPresent")] public int WorkersPresent { get; set; }
        [JsonPropertyName("carrierCapacity")] public long CarrierCapacity { get; set; }
        [JsonPropertyName("serviceWarehouseId")] public int? ServiceWarehouseId { get; set; }
        [JsonPropertyName("serviceWarehouseName")] public string? ServiceWarehouseName { get; set; }
        [JsonPropertyName("serviceSource")] public string? ServiceSource { get; set; }
        [JsonPropertyName("storages")] public List<ConsumerStorageViewDto> Storages { get; set; } = new();
        [JsonPropertyName("openReservations")] public List<LogisticsReservationListDto> OpenReservations { get; set; } = new();
        [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = new();
    }
}
