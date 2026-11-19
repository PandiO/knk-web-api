using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// Production engine P3 (PandiO/knk-workspace#86, PRODUCTION_KINDS.md § 2-5, DESIGN § 9). The engine is engine-free
// API logic (DESIGN § 15.1): nothing here knows Minecraft beyond blueprint ids.

/// <summary>
/// A production kind (PRODUCTION_KINDS § 2): catalogue data, not a class per kind. Seeded with the V1 catalogue
/// (§ 2.1, Hunter deferred, P-D10). Every kind has exactly ten <see cref="ProductionKindLevel"/>s (G8), created with
/// it. The survey profile, yield table and primary outputs (Derived/Hybrid yields), harvest and regeneration rules come
/// with P4 and P8.
/// </summary>
[FormConfigurableEntity("ProductionKind")]
public class ProductionKind
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 1000;
    public const int IconMaxLength = 100;
    public const int DefaultStoragesMaxLength = 4000;
    public const int LevelCount = 10;
    public const int MaxDefaultWorkers = 100;

    public int Id { get; set; }

    [MaxLength(NameMaxLength)]
    public string Name { get; set; } = null!;

    [MaxLength(DescriptionMaxLength)]
    public string? Description { get; set; }

    /// <summary>A Minecraft material name for menus and the web app (e.g. <c>IRON_PICKAXE</c>).</summary>
    [MaxLength(IconMaxLength)]
    public string? Icon { get; set; }

    public ProductionAreaModel AreaModel { get; set; }

    /// <summary>Whether the kind's areas are exclusive (no overlap, § 3.1); from the model, can be turned off.</summary>
    public bool ExclusiveAreas { get; set; }

    // The yield sources a structure of this kind may use (§ 4.1); Hybrid is the default (P-D1).
    public bool AllowsDerived { get; set; } = true;
    public bool AllowsConfigured { get; set; } = true;
    public bool AllowsHybrid { get; set; } = true;

    public ResourceCategory ResourceCategory { get; set; }

    /// <summary>Producer workers a new structure of this kind gets (§ 4.5), default 2.</summary>
    public int DefaultWorkers { get; set; } = 2;

    /// <summary>Optional ±10% per-cycle variation (P-D9); stored now, used with byproducts in P4.</summary>
    public bool CycleVariation { get; set; }

    /// <summary>The storages a new structure of this kind gets next to its Output storage: a JSON list of
    /// <c>{"purpose":"Input","name":"inputs","maxUnits":500}</c>. Empty or null = only Output.</summary>
    [MaxLength(DefaultStoragesMaxLength)]
    public string? DefaultStoragesJson { get; set; }

    public bool IsActive { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [RelatedEntityField(typeof(ProductionKindLevel))]
    public ICollection<ProductionKindLevel> Levels { get; set; } = new List<ProductionKindLevel>();

    // ---- Yield areas and surveys P4 (PandiO/knk-workspace#138, PRODUCTION_KINDS § 2-4) ----

    public const int SurveyProfileMaxLength = 4000;
    public const decimal DefaultPrimaryMultiplier = 1.5m;
    public const decimal MaxPrimaryMultiplier = 100m;

    /// <summary>What the plugin's survey counts and how it groups units (§ 3.2): a JSON object the plugin reads, e.g.
    /// <c>{"unitTypes":["TREE","SAPLING"]}</c>. Seeded per kind; the plugin link (#679) fills in the details.</summary>
    [MaxLength(SurveyProfileMaxLength)]
    public string? SurveyProfileJson { get; set; }

    /// <summary>Raised by the API on every change of <see cref="SurveyProfileJson"/>; stored with each survey.</summary>
    public int SurveyProfileVersion { get; set; } = 1;

    /// <summary>The factor the <see cref="PrimaryOutputs"/> get on Derived and Hybrid lines (built-in "primary" step).</summary>
    public decimal PrimaryMultiplier { get; set; } = DefaultPrimaryMultiplier;

    [RelatedEntityField(typeof(ProductionYieldRow))]
    public ICollection<ProductionYieldRow> YieldTable { get; set; } = new List<ProductionYieldRow>();

    [RelatedEntityField(typeof(ProductionKindPrimaryOutput))]
    public ICollection<ProductionKindPrimaryOutput> PrimaryOutputs { get; set; } = new List<ProductionKindPrimaryOutput>();
}

