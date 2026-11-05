using AutoMapper;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;

namespace knkwebapi_v2.Tests.Services.Survival;

/// <summary>
/// Siege Survival phase 1 test data (PandiO/knk-workspace#41, link S4): SiegeTestData's READY scenario 100 (Cinix,
/// Defender team 201, Attacker team 202, objectives 501 "Keep" (insta-lose) and 502 "Gatehouse" on gate 400) plus a
/// complete, READY survival graph: settings, one difficulty profile, two monster types, a special profile with its
/// schedule, two spawnzones (initial + overrun), one purchase point, an items pool and a books pool each adding up
/// to 100 %, two Mystery Box points. Tests break one rule at a time.
/// </summary>
public static class SurvivalTestData
{
    public const int KitSword = 70;
    public const int KitBow = 71;
    public const int KitOther = 72;
    public const int BlueprintSword = 80;
    public const int BlueprintBow = 81;
    public const int EnchantSharpness = 90;
    public const int EnchantPower = 91;

    public static IMapper Mapper() => new MapperConfiguration(cfg =>
    {
        cfg.AddProfile<SiegeMappingProfile>();
        cfg.AddProfile<ClanMappingProfile>();
        cfg.AddProfile<LocationMappingProfile>();
        cfg.AddProfile<PagedQueryMappingProfile>();
        cfg.AddProfile<SiegeSurvivalMappingProfile>();
    }).CreateMapper();

    public static SiegeSurvivalSettings Settings() => new()
    {
        Id = 1, SiegeScenarioId = 100, Enabled = true, MinPlayers = 1,
        EquipmentMode = SurvivalEquipmentMode.OwnInventory
    };

    public static SurvivalDifficultyProfile Profile(int id = 10) => new()
    {
        Id = id, SiegeScenarioId = 100, Name = "Normal", IsDefault = true,
        CountPreset = SurvivalScalingPreset.Linear, CountBase = 6, CountStep = 2, CountMultiplier = 1.1, CountSwitchRound = 9,
        StatPreset = SurvivalScalingPreset.Hybrid, StatStep = 0.1, StatMultiplier = 1.1, StatSwitchRound = 9,
        HealthWeight = 1, WalkingSpeedWeight = 0.3, MeleeDamageWeight = 1, RangedDamageWeight = 1, AttackSpeedWeight = 0.5,
        PerPlayerFactor = 0.25, MaxAlive = 40, StatCap = 1.5, CountCap = 0, RepairRatePerSecond = 50
    };

    public static SurvivalMysteryBoxPool ItemsPool(int id = 40) => new()
    {
        Id = id, SiegeScenarioId = 100, Name = "Weapons", Variant = SurvivalMysteryBoxVariant.Items,
        Entries =
        {
            new SurvivalMysteryBoxEntry
            {
                Id = id * 10 + 1, SurvivalMysteryBoxPoolId = id, ItemBlueprintId = BlueprintSword, ChancePercent = 60.5m,
                Enchantments = { new SurvivalMysteryBoxEntryEnchantment { Id = id * 100 + 1, EnchantmentDefinitionId = EnchantSharpness, Level = 2, ChancePercent = 25 } }
            },
            new SurvivalMysteryBoxEntry { Id = id * 10 + 2, SurvivalMysteryBoxPoolId = id, ItemBlueprintId = BlueprintBow, ChancePercent = 39.5m }
        }
    };

    public static SurvivalMysteryBoxPool BooksPool(int id = 41) => new()
    {
        Id = id, SiegeScenarioId = 100, Name = "Books", Variant = SurvivalMysteryBoxVariant.EnchantedBooks,
        Entries =
        {
            new SurvivalMysteryBoxEntry { Id = id * 10 + 1, SurvivalMysteryBoxPoolId = id, EnchantmentDefinitionId = EnchantSharpness, EnchantmentLevel = 3, ChancePercent = 70 },
            new SurvivalMysteryBoxEntry { Id = id * 10 + 2, SurvivalMysteryBoxPoolId = id, EnchantmentDefinitionId = EnchantPower, EnchantmentLevel = 1, ChancePercent = 30 }
        }
    };

