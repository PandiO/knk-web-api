using System;
using System.ComponentModel.DataAnnotations;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// Player harvesting P8a (PandiO/knk-workspace#685, PRODUCTION_KINDS.md § 6-7, decision record § 3.6 and § 11 F9-F10).
// Harvesting counts as working for the town: the game server lets players break what a kind's harvest rules allow
// (L22, #134), reports each harvest, and the API splits it at once: the worker's share is paid to the player
// (SYS_PRODUCTION → SYS_WORLD), the rest goes into the structure's Output storage (SYS_PRODUCTION → Output), both as
// PlayerHarvest moves. Each harvest draws from the area's shared ResourcePool, whose fill slows the structure down to
// its kind's floor (0.5, P-D6) until regeneration refills it. Block regeneration jobs themselves live on the game server
// (§ 7); the API keeps the per-rule regeneration settings and the pool's regeneration state.

/// <summary>
/// What players may harvest in a production kind's areas and what each break yields (PRODUCTION_KINDS § 6.1
/// <c>HarvestRule</c>). The game server evaluates the targets, the requirement (permission node or title bracket) and the
/// tool; the API owns the yield, the worker's share, the pool cost and the regeneration settings.
/// </summary>
[FormConfigurableEntity("HarvestRule")]
public class HarvestRule
{
    public const int NameMaxLength = 100;
    public const int TargetsMaxLength = 2000;
    public const int MaxTargets = 50;
    public const int TargetMaxLength = 64;
    public const int PermissionMaxLength = 150;
    public const int MaterialMaxLength = 64;
    public const int NoteMaxLength = 500;
    public const decimal DefaultWorkerShare = 0.25m;
    public const int DefaultRegenSeconds = 60;
    public const int MaxRegenSeconds = 7 * 24 * 3600;
    public const int MaxCooldownSeconds = 24 * 3600;
    public const decimal MaxYieldPerBreak = 64m;
    public const decimal MaxPoolCost = 1_000_000m;
    public const decimal MaxFortuneBonusPerLevel = 1m;

    /// <summary>The legacy V1 cap: fortune counts up to level IV.</summary>
    public const int MaxFortuneLevel = 4;

    public int Id { get; set; }

    [MaxLength(NameMaxLength)]
    public string Name { get; set; } = null!;

    [NavigationPair(nameof(ProductionKind))]
    [RelatedEntityField(typeof(ProductionKind))]
    public int ProductionKindId { get; set; }
    [RelatedEntityField(typeof(ProductionKind))]
    public ProductionKind ProductionKind { get; set; } = null!;

    /// <summary>Off: the game server denies these breaks and the API refuses their reports.</summary>
    public bool Enabled { get; set; } = true;

    public HarvestTargetKind TargetKind { get; set; } = HarvestTargetKind.Block;

    /// <summary>A JSON list of material or entity type names; a name may start or end with <c>*</c>
    /// (<c>["COAL_ORE","DEEPSLATE_COAL_ORE"]</c>, <c>["*_LOG"]</c>, <c>["SHEEP"]</c>).</summary>
    [MaxLength(TargetsMaxLength)]
    public string TargetsJson { get; set; } = "[]";

    /// <summary>Crops: the lowest block age that may be harvested (wheat 7); null = any.</summary>
    public int? MinBlockAge { get; set; }

    /// <summary>The permission node a player needs (through the game server's requirement evaluator); null = none.</summary>
    [MaxLength(PermissionMaxLength)]
    public string? RequiredPermission { get; set; }

    /// <summary>The lowest title bracket a player needs; null = none.</summary>
    [NavigationPair(nameof(MinTitleBracket))]
    [RelatedEntityField(typeof(TitleBracket))]
    public int? MinTitleBracketId { get; set; }
    [RelatedEntityField(typeof(TitleBracket))]
    public TitleBracket? MinTitleBracket { get; set; }