/// <summary>
/// One of a production kind's ten levels (PRODUCTION_KINDS § 5, G8). Written to the structure when its level changes:
/// the yield multiplier is the level modifier source, the cycle speed shortens the cycle, and the caps go to the
/// structure's Output storage and transport settings (null = leave as they are). <see cref="UpkeepReduction"/> has no
/// effect in V1.
/// </summary>
[FormConfigurableEntity("ProductionKindLevel")]
public class ProductionKindLevel
{
    public int Id { get; set; }

    [NavigationPair(nameof(ProductionKind))]
    [RelatedEntityField(typeof(ProductionKind))]
    public int ProductionKindId { get; set; }
    [RelatedEntityField(typeof(ProductionKind))]
    public ProductionKind ProductionKind { get; set; } = null!;

    /// <summary>1-10.</summary>
    public int Level { get; set; }

    public decimal YieldMultiplier { get; set; } = 1m;

    /// <summary>Cycles per base cycle: 2 = settles twice as often (the rate per hour is unchanged).</summary>
    public decimal CycleSpeedMultiplier { get; set; } = 1m;

    /// <summary>The area cap P4 checks: blocks (bordered fields, pastures), volume (exclusive volumes) or radius
    /// (shared radius), by the kind's area model. Null = no cap.</summary>
    public int? MaxAreaSize { get; set; }

    /// <summary>Written to the Output storage's <c>MaxUnits</c>; null = leave it.</summary>
    public int? OutputMaxUnits { get; set; }

    /// <summary>Written to the Output storage's <c>UnitsPerSlot</c>; null = leave it.</summary>
    public int? UnitsPerSlot { get; set; }

    /// <summary>Written to the structure's <c>TransportCapacity</c>; null = leave it.</summary>
    public int? TransportCapacity { get; set; }

    /// <summary>No effect in V1 (upkeep is Later).</summary>
    public decimal UpkeepReduction { get; set; }

    /// <summary>Coins paid to reach this level (through the currency service).</summary>
    public long UpgradeCoins { get; set; }

    /// <summary>Optional construction time; stored, not enforced in V1 (admins upgrade at once).</summary>
    public int? UpgradeDurationMinutes { get; set; }

    [RelatedEntityField(typeof(ProductionKindLevelItem))]
    public ICollection<ProductionKindLevelItem> Items { get; set; } = new List<ProductionKindLevelItem>();
}

/// <summary>A blueprint a level unlocks or costs (PRODUCTION_KINDS § 5 <c>OutputUnlocks</c>, <c>UpgradeCost</c>).
/// Edited inside its level (no form of its own).</summary>
[FormConfigurableEntity("ProductionKindLevelItem")]
public class ProductionKindLevelItem
{
    public int Id { get; set; }

    [NavigationPair(nameof(ProductionKindLevel))]
    [RelatedEntityField(typeof(ProductionKindLevel))]
    public int ProductionKindLevelId { get; set; }
    [RelatedEntityField(typeof(ProductionKindLevel))]
    public ProductionKindLevel ProductionKindLevel { get; set; } = null!;

    public ProductionKindLevelItemRole Role { get; set; }

    [NavigationPair(nameof(ItemBlueprint))]
    [RelatedEntityField(typeof(ItemBlueprint))]
    public int ItemBlueprintId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint ItemBlueprint { get; set; } = null!;

    /// <summary>Units of an upgrade cost; 0 for an unlock.</summary>
    public long Amount { get; set; }
}

/// <summary>
/// One output of a production structure (PRODUCTION_KINDS § 4.1-4.3): a base rate per hour (Configured in P3),
/// a decimal <see cref="Accrued"/>, inputs per hour, and an anchored due time (<see cref="NextSettleAt"/>, indexed) so
/// cycles never drift (V2 defects 5, 10).
/// </summary>
[FormConfigurableEntity("ProductionLine")]
public class ProductionLine
{
    public const int StatusReasonMaxLength = 500;
    public const decimal MaxRatePerHour = 1_000_000m;

    public int Id { get; set; }

