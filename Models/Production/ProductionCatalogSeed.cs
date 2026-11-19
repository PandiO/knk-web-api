using System;
using System.Collections.Generic;
using System.Linq;
using knkwebapi_v2.Enums;

namespace knkwebapi_v2.Models;

/// <summary>
/// The V1 production catalogue seeded by migration <c>AddProductionEngine</c> (PRODUCTION_KINDS § 2.1; Hunter stays
/// out of V1, P-D10), ten levels per kind (G8), one default climate zone and three example weather types. The default
/// zone has no odds, so the weather is clear until staff set them: production is predictable on a fresh database.
/// Level caps (Output storage, units per slot, transport) are null = "leave as they are" until staff set them.
/// </summary>
public static class ProductionCatalogSeed
{
    public static readonly DateTime SeededAt = new(2026, 11, 20, 0, 0, 0, DateTimeKind.Utc);

    public const int DefaultClimateZoneId = 1;
    public const int RainId = 1, StormId = 2, DroughtId = 3;

    public sealed record KindRow(int Id, string Name, string Description, string Icon, ProductionAreaModel Model,
        bool Exclusive, ResourceCategory Category, int AreaSizeLevel1, int AreaSizeStep);

    public static readonly IReadOnlyList<KindRow> Kinds = new[]
    {
        new KindRow(1, "Farm", "Crops on bordered farmland; the crop mix follows the field.", "WHEAT",
            ProductionAreaModel.BorderedField, true, ResourceCategory.Crops, 1024, 256),
        new KindRow(2, "Logger", "Trees in a shared radius around the structure; the yield follows the forest.", "IRON_AXE",
            ProductionAreaModel.SharedRadius, false, ResourceCategory.Wood, 32, 4),
        new KindRow(3, "Quarry", "Rock types in an exclusive volume; ores are byproducts.", "STONE",
            ProductionAreaModel.ExclusiveVolume, true, ResourceCategory.Stone, 262144, 65536),
        new KindRow(4, "Mine", "Ore in an exclusive mineshaft; the designed ore gets the primary boost.", "IRON_PICKAXE",
            ProductionAreaModel.ExclusiveVolume, true, ResourceCategory.Ore, 262144, 65536),
        new KindRow(5, "Fishery", "Fish from the water in a shared radius, by biome.", "FISHING_ROD",
            ProductionAreaModel.SharedRadius, false, ResourceCategory.Fish, 32, 4),
        new KindRow(6, "Flower farm", "Flowers in bordered beds.", "POPPY",
            ProductionAreaModel.BorderedField, true, ResourceCategory.Flowers, 1024, 256),
        new KindRow(7, "Florist", "Flowers growing in a shared radius.", "SUNFLOWER",
            ProductionAreaModel.SharedRadius, false, ResourceCategory.Flowers, 32, 4),
        new KindRow(8, "Animal farm", "Drops of the animals in a pasture.", "WHITE_WOOL",
            ProductionAreaModel.Pasture, true, ResourceCategory.Animals, 1024, 256),
    };

    public static IEnumerable<ProductionKind> KindEntities() => Kinds.Select(k => new ProductionKind
    {
        Id = k.Id,
        Name = k.Name,
        Description = k.Description,
        Icon = k.Icon,
        AreaModel = k.Model,
        ExclusiveAreas = k.Exclusive,
        AllowsDerived = true,
        AllowsConfigured = true,
        AllowsHybrid = true,
        ResourceCategory = k.Category,
        DefaultWorkers = 2,
        IsActive = true,
        CreatedAt = SeededAt,
    });

    /// <summary>The default curve of a new kind's levels (also used for kinds staff create).</summary>
    public static ProductionKindLevel DefaultLevel(int kindId, int level, int? areaSizeLevel1 = null, int areaSizeStep = 0) => new()
    {
        ProductionKindId = kindId,
        Level = level,
        YieldMultiplier = 1m + 0.15m * (level - 1),
        CycleSpeedMultiplier = 1m + 0.05m * (level - 1),
        MaxAreaSize = areaSizeLevel1.HasValue ? areaSizeLevel1.Value + areaSizeStep * (level - 1) : null,
        UpkeepReduction = 0m,
        UpgradeCoins = level == 1 ? 0 : 250L * (level - 1) * (level - 1),
    };

    public static IEnumerable<ProductionKindLevel> LevelEntities() => Kinds.SelectMany(k =>
        Enumerable.Range(1, ProductionKind.LevelCount).Select(level =>
        {
            var row = DefaultLevel(k.Id, level, k.AreaSizeLevel1, k.AreaSizeStep);
            row.Id = (k.Id - 1) * ProductionKind.LevelCount + level;
            return row;
        }));

    public static IEnumerable<ClimateZone> Zones() => new[]
    {
        new ClimateZone
        {
            Id = DefaultClimateZoneId, Name = "Default climate", IsDefault = true, CreatedAt = SeededAt,
            Description = "Every town without a zone of its own. No weather odds until staff set them, so the weather is clear.",
        },
    };

    public static IEnumerable<WeatherType> WeatherTypes() => new[]
    {
        new WeatherType { Id = RainId, Name = "Rain", Description = "Good for crops and flowers.", CreatedAt = SeededAt },
        new WeatherType { Id = StormId, Name = "Storm", Description = "Hits fisheries hard and farms a little.", CreatedAt = SeededAt },
        new WeatherType { Id = DroughtId, Name = "Drought", Description = "Hits farms, flower farms and pastures.", CreatedAt = SeededAt },
    };

    public static IEnumerable<WeatherTypeEffect> WeatherEffects() => new[]
    {
        new WeatherTypeEffect { Id = 1, WeatherTypeId = RainId, Category = ResourceCategory.Crops, Effect = ProductionModifierEffect.Multiply, Value = 1.1m },
        new WeatherTypeEffect { Id = 2, WeatherTypeId = RainId, Category = ResourceCategory.Flowers, Effect = ProductionModifierEffect.Multiply, Value = 1.1m },
        new WeatherTypeEffect { Id = 3, WeatherTypeId = StormId, Category = ResourceCategory.Fish, Effect = ProductionModifierEffect.Multiply, Value = 0.5m },
        new WeatherTypeEffect { Id = 4, WeatherTypeId = StormId, Category = ResourceCategory.Crops, Effect = ProductionModifierEffect.Multiply, Value = 0.9m },
        new WeatherTypeEffect { Id = 5, WeatherTypeId = DroughtId, Category = ResourceCategory.Crops, Effect = ProductionModifierEffect.Multiply, Value = 0.6m },
        new WeatherTypeEffect { Id = 6, WeatherTypeId = DroughtId, Category = ResourceCategory.Flowers, Effect = ProductionModifierEffect.Multiply, Value = 0.7m },
        new WeatherTypeEffect { Id = 7, WeatherTypeId = DroughtId, Category = ResourceCategory.Animals, Effect = ProductionModifierEffect.Multiply, Value = 0.8m },
    };
}