    public HarvestToolClass ToolClass { get; set; } = HarvestToolClass.Any;

    public HarvestToolTier MinToolTier { get; set; } = HarvestToolTier.Any;

    /// <summary>What a break yields (DESIGN § 8: a blueprint item, never vanilla drops).</summary>
    [NavigationPair(nameof(YieldItemBlueprint))]
    [RelatedEntityField(typeof(ItemBlueprint))]
    public int YieldItemBlueprintId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint YieldItemBlueprint { get; set; } = null!;

    /// <summary>Units per break before fortune (legacy <c>baseDropAmount</c>).</summary>
    public decimal YieldPerBreak { get; set; } = 1m;

    /// <summary>Extra yield per fortune level, up to <see cref="MaxFortuneLevel"/>: yield × (1 + bonus × level). 0 = fortune
    /// does nothing.</summary>
    public decimal FortuneBonusPerLevel { get; set; }

    /// <summary>The worker's share of each harvest (§ 6.2), paid to the player; the rest goes to the structure. 0-1.</summary>
    public decimal WorkerShare { get; set; } = DefaultWorkerShare;

    /// <summary>Units drawn from the area's ResourcePool per break (§ 6.3).</summary>
    public decimal PoolCost { get; set; } = 1m;

    /// <summary>Per player per area (anti-farming), enforced by the game server; null = none.</summary>
    public int? CooldownSeconds { get; set; }

    /// <summary>What the game server leaves until the block regenerates (§ 7).</summary>
    public HarvestPlaceholder Placeholder { get; set; } = HarvestPlaceholder.DepletedBlock;

    /// <summary>The placeholder block for <see cref="HarvestPlaceholder.DepletedBlock"/> (STONE, NETHERRACK, …); null =
    /// the game server's default for the world.</summary>
    [MaxLength(MaterialMaxLength)]
    public string? PlaceholderMaterial { get; set; }

    /// <summary>How long until the block comes back (V1: crops and wood 25 s).</summary>
    public int RegenSeconds { get; set; } = DefaultRegenSeconds;

    /// <summary>On: the regeneration time grows as the pool empties, up to twice <see cref="RegenSeconds"/> at an empty
    /// pool (§ 7 "scaled by pool fill").</summary>
    public bool RegenScalesWithPool { get; set; }