    [NavigationPair(nameof(ProductionStructure))]
    [RelatedEntityField(typeof(ProductionStructure))]
    public int ProductionStructureId { get; set; }
    [RelatedEntityField(typeof(ProductionStructure))]
    public ProductionStructure ProductionStructure { get; set; } = null!;

    [NavigationPair(nameof(ItemBlueprint))]
    [RelatedEntityField(typeof(ItemBlueprint))]
    public int ItemBlueprintId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint ItemBlueprint { get; set; } = null!;

    public ProductionRateSource RateSource { get; set; } = ProductionRateSource.Configured;

    /// <summary>The admin-entered base rate (units per hour at level multiplier 1, all workers present).</summary>
    public decimal ConfiguredRatePerHour { get; set; }

    /// <summary>Overrides the kind's resource category for weather and category modifiers; null = the kind's.</summary>
    public ResourceCategory? ResourceCategory { get; set; }

    /// <summary>The staff switch of this line (Paused when off).</summary>
    public bool Enabled { get; set; } = true;

    public ProductionLineStatus Status { get; set; } = ProductionLineStatus.Running;

    [MaxLength(StatusReasonMaxLength)]
    public string? StatusReason { get; set; }

    /// <summary>Fractional output not yet emitted (0 ≤ Accrued &lt; 1 after a settle that emitted everything).</summary>
    public decimal Accrued { get; set; }

    /// <summary>The cycle grid's origin: due times are <c>AnchorAt + k × cycle</c>.</summary>
    public DateTime AnchorAt { get; set; }

    /// <summary>The end of the last settled period: the next settle accrues from here.</summary>
    public DateTime LastSettledAt { get; set; }

    /// <summary>The next due time on the grid; the sweep picks lines with <c>NextSettleAt ≤ now</c>.</summary>
    public DateTime NextSettleAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [RelatedEntityField(typeof(ProductionLineInput))]
    public ICollection<ProductionLineInput> Inputs { get; set; } = new List<ProductionLineInput>();
}

/// <summary>An input a line consumes while it runs (V2 defect 12): units per hour of running time, taken from the
/// structure's Input storage. Edited inside its line.</summary>
[FormConfigurableEntity("ProductionLineInput")]
public class ProductionLineInput
{
    public int Id { get; set; }

    [NavigationPair(nameof(ProductionLine))]
    [RelatedEntityField(typeof(ProductionLine))]
    public int ProductionLineId { get; set; }
    [RelatedEntityField(typeof(ProductionLine))]
    public ProductionLine ProductionLine { get; set; } = null!;

    [NavigationPair(nameof(ItemBlueprint))]
    [RelatedEntityField(typeof(ItemBlueprint))]
    public int ItemBlueprintId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint ItemBlueprint { get; set; } = null!;

    public decimal AmountPerHour { get; set; }

    /// <summary>Fractional consumption not yet taken.</summary>
    public decimal Accrued { get; set; }
}

/// <summary>
/// A stored production modifier (PRODUCTION_KINDS § 4.2): scope, target, effect and active window. The level and
/// weather modifiers are computed by their sources, not stored; staff-made rows are the "Admin" source.
/// </summary>
[FormConfigurableEntity("ProductionModifier")]
public class ProductionModifier
{
    public const int NameMaxLength = 100;
    public const int SourceMaxLength = 50;
    public const int TargetCategoryMaxLength = 32;
    public const string AdminSource = "Admin";

    public int Id { get; set; }

    [MaxLength(NameMaxLength)]
    public string Name { get; set; } = null!;

    /// <summary>Which modifier source made it; staff rows are <see cref="AdminSource"/>.</summary>
    [MaxLength(SourceMaxLength)]
    public string Source { get; set; } = AdminSource;

    public ProductionModifierScope Scope { get; set; }

    /// <summary>The scope's row: kingdom, town, structure or production line id; null for Global (and MapArea, Later).</summary>
    public int? ScopeRefId { get; set; }

    public ProductionModifierTarget Target { get; set; }

    /// <summary>With target Blueprint.</summary>
    [NavigationPair(nameof(TargetItemBlueprint))]
    [RelatedEntityField(typeof(ItemBlueprint))]
    public int? TargetItemBlueprintId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint? TargetItemBlueprint { get; set; }

    /// <summary>With target Category.</summary>
    public ResourceCategory? TargetCategory { get; set; }

