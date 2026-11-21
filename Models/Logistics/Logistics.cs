using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// Town logistics P9 (PandiO/knk-workspace#127, TOWN_LOGISTICS.md § 2-5, production DESIGN § 7.4 "Distribution").
// Warehouses reserve, consumers collect with their own worker (D-L1); the planner runs every 10 minutes per town with
// dry runs (D-L2); default policy templates are edited by admins (D-L3). V1 rule types: reserves, priority classes,
// service areas and replenishment defaults; the Later rules are #324. Behind Production:Enabled (the planner only).

/// <summary>
/// One version of a logistics policy (TOWN_LOGISTICS § 4, § 4.1): a town's, a warehouse's, or a default template. A
/// version is never edited: a save makes version n + 1 with its own rules and marks this one superseded, so "since
/// Tuesday the bread reserve is 14 days" stays readable. <see cref="CurrentKey"/> (unique, null once superseded) keeps one
/// current version per town, warehouse or template lineage, also with two API instances.
/// </summary>
[FormConfigurableEntity("LogisticsPolicy")]
public class LogisticsPolicy
{
    public const int NameMaxLength = 100;
    public const int ReasonMaxLength = 500;
    public const int CurrentKeyMaxLength = 40;

    public int Id { get; set; }

    [MaxLength(NameMaxLength)]
    public string Name { get; set; } = null!;

    public LogisticsPolicyScope Scope { get; set; }

    /// <summary>The town (Town scope); null for warehouse policies and templates.</summary>
    [NavigationPair(nameof(Town))]
    [RelatedEntityField(typeof(Town))]
    public int? TownId { get; set; }
    [RelatedEntityField(typeof(Town))]
    public Town? Town { get; set; }

    /// <summary>The warehouse (Warehouse scope).</summary>
    [NavigationPair(nameof(Warehouse))]
    [RelatedEntityField(typeof(Warehouse))]
    public int? WarehouseId { get; set; }
    [RelatedEntityField(typeof(Warehouse))]
    public Warehouse? Warehouse { get; set; }

    /// <summary>Templates: the town specialization it is made for (concept L144, "iron/wood-rich, fish/wheat-rich");
    /// null = a general template.</summary>
    public ResourceCategory? Specialization { get; set; }

    /// <summary>Templates: the town size it is made for (a label; towns pick a template when it is applied).</summary>
    public LogisticsTownSize? TownSize { get; set; }

    /// <summary>Templates: used by the planner for a town without a policy of its own (one current default).</summary>
    public bool IsDefaultTemplate { get; set; }

    /// <summary>1, 2, ... within the lineage.</summary>
    public int Version { get; set; } = 1;

    /// <summary>The id of version 1: every version of the same policy shares it.</summary>
    public int LineageId { get; set; }

    /// <summary>"town:10", "warehouse:5" or "template:7" while this is the current version; null once superseded.</summary>
    [MaxLength(CurrentKeyMaxLength)]
    public string? CurrentKey { get; set; }

    public bool IsCurrent => CurrentKey != null;

    /// <summary>The version this one replaced, or the template it was copied from.</summary>
    public int? BasedOnPolicyId { get; set; }

    public int? SupersededByPolicyId { get; set; }

    public DateTime? SupersededAt { get; set; }

    public LogisticsAuthor Author { get; set; } = LogisticsAuthor.Admin;

    /// <summary>Why this version was made (shown in the history).</summary>
    [MaxLength(ReasonMaxLength)]
    public string? Reason { get; set; }

    public DateTime CreatedAt { get; set; }

    public int? CreatedByUserId { get; set; }

    [RelatedEntityField(typeof(LogisticsRule))]
    public ICollection<LogisticsRule> Rules { get; set; } = new List<LogisticsRule>();

    public static string KeyFor(LogisticsPolicyScope scope, int? townId, int? warehouseId, int lineageId) => scope switch
    {
        LogisticsPolicyScope.Town => $"town:{townId}",
        LogisticsPolicyScope.Warehouse => $"warehouse:{warehouseId}",
        _ => $"template:{lineageId}",
    };
}