    /// <summary>The in-memory graph (no database) readiness tests start from; every rule passes.</summary>
    public static SurvivalScenarioGraph ReadyGraph(SiegeScenario? scenario = null)
    {
        scenario ??= SiegeTestData.ValidScenario();
        var special = new SurvivalSpecialWaveProfile
        {
            Id = 20, SiegeScenarioId = 100, Key = "wolves", Name = "Wolf pack", CountMultiplier = 1.5,
            MonsterTypes = { new SurvivalSpecialWaveMonsterType { Id = 21, SurvivalSpecialWaveProfileId = 20, EntityType = "WOLF" } }
        };
        return new SurvivalScenarioGraph
        {
            Scenario = scenario,
            Settings = Settings(),
            DifficultyProfiles = { Profile() },
            MonsterTypes =
            {
                new SurvivalMonsterType { Id = 11, SiegeScenarioId = 100, EntityType = "ZOMBIE", Weight = 2 },
                new SurvivalMonsterType { Id = 12, SiegeScenarioId = 100, EntityType = "SKELETON", Weight = 1, UnlockRound = 3 }
            },
            SpecialWaveProfiles = { special },
            SpecialWaveEntries =
            {
                new SurvivalSpecialWaveEntry { Id = 25, SiegeScenarioId = 100, SurvivalSpecialWaveProfileId = 20, Kind = SurvivalSpecialWaveKind.Recurring, Interval = 5, Priority = 1 },
                new SurvivalSpecialWaveEntry { Id = 26, SiegeScenarioId = 100, SurvivalSpecialWaveProfileId = 20, Kind = SurvivalSpecialWaveKind.Explicit, Wave = 7 }
            },
            SpawnZones =
            {
                new SurvivalSpawnZone
                {
                    Id = 30, SiegeScenarioId = 100, Name = "Outside", AnchorLocationId = 1002, AnchorLocation = SiegeTestData.Loc(1002, 100, 0),
                    Points = { new SurvivalSpawnZonePoint { Id = 31, SurvivalSpawnZoneId = 30, Kind = SurvivalSpawnZonePointKind.Spawn, LocationId = 1002, Location = SiegeTestData.Loc(1002, 100, 0) } }
                },
                new SurvivalSpawnZone
                {
                    Id = 32, SiegeScenarioId = 100, Name = "Courtyard", AnchorLocationId = 1003, AnchorLocation = SiegeTestData.Loc(1003), AreaRadius = 6,
                    PrerequisiteObjectives = { new SurvivalSpawnZoneObjective { SurvivalSpawnZoneId = 32, SiegeObjectiveId = 502 } }
                }
            },
            TargetOverrides = { new SurvivalTargetOverride { Id = 35, SiegeScenarioId = 100, GateStructureId = 400, Mode = SurvivalTargetOverrideMode.Allow } },
            PurchasePoints =
            {
                new SurvivalPurchasePoint
                {
                    Id = 36, SiegeScenarioId = 100, Name = "Armory", LocationId = 1001, Location = SiegeTestData.Loc(1001),
                    Offers = { new SurvivalPurchaseOffer { Id = 37, SurvivalPurchasePointId = 36, ItemBlueprintId = BlueprintSword, Price = 500 } }
                }
            },
            MysteryBoxPools = { ItemsPool(), BooksPool() },
            MysteryBoxPoints =
            {
                new SurvivalMysteryBoxPoint { Id = 50, SiegeScenarioId = 100, Name = "Box", LocationId = 1004, Location = SiegeTestData.Loc(1004), SurvivalMysteryBoxPoolId = 40 },
                new SurvivalMysteryBoxPoint { Id = 51, SiegeScenarioId = 100, Name = "Book box", LocationId = 1004, Location = SiegeTestData.Loc(1004), SurvivalMysteryBoxPoolId = 41 }
            },
            ExistingKitIds = new HashSet<int> { KitSword, KitBow, KitOther },
            ExistingItemBlueprintIds = new HashSet<int> { BlueprintSword, BlueprintBow },
            ExistingEnchantmentDefinitionIds = new HashSet<int> { EnchantSharpness, EnchantPower }
        };
    }