    /// <summary>With target Kind.</summary>
    [NavigationPair(nameof(TargetProductionKind))]
    [RelatedEntityField(typeof(ProductionKind))]
    public int? TargetProductionKindId { get; set; }
    [RelatedEntityField(typeof(ProductionKind))]
    public ProductionKind? TargetProductionKind { get; set; }

    public ProductionModifierEffect Effect { get; set; }

    /// <summary>The addition (units per hour) or the factor.</summary>
    public decimal Value { get; set; }

    /// <summary>Active from (null = always started).</summary>
    public DateTime? ActiveFrom { get; set; }

    /// <summary>Active until, exclusive (null = no expiry).</summary>
    public DateTime? ActiveUntil { get; set; }

    public bool Enabled { get; set; } = true;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A climate zone (PRODUCTION_KINDS § 4.4): weather odds and the towns it covers. One zone is the default (seeded)
/// for every town without one. Its weather is rolled once per in-game day and stored (<see cref="WeatherRoll"/>).
/// </summary>
[FormConfigurableEntity("ClimateZone")]
public class ClimateZone
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 1000;

    public int Id { get; set; }

    [MaxLength(NameMaxLength)]
    public string Name { get; set; } = null!;

    [MaxLength(DescriptionMaxLength)]
    public string? Description { get; set; }

    /// <summary>Exactly one zone is the default (checked by the service).</summary>
    public bool IsDefault { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [RelatedEntityField(typeof(ClimateZoneWeather))]
    public ICollection<ClimateZoneWeather> WeatherOdds { get; set; } = new List<ClimateZoneWeather>();

    [RelatedEntityField(typeof(ClimateZoneTown))]
    public ICollection<ClimateZoneTown> Towns { get; set; } = new List<ClimateZoneTown>();

    /// <summary>The stored daily rolls (the API lists the latest ones on the zone).</summary>
    [RelatedEntityField(typeof(WeatherRoll))]
    public ICollection<WeatherRoll> Rolls { get; set; } = new List<WeatherRoll>();
}

/// <summary>The chance of one weather type in a zone, in percent; the rest of 100 is clear weather.</summary>
[FormConfigurableEntity("ClimateZoneWeather")]
public class ClimateZoneWeather
{
    public int Id { get; set; }

    [NavigationPair(nameof(ClimateZone))]
    [RelatedEntityField(typeof(ClimateZone))]
    public int ClimateZoneId { get; set; }
    [RelatedEntityField(typeof(ClimateZone))]
    public ClimateZone ClimateZone { get; set; } = null!;

    [NavigationPair(nameof(WeatherType))]
    [RelatedEntityField(typeof(WeatherType))]
    public int WeatherTypeId { get; set; }
    [RelatedEntityField(typeof(WeatherType))]
    public WeatherType WeatherType { get; set; } = null!;

    public decimal ChancePercent { get; set; }
}

/// <summary>A town in a climate zone (one zone per town, unique). A town without a row is in the default zone.</summary>
[FormConfigurableEntity("ClimateZoneTown")]
public class ClimateZoneTown
{
    public int Id { get; set; }

    [NavigationPair(nameof(ClimateZone))]
    [RelatedEntityField(typeof(ClimateZone))]
    public int ClimateZoneId { get; set; }
    [RelatedEntityField(typeof(ClimateZone))]
    public ClimateZone ClimateZone { get; set; } = null!;

    [NavigationPair(nameof(Town))]
    [RelatedEntityField(typeof(Town))]
    public int TownId { get; set; }
    [RelatedEntityField(typeof(Town))]
    public Town Town { get; set; } = null!;
}

/// <summary>A weather type (rain, storm, drought) with its modifiers per resource category (PRODUCTION_KINDS § 4.4).</summary>
[FormConfigurableEntity("WeatherType")]
public class WeatherType
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 1000;

    public int Id { get; set; }

    [MaxLength(NameMaxLength)]
    public string Name { get; set; } = null!;

    [MaxLength(DescriptionMaxLength)]
    public string? Description { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [RelatedEntityField(typeof(WeatherTypeEffect))]
    public ICollection<WeatherTypeEffect> Effects { get; set; } = new List<WeatherTypeEffect>();
}

/// <summary>What a weather type does to one resource category (a pipeline modifier for the zone's towns that day).</summary>
[FormConfigurableEntity("WeatherTypeEffect")]
public class WeatherTypeEffect
{
    public int Id { get; set; }

