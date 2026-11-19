using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using knkwebapi_v2.Attributes;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

// Yield areas and surveys P4 (PandiO/knk-workspace#138, PRODUCTION_KINDS.md § 2-4). The plugin surveys an area and
// reports the units it counted (L12, #679); the API stores the survey history and turns the latest surveys into base
// rates (Derived, Hybrid) through the kind's yield table. Nothing here reads the world.

/// <summary>
/// A production structure's area (PRODUCTION_KINDS § 3.1): its model (from the kind), its shape (a WorldGuard region,
/// a cuboid, a polygon or a radius) and an axis-aligned bounding box. Exclusive areas of the same model may not
/// overlap; that is checked on save from the bounding boxes (exact checks are the plugin's). <see cref="Size"/> is
/// what the level's <c>MaxAreaSize</c> caps: blocks (bordered fields, pastures), volume (exclusive volumes) or radius
/// (shared radius, hunting grounds).
/// </summary>
[FormConfigurableEntity("ProductionArea")]
public class ProductionArea
{
    public const int NameMaxLength = 100;
    public const int WorldNameMaxLength = 100;
    public const int RegionIdMaxLength = 128;
    public const int PointsMaxLength = 8000;
    public const int MaxPolygonPoints = 200;
    public const int MaxRadius = 1024;
    public const int WorldMinY = -64;
    public const int WorldMaxY = 319;

    public int Id { get; set; }

    [NavigationPair(nameof(ProductionStructure))]
    [RelatedEntityField(typeof(ProductionStructure))]
    public int ProductionStructureId { get; set; }
    [RelatedEntityField(typeof(ProductionStructure))]
    public ProductionStructure ProductionStructure { get; set; } = null!;

    [MaxLength(NameMaxLength)]
    public string Name { get; set; } = null!;

    /// <summary>The structure kind's area model when the area was saved.</summary>
    public ProductionAreaModel Model { get; set; }

    public ProductionAreaShape Shape { get; set; }

    /// <summary>Exclusive areas may not overlap other exclusive areas of the same model. From the kind; can be turned off.</summary>
    public bool Exclusive { get; set; }

    [MaxLength(WorldNameMaxLength)]
    public string WorldName { get; set; } = "world";

    /// <summary>With shape Region.</summary>
    [MaxLength(RegionIdMaxLength)]
    public string? WgRegionId { get; set; }

    /// <summary>With shape Polygon: a JSON list of <c>{"x":..,"z":..}</c> points.</summary>
    [MaxLength(PointsMaxLength)]
    public string? PointsJson { get; set; }

    /// <summary>With shape Radius: the centre (default the structure's Location) and the radius in blocks.</summary>
    public int? CenterX { get; set; }
    public int? CenterY { get; set; }
    public int? CenterZ { get; set; }
    public int? Radius { get; set; }

    // The bounding box (block coordinates, inclusive): computed for cuboid, polygon and radius, reported for regions.
    public int MinX { get; set; }
    public int MinY { get; set; }
    public int MinZ { get; set; }
    public int MaxX { get; set; }
    public int MaxY { get; set; }
    public int MaxZ { get; set; }

    /// <summary>Blocks, volume or radius by the model (see the class summary).</summary>
    public long Size { get; set; }

    /// <summary>Surveys run only for enabled areas of Derived and Hybrid structures (G5).</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>A survey is wanted (on creation, staff request); cleared by the next survey report.</summary>
    public DateTime? SurveyRequestedAt { get; set; }

    public AreaSurveyTrigger? SurveyRequestTrigger { get; set; }

    /// <summary>The latest survey (no FK: the history is its own table).</summary>
    public long? LastSurveyId { get; set; }

