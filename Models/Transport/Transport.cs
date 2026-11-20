using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// Transport core P5 (PandiO/knk-workspace#79, production DESIGN § 7). V1 carriers are simulated (G14): one of the
// producer's own workers carries the order and the trip takes route length ÷ walking speed; robbery is off (D6).
// Cargo exists only in the order's Transit storage (§ 7.2); every move goes through IStockService.

/// <summary>
/// One transport (DESIGN § 7.1): goods from a source storage to one or more stops, through the order's own
/// <see cref="CargoStorageId"/> (a <c>Transit</c> storage, no holder, uncapped). Moved along its lifecycle by the
/// transport sweep (<see cref="NextActionAt"/>, indexed) under the order's row lock, so a restart picks it up where it
/// was. Written by the transport service only; staff create manual orders and cancel.
/// </summary>
[FormConfigurableEntity("TransportOrder")]
public class TransportOrder
{
    public const int StatusReasonMaxLength = 500;
    public const int NoteMaxLength = 500;
    public const int ClientKeyMaxLength = 80;

    public int Id { get; set; }

    public TransportOrigin Origin { get; set; }

    public TransportStatus Status { get; set; } = TransportStatus.Planned;

    /// <summary>Why the order waits, re-routed, returned or was cancelled (shown on the order's page).</summary>
    [MaxLength(StatusReasonMaxLength)]
    public string? StatusReason { get; set; }

    [NavigationPair(nameof(SourceStorage))]
    [RelatedEntityField(typeof(Storage))]
    public int SourceStorageId { get; set; }
    [RelatedEntityField(typeof(Storage))]
    public Storage SourceStorage { get; set; } = null!;

    /// <summary>The structure the cargo starts from (the source storage's holder when it is a structure); used for lanes.</summary>
    [NavigationPair(nameof(SourceStructure))]
    [RelatedEntityField(typeof(Structure))]
    public int? SourceStructureId { get; set; }
    [RelatedEntityField(typeof(Structure))]
    public Structure? SourceStructure { get; set; }

    /// <summary>The order's Transit storage: the only place its cargo exists while it travels (§ 7.2).</summary>
    [NavigationPair(nameof(CargoStorage))]
    [RelatedEntityField(typeof(Storage))]
    public int CargoStorageId { get; set; }
    [RelatedEntityField(typeof(Storage))]
    public Storage CargoStorage { get; set; } = null!;

    /// <summary>The production structure one of whose workers carries the order (§ 7.7); null = a simulated carrier
    /// without a worker (a staff order from a storage outside a production structure).</summary>
    [NavigationPair(nameof(CarrierStructure))]
    [RelatedEntityField(typeof(ProductionStructure))]
    public int? CarrierStructureId { get; set; }
    [RelatedEntityField(typeof(ProductionStructure))]
    public ProductionStructure? CarrierStructure { get; set; }

    /// <summary>Raw units the carrier can take (the worker's capacity, § 7.7); null = unbounded (staff orders without a worker).</summary>
    public long? CarrierCapacity { get; set; }

    /// <summary>Route risk (§ 7.5). [Later]: always null in V1 (D6).</summary>
    public decimal? Risk { get; set; }

    /// <summary>The structure where the carrier is now, or that it left last (lanes start here).</summary>
    public int? CurrentStructureId { get; set; }

    /// <summary>The stop the carrier is heading for or standing at; null before departure and after the last stop.</summary>
    public int? CurrentStopId { get; set; }

    public int Reroutes { get; set; }

    /// <summary>Tries to put the cargo back into the source (each one a ledger transaction with its own idempotency key).</summary>
    public int ReturnAttempts { get; set; }

    /// <summary>Set when staff cancelled a loaded order: it returns its cargo and ends as Returned.</summary>
    public bool CancelRequested { get; set; }

    /// <summary>Not before this time (manual orders may be scheduled).</summary>
    public DateTime ExecuteAfter { get; set; }

