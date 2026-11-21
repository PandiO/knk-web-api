using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace knkwebapi_v2.Dtos
{
    // Transport core P5 (PandiO/knk-workspace#79, production DESIGN § 7). Enums travel as their names.

    // ================= Orders =================

    /// <summary>A transport order with its stops, cargo, carrier and event log: <c>GET api/TransportOrders/{id}</c>.</summary>
    public class TransportOrderDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("origin")] public string Origin { get; set; } = string.Empty;
        [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
        [JsonPropertyName("statusReason")] public string? StatusReason { get; set; }
        [JsonPropertyName("sourceStorageId")] public int SourceStorageId { get; set; }
        [JsonPropertyName("sourceStorage")] public NamedRefDto? SourceStorage { get; set; }
        [JsonPropertyName("sourceStructureId")] public int? SourceStructureId { get; set; }
        [JsonPropertyName("sourceStructure")] public NamedRefDto? SourceStructure { get; set; }
        [JsonPropertyName("cargoStorageId")] public int CargoStorageId { get; set; }
        [JsonPropertyName("cargoStorage")] public NamedRefDto? CargoStorage { get; set; }
        [JsonPropertyName("carrierStructureId")] public int? CarrierStructureId { get; set; }
        [JsonPropertyName("carrierStructure")] public NamedRefDto? CarrierStructure { get; set; }
        [JsonPropertyName("carrierCapacity")] public long? CarrierCapacity { get; set; }
        [JsonPropertyName("risk")] public decimal? Risk { get; set; }
        [JsonPropertyName("currentStructureId")] public int? CurrentStructureId { get; set; }
        [JsonPropertyName("currentStopId")] public int? CurrentStopId { get; set; }
        [JsonPropertyName("reroutes")] public int Reroutes { get; set; }
        [JsonPropertyName("cancelRequested")] public bool CancelRequested { get; set; }
        [JsonPropertyName("executeAfter")] public DateTime ExecuteAfter { get; set; }
        [JsonPropertyName("nextActionAt")] public DateTime? NextActionAt { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("departedAt")] public DateTime? DepartedAt { get; set; }
        [JsonPropertyName("legStartedAt")] public DateTime? LegStartedAt { get; set; }
        [JsonPropertyName("etaAt")] public DateTime? EtaAt { get; set; }
        [JsonPropertyName("waitingSince")] public DateTime? WaitingSince { get; set; }
        [JsonPropertyName("completedAt")] public DateTime? CompletedAt { get; set; }
        [JsonPropertyName("createdByUserId")] public int? CreatedByUserId { get; set; }
        [JsonPropertyName("note")] public string? Note { get; set; }
        [JsonPropertyName("clientKey")] public string? ClientKey { get; set; }
        [JsonPropertyName("isFinal")] public bool IsFinal { get; set; }
        /// <summary>What the Transit storage holds now (the cargo on the road).</summary>
        [JsonPropertyName("inTransit")] public List<TransportCargoDto> InTransit { get; set; } = new();
        [JsonPropertyName("stops")] public List<TransportStopDto> Stops { get; set; } = new();
        [JsonPropertyName("lines")] public List<TransportOrderLineDto> Lines { get; set; } = new();
        [JsonPropertyName("assignments")] public List<TransportAssignmentDto> Assignments { get; set; } = new();
        [JsonPropertyName("events")] public List<TransportEventDto> Events { get; set; } = new();
    }

    /// <summary>A row of <c>POST api/TransportOrders/search</c>.</summary>
    public class TransportOrderListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("origin")] public string Origin { get; set; } = string.Empty;
        [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
        [JsonPropertyName("statusReason")] public string? StatusReason { get; set; }
        [JsonPropertyName("sourceStorageId")] public int SourceStorageId { get; set; }
        [JsonPropertyName("sourceStorageName")] public string? SourceStorageName { get; set; }
        [JsonPropertyName("sourceStructureId")] public int? SourceStructureId { get; set; }
        [JsonPropertyName("sourceStructureName")] public string? SourceStructureName { get; set; }
        /// <summary>The stops' storage names in visiting order, joined with " → ".</summary>
        [JsonPropertyName("destinations")] public string Destinations { get; set; } = string.Empty;
        [JsonPropertyName("unitsRequested")] public long UnitsRequested { get; set; }
        [JsonPropertyName("unitsLoaded")] public long UnitsLoaded { get; set; }
        [JsonPropertyName("unitsDelivered")] public long UnitsDelivered { get; set; }
        [JsonPropertyName("unitsReturned")] public long UnitsReturned { get; set; }
        [JsonPropertyName("createdAt")] public DateTime CreatedAt { get; set; }
        [JsonPropertyName("etaAt")] public DateTime? EtaAt { get; set; }
        [JsonPropertyName("completedAt")] public DateTime? CompletedAt { get; set; }
    }

    public class TransportCargoDto
    {
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("amount")] public long Amount { get; set; }
    }

    public class TransportStopDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("sequence")] public int Sequence { get; set; }
        [JsonPropertyName("storageId")] public int StorageId { get; set; }
        [JsonPropertyName("storage")] public NamedRefDto? Storage { get; set; }
        [JsonPropertyName("structureId")] public int? StructureId { get; set; }
        [JsonPropertyName("structure")] public NamedRefDto? Structure { get; set; }
        [JsonPropertyName("status")] public string Status { get; set; } = string.Empty;
        [JsonPropertyName("reroutedFromStopId")] public int? ReroutedFromStopId { get; set; }
        [JsonPropertyName("distanceBlocks")] public decimal? DistanceBlocks { get; set; }
        [JsonPropertyName("distanceSource")] public string? DistanceSource { get; set; }
        [JsonPropertyName("etaAt")] public DateTime? EtaAt { get; set; }
        [JsonPropertyName("arrivedAt")] public DateTime? ArrivedAt { get; set; }
        [JsonPropertyName("completedAt")] public DateTime? CompletedAt { get; set; }
        [JsonPropertyName("attempts")] public int Attempts { get; set; }
        [JsonPropertyName("lines")] public List<TransportStopLineDto> Lines { get; set; } = new();
    }

    public class TransportStopLineDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("itemBlueprint")] public NamedRefDto? ItemBlueprint { get; set; }
        [JsonPropertyName("planned")] public long Planned { get; set; }
        [JsonPropertyName("delivered")] public long Delivered { get; set; }
    }

    public class TransportOrderLineDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("itemBlueprint")] public NamedRefDto? ItemBlueprint { get; set; }
        [JsonPropertyName("requested")] public long Requested { get; set; }
        [JsonPropertyName("loaded")] public long Loaded { get; set; }
        [JsonPropertyName("delivered")] public long Delivered { get; set; }
        [JsonPropertyName("returned")] public long Returned { get; set; }
        [JsonPropertyName("lost")] public long Lost { get; set; }
    }

    public class TransportAssignmentDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("mode")] public string Mode { get; set; } = string.Empty;
        [JsonPropertyName("carrierStructureId")] public int? CarrierStructureId { get; set; }
        [JsonPropertyName("carrierStructure")] public NamedRefDto? CarrierStructure { get; set; }
        [JsonPropertyName("workers")] public int Workers { get; set; }
        [JsonPropertyName("speedBlocksPerSecond")] public decimal SpeedBlocksPerSecond { get; set; }
        [JsonPropertyName("capacity")] public long? Capacity { get; set; }
        [JsonPropertyName("assignedAt")] public DateTime AssignedAt { get; set; }
        [JsonPropertyName("releasedAt")] public DateTime? ReleasedAt { get; set; }
    }

    public class TransportEventDto
    {
        [JsonPropertyName("id")] public long Id { get; set; }
        [JsonPropertyName("at")] public DateTime At { get; set; }
        [JsonPropertyName("kind")] public string Kind { get; set; } = string.Empty;
        [JsonPropertyName("statusAfter")] public string StatusAfter { get; set; } = string.Empty;
        [JsonPropertyName("transportStopId")] public int? TransportStopId { get; set; }
        [JsonPropertyName("stockTransactionId")] public long? StockTransactionId { get; set; }
        [JsonPropertyName("message")] public string Message { get; set; } = string.Empty;
    }

    // ================= Manual orders and dry runs =================

    /// <summary>
    /// A manual (or quest) order: <c>POST api/TransportOrders</c>, and the same body for <c>POST api/TransportOrders/dry-run</c>.
    /// The cargo is the sum of the stops' lines. Without <see cref="CarrierStructureId"/> the source storage's structure
    /// carries when it is a production structure (one of its workers, § 7.7); otherwise a simulated carrier without a
    /// worker does.
    /// </summary>
    public class TransportOrderCreateDto
    {
        [JsonPropertyName("sourceStorageId")] public int SourceStorageId { get; set; }
        [JsonPropertyName("carrierStructureId")] public int? CarrierStructureId { get; set; }
        [JsonPropertyName("origin")] public string? Origin { get; set; }
        [JsonPropertyName("executeAfter")] public DateTime? ExecuteAfter { get; set; }
        [JsonPropertyName("note")] public string? Note { get; set; }
        /// <summary>A retry with the same key returns the order the first request made (no second order).</summary>
        [JsonPropertyName("clientKey")] public string? ClientKey { get; set; }
        [JsonPropertyName("stops")] public List<TransportStopRequestDto> Stops { get; set; } = new();
    }

    public class TransportStopRequestDto
    {
        [JsonPropertyName("storageId")] public int StorageId { get; set; }
        [JsonPropertyName("lines")] public List<TransportLineRequestDto> Lines { get; set; } = new();
    }

    public class TransportLineRequestDto
    {
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("amount")] public long Amount { get; set; }
    }

    public class TransportCancelDto
    {
        [JsonPropertyName("reason")] public string? Reason { get; set; }
    }

    /// <summary>What an order would do, without writing anything (a dry run, replaces <c>/test transportpreperation</c>).</summary>
    public class TransportPlanDto
    {
        [JsonPropertyName("valid")] public bool Valid { get; set; }
        /// <summary>Why the order can't be made (Valid = false).</summary>
        [JsonPropertyName("errors")] public List<string> Errors { get; set; } = new();
        /// <summary>It can be made, but: short stock, no free worker, an estimated route, no room at a stop, ...</summary>
        [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = new();
        [JsonPropertyName("sourceStorageId")] public int SourceStorageId { get; set; }
        [JsonPropertyName("sourceStructureId")] public int? SourceStructureId { get; set; }
        [JsonPropertyName("carrierStructureId")] public int? CarrierStructureId { get; set; }
        /// <summary>#890: the carrier's name.</summary>
        [JsonPropertyName("carrierStructureName")] public string? CarrierStructureName { get; set; }
        [JsonPropertyName("carrierCapacity")] public long? CarrierCapacity { get; set; }
        [JsonPropertyName("speedBlocksPerSecond")] public decimal SpeedBlocksPerSecond { get; set; }
        [JsonPropertyName("workersPresent")] public int? WorkersPresent { get; set; }
        [JsonPropertyName("workersTotal")] public int? WorkersTotal { get; set; }
        [JsonPropertyName("lines")] public List<TransportPlanLineDto> Lines { get; set; } = new();
        [JsonPropertyName("legs")] public List<TransportPlanLegDto> Legs { get; set; } = new();
        [JsonPropertyName("totalUnits")] public long TotalUnits { get; set; }
        [JsonPropertyName("totalSeconds")] public long TotalSeconds { get; set; }
    }

    public class TransportPlanLineDto
    {
        [JsonPropertyName("itemBlueprintId")] public int ItemBlueprintId { get; set; }
        [JsonPropertyName("itemBlueprintName")] public string? ItemBlueprintName { get; set; }
        [JsonPropertyName("requested")] public long Requested { get; set; }
        /// <summary>What the source holds now (what loading would take, at most the request).</summary>
        [JsonPropertyName("available")] public long Available { get; set; }
    }

    public class TransportPlanLegDto
    {
        [JsonPropertyName("sequence")] public int Sequence { get; set; }
        [JsonPropertyName("fromStructureId")] public int? FromStructureId { get; set; }
        [JsonPropertyName("toStructureId")] public int? ToStructureId { get; set; }
        [JsonPropertyName("toStorageId")] public int ToStorageId { get; set; }
        [JsonPropertyName("toStorageName")] public string? ToStorageName { get; set; }
        [JsonPropertyName("distanceBlocks")] public decimal DistanceBlocks { get; set; }
        [JsonPropertyName("distanceSource")] public string DistanceSource { get; set; } = string.Empty;
        [JsonPropertyName("seconds")] public long Seconds { get; set; }
        [JsonPropertyName("lines")] public List<TransportLineRequestDto> Lines { get; set; } = new();
        /// <summary>Raw units the stop's storage has room for now (null = uncapped).</summary>
        [JsonPropertyName("roomUnits")] public decimal? RoomUnits { get; set; }
    }

    /// <summary>
    /// A production structure's transport (<c>GET api/ProductionStructures/{id}/transport</c>): the automatic trigger's
    /// dry run (why it fires or is held, how it would size the order and where it would go), its open orders and the
    /// workers away. Writes nothing.
    /// </summary>
    public class TransportStructureViewDto
    {
        [JsonPropertyName("productionStructureId")] public int ProductionStructureId { get; set; }
        [JsonPropertyName("featureEnabled")] public bool FeatureEnabled { get; set; }
        [JsonPropertyName("automaticTransport")] public bool AutomaticTransport { get; set; }
        [JsonPropertyName("roadConnection")] public string RoadConnection { get; set; } = string.Empty;
        [JsonPropertyName("isRural")] public bool IsRural { get; set; }
        [JsonPropertyName("effectiveTownId")] public int? EffectiveTownId { get; set; }
        [JsonPropertyName("outputStorageId")] public int? OutputStorageId { get; set; }
        [JsonPropertyName("outputUnits")] public decimal OutputUnits { get; set; }
        [JsonPropertyName("outputMaxUnits")] public long? OutputMaxUnits { get; set; }
        [JsonPropertyName("threshold")] public decimal Threshold { get; set; }
        [JsonPropertyName("carrierCapacity")] public long CarrierCapacity { get; set; }
        [JsonPropertyName("workersPresent")] public int WorkersPresent { get; set; }
        [JsonPropertyName("workersTotal")] public int WorkersTotal { get; set; }
        [JsonPropertyName("workersAway")] public int WorkersAway { get; set; }
        /// <summary>Dashboard warnings: no road connection (D5), a full Output storage, no warehouse, no workers.</summary>
        [JsonPropertyName("warnings")] public List<string> Warnings { get; set; } = new();
        /// <summary>Why an automatic order would fire now (occupancy, a line blocked full, the distinct cap).</summary>
        [JsonPropertyName("triggerReasons")] public List<string> TriggerReasons { get; set; } = new();
        /// <summary>Why no automatic order would be made now.</summary>
        [JsonPropertyName("heldReasons")] public List<string> HeldReasons { get; set; } = new();
        /// <summary>The order the trigger would make now (null when held or not triggered).</summary>
        [JsonPropertyName("wouldCreate")] public TransportPlanDto? WouldCreate { get; set; }
        [JsonPropertyName("destinationWarehouseId")] public int? DestinationWarehouseId { get; set; }
        /// <summary>#890: the destination warehouse's name.</summary>
        [JsonPropertyName("destinationWarehouseName")] public string? DestinationWarehouseName { get; set; }
        [JsonPropertyName("openOrders")] public List<TransportOrderListDto> OpenOrders { get; set; } = new();
    }

    /// <summary>The result of one transport sweep (<c>POST api/TransportOrders/run-now</c>).</summary>
    public class TransportSweepResultDto
    {
        [JsonPropertyName("featureEnabled")] public bool FeatureEnabled { get; set; }
        [JsonPropertyName("ranAt")] public DateTime RanAt { get; set; }
        [JsonPropertyName("ordersCreated")] public int OrdersCreated { get; set; }
        [JsonPropertyName("ordersAdvanced")] public int OrdersAdvanced { get; set; }
        [JsonPropertyName("ordersFailed")] public int OrdersFailed { get; set; }
    }

    // ================= Lanes =================

    public class TransportLaneDto
    {
        [JsonPropertyName("id")] public int? Id { get; set; }
        [JsonPropertyName("fromStructureId")] public int? FromStructureId { get; set; }
        [JsonPropertyName("fromStructure")] public NamedRefDto? FromStructure { get; set; }
        [JsonPropertyName("toStructureId")] public int? ToStructureId { get; set; }
        [JsonPropertyName("toStructure")] public NamedRefDto? ToStructure { get; set; }
        [JsonPropertyName("reachable")] public bool? Reachable { get; set; }
        [JsonPropertyName("roadDistance")] public decimal? RoadDistance { get; set; }
        [JsonPropertyName("straightDistance")] public decimal? StraightDistance { get; set; }
        [JsonPropertyName("routeEdgeIds")] public List<long>? RouteEdgeIds { get; set; }
        [JsonPropertyName("measuredSeconds")] public int? MeasuredSeconds { get; set; }
        [JsonPropertyName("source")] public string? Source { get; set; }
        [JsonPropertyName("measuredAt")] public DateTime? MeasuredAt { get; set; }
        [JsonPropertyName("note")] public string? Note { get; set; }
    }

    public class TransportLaneListDto
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("fromStructureId")] public int FromStructureId { get; set; }
        [JsonPropertyName("fromStructureName")] public string? FromStructureName { get; set; }
        [JsonPropertyName("toStructureId")] public int ToStructureId { get; set; }
        [JsonPropertyName("toStructureName")] public string? ToStructureName { get; set; }
        [JsonPropertyName("reachable")] public bool Reachable { get; set; }
        [JsonPropertyName("roadDistance")] public decimal? RoadDistance { get; set; }
        [JsonPropertyName("straightDistance")] public decimal? StraightDistance { get; set; }
        [JsonPropertyName("source")] public string Source { get; set; } = string.Empty;
        [JsonPropertyName("measuredAt")] public DateTime MeasuredAt { get; set; }
    }

    /// <summary>The lanes the game server reports (<c>POST api/TransportLanes/report</c>, P5b #681): upserted by pair.</summary>
    public class TransportLaneReportDto
    {
        [JsonPropertyName("lanes")] public List<TransportLaneDto> Lanes { get; set; } = new();
    }

    public class TransportLaneReportResultDto
    {
        [JsonPropertyName("created")] public int Created { get; set; }
        [JsonPropertyName("updated")] public int Updated { get; set; }
    }

    /// <summary>One pair the game server should measure (<c>GET api/TransportLanes/due</c>), with both positions.</summary>
    public class TransportLaneDueDto
    {
        [JsonPropertyName("fromStructureId")] public int FromStructureId { get; set; }
        [JsonPropertyName("fromStructureName")] public string? FromStructureName { get; set; }
        /// <summary>#890: <c>Producer</c> (a production structure) or <c>Warehouse</c>.</summary>
        [JsonPropertyName("fromKind")] public string FromKind { get; set; } = string.Empty;
        [JsonPropertyName("from")] public TransportPointDto? From { get; set; }
        [JsonPropertyName("toStructureId")] public int ToStructureId { get; set; }
        [JsonPropertyName("toStructureName")] public string? ToStructureName { get; set; }
        /// <summary>#890: always <c>Warehouse</c> today (pairs end at a warehouse).</summary>
        [JsonPropertyName("toKind")] public string ToKind { get; set; } = string.Empty;
        [JsonPropertyName("to")] public TransportPointDto? To { get; set; }
        /// <summary>Missing (never measured) or Stale (older than laneMaxAgeHours).</summary>
        [JsonPropertyName("reason")] public string Reason { get; set; } = string.Empty;
        [JsonPropertyName("lastMeasuredAt")] public DateTime? LastMeasuredAt { get; set; }
    }

    public class TransportPointDto
    {
        [JsonPropertyName("world")] public string? World { get; set; }
        [JsonPropertyName("x")] public double X { get; set; }
        [JsonPropertyName("y")] public double Y { get; set; }
        [JsonPropertyName("z")] public double Z { get; set; }
    }
}