    [NavigationPair(nameof(WeatherType))]
    [RelatedEntityField(typeof(WeatherType))]
    public int WeatherTypeId { get; set; }
    [RelatedEntityField(typeof(WeatherType))]
    public WeatherType WeatherType { get; set; } = null!;

    public ResourceCategory Category { get; set; }

    public ProductionModifierEffect Effect { get; set; }

    public decimal Value { get; set; }
}

/// <summary>
/// The stored weather of one zone on one in-game day (unique per zone and day). Deterministic: the roll is derived from
/// the zone and the day, so a replay gives the same weather; stored so it is traceable.
/// </summary>
[FormConfigurableEntity("WeatherRoll")]
public class WeatherRoll
{
    public long Id { get; set; }

    [NavigationPair(nameof(ClimateZone))]
    [RelatedEntityField(typeof(ClimateZone))]
    public int ClimateZoneId { get; set; }
    [RelatedEntityField(typeof(ClimateZone))]
    public ClimateZone ClimateZone { get; set; } = null!;

    /// <summary>The in-game day number (minutes since the Unix epoch ÷ the day length).</summary>
    public long Day { get; set; }

    public DateTime DayStartsAt { get; set; }

    public DateTime DayEndsAt { get; set; }

    /// <summary>Null = clear (no weather modifiers).</summary>
    [NavigationPair(nameof(WeatherType))]
    [RelatedEntityField(typeof(WeatherType))]
    public int? WeatherTypeId { get; set; }
    [RelatedEntityField(typeof(WeatherType))]
    public WeatherType? WeatherType { get; set; }

    /// <summary>The roll in [0, 100).</summary>
    public decimal RollValue { get; set; }

    public DateTime RolledAt { get; set; }
}

/// <summary>
/// One settle of one line (PRODUCTION_KINDS § 4.3): the "why did it produce 7" trace. Each applied modifier is in
/// <see cref="ModifiersJson"/> with its source, effect and value. Append-only, listed on the structure (view only).
/// </summary>
[FormConfigurableEntity("ProductionCycleRecord")]
public class ProductionCycleRecord
{
    public const int ShortfallReasonMaxLength = 500;

    public long Id { get; set; }

    [NavigationPair(nameof(ProductionLine))]
    [RelatedEntityField(typeof(ProductionLine))]
    public int ProductionLineId { get; set; }
    [RelatedEntityField(typeof(ProductionLine))]
    public ProductionLine ProductionLine { get; set; } = null!;

    public int ProductionStructureId { get; set; }

    public int ItemBlueprintId { get; set; }

    public DateTime SettledAt { get; set; }

    public DateTime PeriodStart { get; set; }

    public DateTime PeriodEnd { get; set; }

    /// <summary>Seconds accrued (after the catch-up cap).</summary>
    public long ElapsedSeconds { get; set; }

    /// <summary>Seconds dropped by the catch-up cap or while blocked.</summary>
    public long SkippedSeconds { get; set; }

    public ProductionRateSource RateSource { get; set; }

    public decimal BaseRatePerHour { get; set; }

    /// <summary>JSON list of { source, name, effect, value, scope } in the order applied.</summary>
    public string ModifiersJson { get; set; } = "[]";

    /// <summary>The level-unlock factor (0 or 1).</summary>
    public decimal UnlockFactor { get; set; } = 1m;

    public int WorkersPresent { get; set; }

    public int WorkersTotal { get; set; }

    public decimal WorkerFactor { get; set; }

    public decimal EffectiveRatePerHour { get; set; }

    public decimal AccruedBefore { get; set; }

    public decimal AccruedAfter { get; set; }

    public long Emitted { get; set; }

    public long Accepted { get; set; }

    /// <summary>JSON list of { itemBlueprintId, consumed } for the inputs taken.</summary>
    public string? InputsJson { get; set; }

    public ProductionLineStatus StatusAfter { get; set; }

    [MaxLength(ShortfallReasonMaxLength)]
    public string? ShortfallReason { get; set; }

    /// <summary>The stock transaction of the yield (and inputs), if anything moved.</summary>
    public long? StockTransactionId { get; set; }
}