    /// <summary>When the sweep looks at the order next (indexed with the status, V2 defect 10); null once it is final.</summary>
    public DateTime? NextActionAt { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime? DepartedAt { get; set; }

    /// <summary>When the current leg started (departure, leaving a stop, turning back).</summary>
    public DateTime? LegStartedAt { get; set; }

    /// <summary>Arrival at the next stop (or back at the source while returning).</summary>
    public DateTime? EtaAt { get; set; }

    /// <summary>Since when a stop or the source has had no room (AwaitingSpace, ReturnBlocked).</summary>
    public DateTime? WaitingSince { get; set; }

    public DateTime? CompletedAt { get; set; }

    public int? CreatedByUserId { get; set; }

    [MaxLength(NoteMaxLength)]
    public string? Note { get; set; }

    /// <summary>A caller's key for a manual or quest order: a retry with the same key returns the first order.</summary>
    [MaxLength(ClientKeyMaxLength)]
    public string? ClientKey { get; set; }

    [RelatedEntityField(typeof(TransportStop))]
    public ICollection<TransportStop> Stops { get; set; } = new List<TransportStop>();

    [RelatedEntityField(typeof(TransportOrderLine))]
    public ICollection<TransportOrderLine> Lines { get; set; } = new List<TransportOrderLine>();

    [RelatedEntityField(typeof(TransportAssignment))]
    public ICollection<TransportAssignment> Assignments { get; set; } = new List<TransportAssignment>();

    [RelatedEntityField(typeof(TransportEvent))]
    public ICollection<TransportEvent> Events { get; set; } = new List<TransportEvent>();

    public static bool IsFinal(TransportStatus s) =>
        s is TransportStatus.Delivered or TransportStatus.Returned or TransportStatus.Cancelled or TransportStatus.Lost;

    public static bool IsLoaded(TransportStatus s) =>
        s is TransportStatus.InTransit or TransportStatus.AtStop or TransportStatus.AwaitingSpace
            or TransportStatus.Returning or TransportStatus.ReturnBlocked;
}

/// <summary>One destination of an order with its own planned amounts per blueprint (DESIGN § 7.1, V2 defect 3).</summary>
[FormConfigurableEntity("TransportStop")]
public class TransportStop
{
    public int Id { get; set; }

    public int TransportOrderId { get; set; }
    public TransportOrder TransportOrder { get; set; } = null!;

    /// <summary>1, 2, ...: the order the carrier visits the stops in.</summary>
    public int Sequence { get; set; }

    [NavigationPair(nameof(Storage))]
    [RelatedEntityField(typeof(Storage))]
    public int StorageId { get; set; }
    [RelatedEntityField(typeof(Storage))]
    public Storage Storage { get; set; } = null!;

    /// <summary>The storage's structure (its holder), used for lanes; null for a storage outside a structure.</summary>
    [NavigationPair(nameof(Structure))]
    [RelatedEntityField(typeof(Structure))]
    public int? StructureId { get; set; }
    [RelatedEntityField(typeof(Structure))]
    public Structure? Structure { get; set; }

    public TransportStopStatus Status { get; set; } = TransportStopStatus.Pending;

    /// <summary>Set on a stop the order was re-routed to: the stop it took the remaining cargo from.</summary>
    public int? ReroutedFromStopId { get; set; }

    /// <summary>The leg to this stop, in blocks, and where the number came from.</summary>
    public decimal? DistanceBlocks { get; set; }
    public TransportDistanceSource? DistanceSource { get; set; }

    public DateTime? EtaAt { get; set; }
    public DateTime? ArrivedAt { get; set; }
    public DateTime? CompletedAt { get; set; }

    /// <summary>Delivery tries at this stop (each one a ledger transaction with its own idempotency key).</summary>
    public int Attempts { get; set; }

    [RelatedEntityField(typeof(TransportStopLine))]
    public ICollection<TransportStopLine> Lines { get; set; } = new List<TransportStopLine>();
}

/// <summary>The planned amount of one blueprint for one stop, and what was delivered there.</summary>
[FormConfigurableEntity("TransportStopLine")]
public class TransportStopLine
{
    public int Id { get; set; }

    public int TransportStopId { get; set; }
    public TransportStop TransportStop { get; set; } = null!;

    [NavigationPair(nameof(ItemBlueprint))]
    [RelatedEntityField(typeof(ItemBlueprint))]
    public int ItemBlueprintId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint ItemBlueprint { get; set; } = null!;

    public long Planned { get; set; }
    public long Delivered { get; set; }
}

/// <summary>The cargo of one blueprint: requested, loaded, delivered, returned and lost (DESIGN § 7.1).
/// <c>Loaded = Delivered + Returned + Lost + what is still in transit</c>.</summary>
[FormConfigurableEntity("TransportOrderLine")]
public class TransportOrderLine
{
    public int Id { get; set; }

    public int TransportOrderId { get; set; }
    public TransportOrder TransportOrder { get; set; } = null!;