/// <summary>
/// One rule of a policy version (TOWN_LOGISTICS § 4): type, scope, target, parameters, active window, author and a
/// reason shown in the planner's explanation. Edited inside its policy (a save makes a new version).
/// </summary>
[FormConfigurableEntity("LogisticsRule")]
public class LogisticsRule
{
    public const int ReasonMaxLength = 300;
    public const long MaxUnits = 100_000_000;
    public const decimal MaxDays = 3650m;

    public int Id { get; set; }

    [NavigationPair(nameof(LogisticsPolicy))]
    [RelatedEntityField(typeof(LogisticsPolicy))]
    public int LogisticsPolicyId { get; set; }
    [RelatedEntityField(typeof(LogisticsPolicy))]
    public LogisticsPolicy LogisticsPolicy { get; set; } = null!;

    /// <summary>1, 2, ...: the order the editor shows.</summary>
    public int Sequence { get; set; }

    public LogisticsRuleType Type { get; set; }

    public LogisticsRuleScope Scope { get; set; } = LogisticsRuleScope.Town;

    /// <summary>Reserve with Warehouse scope: the warehouse it keeps stock in. ServiceArea: the warehouse that serves.</summary>
    [NavigationPair(nameof(Warehouse))]
    [RelatedEntityField(typeof(Warehouse))]
    public int? WarehouseId { get; set; }
    [RelatedEntityField(typeof(Warehouse))]
    public Warehouse? Warehouse { get; set; }

    // ---- Target items (Reserve, PriorityClass, Replenishment) ----

    public LogisticsTargetKind TargetKind { get; set; } = LogisticsTargetKind.Any;

    [NavigationPair(nameof(ItemBlueprint))]
    [RelatedEntityField(typeof(ItemBlueprint))]
    public int? ItemBlueprintId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint? ItemBlueprint { get; set; }

    [NavigationPair(nameof(Category))]
    [RelatedEntityField(typeof(Category))]
    public int? CategoryId { get; set; }
    [RelatedEntityField(typeof(Category))]
    public Category? Category { get; set; }

    [NavigationPair(nameof(Tag))]
    [RelatedEntityField(typeof(Tag))]
    public int? TagId { get; set; }
    [RelatedEntityField(typeof(Tag))]
    public Tag? Tag { get; set; }

    // ---- Target consumers (PriorityClass, Replenishment) ----

    public LogisticsConsumerKind ConsumerKind { get; set; } = LogisticsConsumerKind.Any;

    /// <summary>ConsumerKind Structure, or a ServiceArea rule for one structure.</summary>
    [NavigationPair(nameof(Structure))]
    [RelatedEntityField(typeof(Structure))]
    public int? StructureId { get; set; }
    [RelatedEntityField(typeof(Structure))]
    public Structure? Structure { get; set; }

    [NavigationPair(nameof(ProductionKind))]
    [RelatedEntityField(typeof(ProductionKind))]
    public int? ProductionKindId { get; set; }
    [RelatedEntityField(typeof(ProductionKind))]
    public ProductionKind? ProductionKind { get; set; }

    // ---- Service area (ServiceArea) ----

    public LogisticsAreaKind? AreaKind { get; set; }

    [NavigationPair(nameof(District))]
    [RelatedEntityField(typeof(District))]
    public int? DistrictId { get; set; }
    [RelatedEntityField(typeof(District))]
    public District? District { get; set; }

    // ---- Parameters (raw units, DESIGN § 4.4) ----

    /// <summary>Reserve: units to keep.</summary>
    public long? Units { get; set; }

    /// <summary>Reserve: days of cover to keep (× the average daily consumption).</summary>
    public decimal? DaysOfCover { get; set; }

    /// <summary>Replenishment: reserve when stock falls to this.</summary>
    public long? ReorderPoint { get; set; }

    /// <summary>Replenishment: reserve up to this.</summary>
    public long? TargetLevel { get; set; }

    /// <summary>PriorityClass: the class.</summary>
    public LogisticsPriorityClass? PriorityClass { get; set; }

    // ---- Window, author, reason ----

    public DateTime? ActiveFrom { get; set; }
    public DateTime? ActiveUntil { get; set; }

    public LogisticsAuthor Author { get; set; } = LogisticsAuthor.Admin;

    [MaxLength(ReasonMaxLength)]
    public string? Reason { get; set; }

    public bool IsActiveAt(DateTime now) => (ActiveFrom == null || ActiveFrom <= now) && (ActiveUntil == null || ActiveUntil > now);
}