    public DateTime? LastSurveyedAt { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// A snapshot of what an area contains (PRODUCTION_KINDS § 3.2), computed by the plugin and stored by the API. The
/// history is kept so the web app can show trends. Units counted in shared areas carry how many structures share
/// them, so each structure gets an equal part (G7).
/// </summary>
[FormConfigurableEntity("AreaSurvey")]
public class AreaSurvey
{
    public const int ContentHashMaxLength = 128;
    public const int NoteMaxLength = 500;
    public const int MaxUnits = 2000;

    public long Id { get; set; }

    [NavigationPair(nameof(ProductionArea))]
    [RelatedEntityField(typeof(ProductionArea))]
    public int ProductionAreaId { get; set; }
    [RelatedEntityField(typeof(ProductionArea))]
    public ProductionArea ProductionArea { get; set; } = null!;

    public int ProductionStructureId { get; set; }

    public DateTime SurveyedAt { get; set; }

    /// <summary>When the API stored it.</summary>
    public DateTime ReceivedAt { get; set; }

    /// <summary>The kind's survey profile version the plugin used.</summary>
    public int ProfileVersion { get; set; }

    public AreaSurveyTrigger Trigger { get; set; }

    /// <summary>The plugin's hash of the contents: the same world gives the same hash.</summary>
    [MaxLength(ContentHashMaxLength)]
    public string? ContentHash { get; set; }

    /// <summary>The sum of all unit counts.</summary>
    public long TotalUnits { get; set; }

    [MaxLength(NoteMaxLength)]
    public string? Note { get; set; }

    [RelatedEntityField(typeof(AreaSurveyUnit))]
    public ICollection<AreaSurveyUnit> Units { get; set; } = new List<AreaSurveyUnit>();
}

/// <summary>One counted unit group of a survey: a unit key (<c>TREE:OAK:LARGE</c>, <c>BLOCK:COAL_ORE</c>,
/// <c>WATER:RIVER</c>, <c>ENTITY:SHEEP</c>), its count and how many structures share these units (1 = not shared).</summary>
[FormConfigurableEntity("AreaSurveyUnit")]
public class AreaSurveyUnit
{
    public const int UnitKeyMaxLength = 100;
    public const int ExtraMaxLength = 500;
    public const int MaxSharedBy = 100;

    public long Id { get; set; }

    [NavigationPair(nameof(AreaSurvey))]
    [RelatedEntityField(typeof(AreaSurvey))]
    public long AreaSurveyId { get; set; }
    [RelatedEntityField(typeof(AreaSurvey))]
    public AreaSurvey AreaSurvey { get; set; } = null!;

    [MaxLength(UnitKeyMaxLength)]
    public string UnitKey { get; set; } = null!;

    public long Count { get; set; }

    /// <summary>The number of structures whose areas contain these units (equal split per unit, G7).</summary>
    public int SharedBy { get; set; } = 1;

    /// <summary>Free extra data from the plugin (depth, open-sky share, …), JSON.</summary>
    [MaxLength(ExtraMaxLength)]
    public string? Extra { get; set; }
}

/// <summary>
/// One row of a kind's yield table (PRODUCTION_KINDS § 2): survey units matching <see cref="UnitKey"/> (exact, or a
/// prefix ending in <c>*</c>) produce the blueprint at <see cref="RatePerUnitPerHour"/>, times the byproduct chance
/// (a share, P-D9), from <see cref="MinLevel"/> on. Edited inside its kind.
/// </summary>
[FormConfigurableEntity("ProductionYieldRow")]
public class ProductionYieldRow
{
    public int Id { get; set; }

    [NavigationPair(nameof(ProductionKind))]
    [RelatedEntityField(typeof(ProductionKind))]
    public int ProductionKindId { get; set; }
    [RelatedEntityField(typeof(ProductionKind))]
    public ProductionKind ProductionKind { get; set; } = null!;

    [MaxLength(AreaSurveyUnit.UnitKeyMaxLength)]
    public string UnitKey { get; set; } = null!;

    [NavigationPair(nameof(ItemBlueprint))]
    [RelatedEntityField(typeof(ItemBlueprint))]
    public int ItemBlueprintId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint ItemBlueprint { get; set; } = null!;

    public decimal RatePerUnitPerHour { get; set; }

    /// <summary>1 = a main output; below 1 = a byproduct produced as that share of the rate.</summary>
    public decimal ByproductChance { get; set; } = 1m;

    /// <summary>1-10: the row counts from this structure level on.</summary>
    public int MinLevel { get; set; } = 1;
}

/// <summary>A kind's primary output (PRODUCTION_KINDS § 2 <c>PrimaryOutputs</c>): gets the kind's primary multiplier on
/// Derived and Hybrid lines; everything else is a byproduct. Edited inside its kind.</summary>
[FormConfigurableEntity("ProductionKindPrimaryOutput")]
public class ProductionKindPrimaryOutput
{
    public int Id { get; set; }

    [NavigationPair(nameof(ProductionKind))]
    [RelatedEntityField(typeof(ProductionKind))]
    public int ProductionKindId { get; set; }
    [RelatedEntityField(typeof(ProductionKind))]
    public ProductionKind ProductionKind { get; set; } = null!;

    [NavigationPair(nameof(ItemBlueprint))]
    [RelatedEntityField(typeof(ItemBlueprint))]
    public int ItemBlueprintId { get; set; }
    [RelatedEntityField(typeof(ItemBlueprint))]
    public ItemBlueprint ItemBlueprint { get; set; } = null!;
}

/// <summary>
/// A Hybrid override of one output of one structure (PRODUCTION_KINDS § 4.1): a pin (always produce at a fixed base
/// rate), an exclusion or a scale of the survey-derived rate. Ignored unless the structure's yield source is Hybrid.
/// </summary>
[FormConfigurableEntity("ProductionYieldOverride")]
public class ProductionYieldOverride
{
    public const int NoteMaxLength = 500;
    public const decimal MaxScale = 100m;

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

    public ProductionYieldOverrideKind Kind { get; set; }

    /// <summary>The pinned base rate per hour (Pin) or the factor (Scale); unused for Exclude.</summary>
    public decimal Value { get; set; }

    [MaxLength(NoteMaxLength)]
    public string? Note { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}