    [NavigationPair(nameof(ItemBlueprint))]
    [RelatedEntityField(typeof(ItemBlueprint))]
    public int ItemBlueprintId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint ItemBlueprint { get; set; } = null!;

    public long Requested { get; set; }
    public long Loaded { get; set; }
    public long Delivered { get; set; }
    public long Returned { get; set; }
    public long Lost { get; set; }
}

/// <summary>
/// Who carries an order (DESIGN § 7.3): in V1 a simulated carrier, normally one worker of
/// <see cref="CarrierStructureId"/> (§ 7.7), who is away from <see cref="AssignedAt"/> until <see cref="ReleasedAt"/>.
/// Without a carrier structure (a staff order from a storage outside a production structure) nobody is away.
/// </summary>
[FormConfigurableEntity("TransportAssignment")]
public class TransportAssignment
{
    public int Id { get; set; }

    public int TransportOrderId { get; set; }
    public TransportOrder TransportOrder { get; set; } = null!;

    public TransportCarrierMode Mode { get; set; } = TransportCarrierMode.Simulated;

    [NavigationPair(nameof(CarrierStructure))]
    [RelatedEntityField(typeof(ProductionStructure))]
    public int? CarrierStructureId { get; set; }
    [RelatedEntityField(typeof(ProductionStructure))]
    public ProductionStructure? CarrierStructure { get; set; }

    /// <summary>1 when one of the carrier structure's workers carries, else 0.</summary>
    public int Workers { get; set; }

    public decimal SpeedBlocksPerSecond { get; set; }

    public long? Capacity { get; set; }

    public DateTime AssignedAt { get; set; }
    public DateTime? ReleasedAt { get; set; }

    public bool IsActive => ReleasedAt == null;
}

/// <summary>One entry in an order's log (DESIGN § 7.1): what happened, when, where, and the ledger transaction.</summary>
[FormConfigurableEntity("TransportEvent")]
public class TransportEvent
{
    public const int MessageMaxLength = 500;

    public long Id { get; set; }

    public int TransportOrderId { get; set; }
    public TransportOrder TransportOrder { get; set; } = null!;

    public DateTime At { get; set; }

    public TransportEventKind Kind { get; set; }

    public TransportStatus StatusAfter { get; set; }

    public int? TransportStopId { get; set; }

    public long? StockTransactionId { get; set; }

    [MaxLength(MessageMaxLength)]
    public string Message { get; set; } = string.Empty;
}

/// <summary>
/// The measured route between two structures (DESIGN § 7.6): road distance, straight distance and the road graph's
/// edge ids. The game server measures lanes with <c>AStarRouter</c> over the road snapshot (P5b, #681); staff can type
/// one in. Read in both directions. Simulated ETA = road distance ÷ walking speed (G14).
/// </summary>
[FormConfigurableEntity("TransportLane")]
public class TransportLane
{
    public const int EdgeIdsMaxLength = 8000;
    public const int NoteMaxLength = 300;

    public int Id { get; set; }

    [NavigationPair(nameof(FromStructure))]
    [RelatedEntityField(typeof(Structure))]
    public int FromStructureId { get; set; }
    [RelatedEntityField(typeof(Structure))]
    public Structure FromStructure { get; set; } = null!;

    [NavigationPair(nameof(ToStructure))]
    [RelatedEntityField(typeof(Structure))]
    public int ToStructureId { get; set; }
    [RelatedEntityField(typeof(Structure))]
    public Structure ToStructure { get; set; } = null!;

    /// <summary>False when the router found no road between them: the lane is not used for routes.</summary>
    public bool Reachable { get; set; } = true;

    /// <summary>Length of the road route in blocks; null when not reachable.</summary>
    public decimal? RoadDistance { get; set; }

    /// <summary>Straight distance between the two structures in blocks.</summary>
    public decimal? StraightDistance { get; set; }

    /// <summary>A JSON list of the route's road edge ids.</summary>
    [MaxLength(EdgeIdsMaxLength)]
    public string? RouteEdgeIdsJson { get; set; }

    /// <summary>A walked time in seconds, when one was measured (shown; the ETA uses the distance).</summary>
    public int? MeasuredSeconds { get; set; }

    public TransportLaneSource Source { get; set; } = TransportLaneSource.Manual;

    public DateTime MeasuredAt { get; set; }

    [MaxLength(NoteMaxLength)]
    public string? Note { get; set; }
}