/// <summary>
/// A consumer's replenishment setting (TOWN_LOGISTICS § 3): min/max for one blueprint in one of its Input or Merchandise
/// storages, and an optional priority class. When stock (with what is on the way) falls to the reorder point, the
/// planner reserves up to the target level. Without a setting the planner uses the policy's Replenishment rules, then
/// the structure's production line inputs (game setting <c>production.logistics</c>).
/// </summary>
[FormConfigurableEntity("ConsumerReplenishment")]
public class ConsumerReplenishment
{
    public const int NoteMaxLength = 300;

    public int Id { get; set; }

    /// <summary>The consumer's Input or Merchandise storage.</summary>
    [NavigationPair(nameof(Storage))]
    [RelatedEntityField(typeof(Storage))]
    public int StorageId { get; set; }
    [RelatedEntityField(typeof(Storage))]
    public Storage Storage { get; set; } = null!;

    /// <summary>The structure that holds the storage (set from the storage on save).</summary>
    [NavigationPair(nameof(Structure))]
    [RelatedEntityField(typeof(Structure))]
    public int StructureId { get; set; }
    [RelatedEntityField(typeof(Structure))]
    public Structure Structure { get; set; } = null!;

    [NavigationPair(nameof(ItemBlueprint))]
    [RelatedEntityField(typeof(ItemBlueprint))]
    public int ItemBlueprintId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint ItemBlueprint { get; set; } = null!;

    /// <summary>Min: reserve when stock (with what is on the way) falls to this.</summary>
    public long ReorderPoint { get; set; }

    /// <summary>Max: reserve up to this.</summary>
    public long TargetLevel { get; set; }

    /// <summary>Null = the policy's priority rules (Normal without one).</summary>
    public LogisticsPriorityClass? PriorityClass { get; set; }

    public bool Enabled { get; set; } = true;

    [MaxLength(NoteMaxLength)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}

/// <summary>
/// A claim on warehouse stock for one consumer storage (TOWN_LOGISTICS § 5.1): it holds stock without moving it, can't
/// be taken twice (<see cref="OpenKey"/> is unique while open) and is released at the first planner run after
/// <see cref="ExpiresAt"/> when the consumer's worker hasn't collected it (F11). Written by the planner only.
/// </summary>
[FormConfigurableEntity("LogisticsReservation")]
public class LogisticsReservation
{
    public const int ReasonMaxLength = 500;
    public const int OpenKeyMaxLength = 40;

    public int Id { get; set; }

    [NavigationPair(nameof(Town))]
    [RelatedEntityField(typeof(Town))]
    public int TownId { get; set; }
    [RelatedEntityField(typeof(Town))]
    public Town Town { get; set; } = null!;

    [NavigationPair(nameof(Warehouse))]
    [RelatedEntityField(typeof(Warehouse))]
    public int WarehouseId { get; set; }
    [RelatedEntityField(typeof(Warehouse))]
    public Warehouse Warehouse { get; set; } = null!;

    [NavigationPair(nameof(WarehouseStorage))]
    [RelatedEntityField(typeof(Storage))]
    public int WarehouseStorageId { get; set; }
    [RelatedEntityField(typeof(Storage))]
    public Storage WarehouseStorage { get; set; } = null!;

    [NavigationPair(nameof(ConsumerStructure))]
    [RelatedEntityField(typeof(Structure))]
    public int ConsumerStructureId { get; set; }
    [RelatedEntityField(typeof(Structure))]
    public Structure ConsumerStructure { get; set; } = null!;

    [NavigationPair(nameof(ConsumerStorage))]
    [RelatedEntityField(typeof(Storage))]
    public int ConsumerStorageId { get; set; }
    [RelatedEntityField(typeof(Storage))]
    public Storage ConsumerStorage { get; set; } = null!;

    [NavigationPair(nameof(ItemBlueprint))]
    [RelatedEntityField(typeof(ItemBlueprint))]
    public int ItemBlueprintId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint ItemBlueprint { get; set; } = null!;

    public long Amount { get; set; }

    /// <summary>What the worker loaded (set when Collected).</summary>
    public long Collected { get; set; }

    public LogisticsReservationStatus Status { get; set; } = LogisticsReservationStatus.Open;

    public LogisticsPriorityClass PriorityClass { get; set; } = LogisticsPriorityClass.Normal;