    /// <summary>Persists the catalog rows the survival graph references (kits, blueprints, enchantments).</summary>
    public static async Task SeedCatalogAsync(KnKDbContext context)
    {
        context.Kits.Add(new Kit { Id = KitSword, Name = "Swordsman" });
        context.Kits.Add(new Kit { Id = KitBow, Name = "Archer" });
        context.Kits.Add(new Kit { Id = KitOther, Name = "Other" });
        context.ItemBlueprints.Add(new ItemBlueprint { Id = BlueprintSword, Name = "Iron sword", DefaultDisplayName = "Iron sword" });
        context.ItemBlueprints.Add(new ItemBlueprint { Id = BlueprintBow, Name = "Bow", DefaultDisplayName = "Bow" });
        context.EnchantmentDefinitions.Add(new EnchantmentDefinition { Id = EnchantSharpness, Key = "minecraft:sharpness", DisplayName = "Sharpness", Description = "d", MaxLevel = 5 });
        context.EnchantmentDefinitions.Add(new EnchantmentDefinition { Id = EnchantPower, Key = "minecraft:power", DisplayName = "Power", Description = "d", MaxLevel = 5 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }

    /// <summary>Persists ReadyGraph()'s survival rows (ids-only shape) on top of SiegeTestData.SeedValidScenarioAsync.</summary>
    public static async Task SeedReadySurvivalAsync(KnKDbContext context)
    {
        await SeedCatalogAsync(context);
        context.SiegeSurvivalSettings.Add(Settings());
        context.SurvivalDifficultyProfiles.Add(Profile());
        context.SurvivalMonsterTypes.Add(new SurvivalMonsterType { Id = 11, SiegeScenarioId = 100, EntityType = "ZOMBIE", Weight = 2 });
        context.SurvivalMonsterTypes.Add(new SurvivalMonsterType { Id = 12, SiegeScenarioId = 100, EntityType = "SKELETON", Weight = 1, UnlockRound = 3 });
        context.SurvivalSpecialWaveProfiles.Add(new SurvivalSpecialWaveProfile
        {
            Id = 20, SiegeScenarioId = 100, Key = "wolves", Name = "Wolf pack", CountMultiplier = 1.5,
            MonsterTypes = { new SurvivalSpecialWaveMonsterType { Id = 21, EntityType = "WOLF" } }
        });
        context.SurvivalSpecialWaveEntries.Add(new SurvivalSpecialWaveEntry { Id = 25, SiegeScenarioId = 100, SurvivalSpecialWaveProfileId = 20, Kind = SurvivalSpecialWaveKind.Recurring, Interval = 5, Priority = 1 });
        context.SurvivalSpecialWaveEntries.Add(new SurvivalSpecialWaveEntry { Id = 26, SiegeScenarioId = 100, SurvivalSpecialWaveProfileId = 20, Kind = SurvivalSpecialWaveKind.Explicit, Wave = 7 });
        context.SurvivalSpawnZones.Add(new SurvivalSpawnZone
        {
            Id = 30, SiegeScenarioId = 100, Name = "Outside", AnchorLocationId = 1002,
            Points = { new SurvivalSpawnZonePoint { Id = 31, Kind = SurvivalSpawnZonePointKind.Spawn, LocationId = 1002 } }
        });
        context.SurvivalSpawnZones.Add(new SurvivalSpawnZone
        {
            Id = 32, SiegeScenarioId = 100, Name = "Courtyard", AnchorLocationId = 1003, AreaRadius = 6,
            PrerequisiteObjectives = { new SurvivalSpawnZoneObjective { SiegeObjectiveId = 502 } }
        });
        context.SurvivalTargetOverrides.Add(new SurvivalTargetOverride { Id = 35, SiegeScenarioId = 100, GateStructureId = 400, Mode = SurvivalTargetOverrideMode.Allow });
        context.SurvivalPurchasePoints.Add(new SurvivalPurchasePoint
        {
            Id = 36, SiegeScenarioId = 100, Name = "Armory", LocationId = 1001,
            Offers = { new SurvivalPurchaseOffer { Id = 37, ItemBlueprintId = BlueprintSword, Price = 500 } }
        });
        context.SurvivalMysteryBoxPools.Add(ItemsPool());
        context.SurvivalMysteryBoxPools.Add(BooksPool());
        context.SurvivalMysteryBoxPoints.Add(new SurvivalMysteryBoxPoint { Id = 50, SiegeScenarioId = 100, Name = "Box", LocationId = 1004, SurvivalMysteryBoxPoolId = 40 });
        context.SurvivalMysteryBoxPoints.Add(new SurvivalMysteryBoxPoint { Id = 51, SiegeScenarioId = 100, Name = "Book box", LocationId = 1004, SurvivalMysteryBoxPoolId = 41 });
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }
}