    [MaxLength(NoteMaxLength)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A production area's shared pool of harvestable units (PRODUCTION_KINDS § 6.3). Player harvests lower
/// <see cref="Current"/> by their pool cost; regeneration adds <see cref="RegenPerHour"/> up to <see cref="Capacity"/>
/// (applied lazily: <see cref="LastRegenAt"/> is when it was last written). The structure's production reads the fill
/// through the built-in pool step: <c>floor + (1 − floor) × Current / Capacity</c>. The abstract production itself never
/// draws from the pool. One pool per area, made on the first harvest or by staff.
/// </summary>
[FormConfigurableEntity("ResourcePool")]
public class ResourcePool
{
    public const decimal MaxCapacity = 1_000_000_000m;

    public int Id { get; set; }

    [NavigationPair(nameof(ProductionArea))]
    [RelatedEntityField(typeof(ProductionArea))]
    public int ProductionAreaId { get; set; }
    [RelatedEntityField(typeof(ProductionArea))]
    public ProductionArea ProductionArea { get; set; } = null!;

    /// <summary>The structure of the area (kept for filters; the area's structure never changes).</summary>
    public int ProductionStructureId { get; set; }

    /// <summary>The harvestable units, from the latest survey's total (or typed in).</summary>
    public decimal Capacity { get; set; }

    /// <summary>The units left, as of <see cref="LastRegenAt"/>.</summary>
    public decimal Current { get; set; }

    public decimal RegenPerHour { get; set; }

    /// <summary>On: each new survey of the area sets <see cref="Capacity"/> to its total units (the fill is kept).</summary>
    public bool CapacityFromSurvey { get; set; } = true;

    /// <summary>The survey the capacity came from, if any (no FK: the history is its own table).</summary>
    public long? CapacitySurveyId { get; set; }

    public DateTime LastRegenAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// One reported harvest and how it was split (PRODUCTION_KINDS § 6.2): the worker's whole units paid to the player and
/// the town's whole units moved into the structure's Output storage, with the pool before and after. One row per
/// <see cref="IdempotencyKey"/> (unique), so a retried report books once. Kept as history; the stock ledger holds the
/// moves (<see cref="WorkerTransactionId"/>, <see cref="TownTransactionId"/>).
/// </summary>
[FormConfigurableEntity("PlayerHarvest")]
public class PlayerHarvest
{
    public const int KeyMaxLength = 100;
    public const int HashLength = 64;
    public const int ReasonMaxLength = 500;

    public long Id { get; set; }

    [MaxLength(KeyMaxLength)]
    public string IdempotencyKey { get; set; } = null!;

    /// <summary>SHA-256 of the entry; the same key with another entry is refused.</summary>
    [MaxLength(HashLength)]
    public string RequestHash { get; set; } = null!;

    [NavigationPair(nameof(User))]
    [RelatedEntityField(typeof(User))]
    public int UserId { get; set; }
    [RelatedEntityField(typeof(User))]
    public User User { get; set; } = null!;

    [NavigationPair(nameof(HarvestRule))]
    [RelatedEntityField(typeof(HarvestRule))]
    public int HarvestRuleId { get; set; }
    [RelatedEntityField(typeof(HarvestRule))]
    public HarvestRule HarvestRule { get; set; } = null!;

    /// <summary>No FK: the history outlives a deleted area or structure.</summary>
    public int ProductionAreaId { get; set; }

    public int ProductionStructureId { get; set; }

    /// <summary>The rule's yield blueprint at the time.</summary>
    public int ItemBlueprintId { get; set; }

    public int Breaks { get; set; }

    public int FortuneLevel { get; set; }

    /// <summary>When the game server saw the harvest.</summary>
    public DateTime HarvestedAt { get; set; }

    public DateTime ReceivedAt { get; set; }

    /// <summary>Breaks × yield per break × the fortune factor.</summary>
    public decimal YieldUnits { get; set; }

    /// <summary>The rule's worker share at the time.</summary>
    public decimal WorkerShare { get; set; }

    /// <summary>Whole units paid to the player (the game server gives them as drops).</summary>
    public long WorkerUnits { get; set; }

    /// <summary>Whole units of the town's share moved towards Output.</summary>
    public long TownUnits { get; set; }

    /// <summary>What Output accepted; the rest stays in SYS_PRODUCTION, lost to the town (F10).</summary>
    public long TownAccepted { get; set; }

    public decimal PoolBefore { get; set; }

    public decimal PoolAfter { get; set; }

    public decimal PoolCapacity { get; set; }

    /// <summary>The regeneration time the game server was given for these blocks.</summary>
    public int RegenSeconds { get; set; }

    public long? WorkerTransactionId { get; set; }

    public long? TownTransactionId { get; set; }

    /// <summary>Why the town's share was (partly) lost, if it was.</summary>
    [MaxLength(ReasonMaxLength)]
    public string? ShortfallReason { get; set; }
}

/// <summary>The fractions of harvests not yet paid out, per player and rule (§ 6.2: they accrue like line accrual).</summary>
public class HarvestAccrual
{
    public int Id { get; set; }

    public int UserId { get; set; }

    public int HarvestRuleId { get; set; }

    public HarvestRule HarvestRule { get; set; } = null!;

    /// <summary>The worker's share below one unit, carried to the next harvest.</summary>
    public decimal WorkerAccrued { get; set; }

    /// <summary>The town's share below one unit, carried to the next harvest.</summary>
    public decimal TownAccrued { get; set; }

    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