    /// <summary>"{consumer storage}:{blueprint}" while Open, null after: one open reservation per consumer storage and blueprint.</summary>
    [MaxLength(OpenKeyMaxLength)]
    public string? OpenKey { get; set; }

    /// <summary>The collection order (origin Distribution) the consumer's worker carries.</summary>
    [NavigationPair(nameof(TransportOrder))]
    [RelatedEntityField(typeof(TransportOrder))]
    public int? TransportOrderId { get; set; }
    [RelatedEntityField(typeof(TransportOrder))]
    public TransportOrder? TransportOrder { get; set; }

    public int? PlannerRunId { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public DateTime? ClosedAt { get; set; }

    /// <summary>The planner's explanation (which rule, which shortfall, which priority).</summary>
    [MaxLength(ReasonMaxLength)]
    public string? Explanation { get; set; }

    /// <summary>Why it was released or cancelled.</summary>
    [MaxLength(ReasonMaxLength)]
    public string? StatusReason { get; set; }

    public static string KeyFor(int consumerStorageId, int blueprintId) => $"{consumerStorageId}:{blueprintId}";
}

/// <summary>One run of a town's planner (TOWN_LOGISTICS § 5): when, which policy version, and what it did. Its entries
/// are the plan explanation per consumer storage and blueprint with demand. Dry runs are not stored.</summary>
[FormConfigurableEntity("LogisticsPlannerRun")]
public class LogisticsPlannerRun
{
    public const int SummaryMaxLength = 1000;

    public int Id { get; set; }

    [NavigationPair(nameof(Town))]
    [RelatedEntityField(typeof(Town))]
    public int TownId { get; set; }
    [RelatedEntityField(typeof(Town))]
    public Town Town { get; set; } = null!;

    /// <summary>The policy version the run used (the town's, else the default template); null = no policy.</summary>
    public int? LogisticsPolicyId { get; set; }

    public int? PolicyVersion { get; set; }

    public LogisticsRunTrigger Trigger { get; set; }

    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }

    public int DemandLines { get; set; }
    public int ReservationsCreated { get; set; }
    public int OrdersCreated { get; set; }
    public int ReservationsReleased { get; set; }
    public int ReservationsCollected { get; set; }

    /// <summary>Demand lines that got less than they needed.</summary>
    public int Shortfalls { get; set; }

    [MaxLength(SummaryMaxLength)]
    public string? Summary { get; set; }

    public int? CreatedByUserId { get; set; }

    [RelatedEntityField(typeof(LogisticsPlanEntry))]
    public ICollection<LogisticsPlanEntry> Entries { get; set; } = new List<LogisticsPlanEntry>();
}

/// <summary>The planner's explanation for one consumer storage and blueprint in one run (TOWN_LOGISTICS § 5 step 6):
/// the shortfall, the priority, the warehouse, the rules that applied and the outcome. A log: plain ids, no FKs except
/// its run. Shown on the run's page.</summary>
[FormConfigurableEntity("LogisticsPlanEntry")]
public class LogisticsPlanEntry
{
    public const int ExplanationMaxLength = 1000;
    public const int RuleIdsMaxLength = 200;

    public long Id { get; set; }

    public int LogisticsPlannerRunId { get; set; }
    public LogisticsPlannerRun LogisticsPlannerRun { get; set; } = null!;

    public int ConsumerStructureId { get; set; }
    public int ConsumerStorageId { get; set; }
    public int ItemBlueprintId { get; set; }

    public int? WarehouseId { get; set; }

    public LogisticsPriorityClass PriorityClass { get; set; }

    /// <summary>Stock at the consumer plus what is on the way to it.</summary>
    public long Stock { get; set; }
    public long ReorderPoint { get; set; }
    public long TargetLevel { get; set; }
    public long Shortfall { get; set; }
    public long Reserved { get; set; }

    public LogisticsPlanOutcome Outcome { get; set; }

    /// <summary>Comma list of the rules that applied.</summary>
    [MaxLength(RuleIdsMaxLength)]
    public string? RuleIds { get; set; }

    public int? LogisticsReservationId { get; set; }
    public int? TransportOrderId { get; set; }

    [MaxLength(ExplanationMaxLength)]
    public string Explanation { get; set; } = string.Empty;
}
