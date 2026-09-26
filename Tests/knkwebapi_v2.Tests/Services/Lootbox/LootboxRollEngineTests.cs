using FluentAssertions;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Lootbox;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Lootbox;

/// <summary>
/// Lootboxes Phase 1 (docs/specs/lootboxes/DESIGN.md §3.1, IMPLEMENTATION_PLAN.md "LootboxRollEngineTests"): box-grade
/// weights, the item-grade window (clamping, widening), the two-stage pick, specials, the KNG-6 enchant cap, merging
/// with defaults, and books/stackables never getting rolls. Randomness is scripted.
/// </summary>
public class LootboxRollEngineTests
{
    // ===== Fixtures =====

    /// <summary>Returns queued values; fails the test if the engine asks for more (or other) randomness than expected.</summary>
    private sealed class ScriptedRandom : ILootRandom
    {
        private readonly Queue<int> _ints = new();
        private readonly Queue<double> _doubles = new();

        public ScriptedRandom Ints(params int[] values) { foreach (var v in values) _ints.Enqueue(v); return this; }
        public ScriptedRandom Doubles(params double[] values) { foreach (var v in values) _doubles.Enqueue(v); return this; }

        public int IntsLeft => _ints.Count;
        public int DoublesLeft => _doubles.Count;

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (_ints.Count == 0) throw new InvalidOperationException($"Unexpected NextInt({minInclusive}, {maxExclusive}).");
            var value = _ints.Dequeue();
            if (value < minInclusive || value >= maxExclusive)
                throw new InvalidOperationException($"Scripted int {value} is outside [{minInclusive}, {maxExclusive}).");
            return value;
        }

        public double NextDouble()
        {
            if (_doubles.Count == 0) throw new InvalidOperationException("Unexpected NextDouble().");
            return _doubles.Dequeue();
        }
    }

    /// <summary>Seeded System.Random, for the statistical cross-check against ComputeOdds.</summary>
    private sealed class SeededRandom : ILootRandom
    {
        private readonly Random _random;
        public SeededRandom(int seed) { _random = new Random(seed); }
        public int NextInt(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);
        public double NextDouble() => _random.NextDouble();
    }

    private const int Hit = 0;            // NextInt(0, 1_000_000) = 0 hits any chance > 0
    private const int Miss = 999_999;     // ... misses any chance < 100%

    // Grades 1-10 exactly as GradeDefaults seeds them, ids = stars.
    private static readonly IReadOnlyList<LootGrade> Grades = GradeDefaults.All
        .Select(g => new LootGrade(g.Stars, g.Name, g.Stars, g.DropChance, g.EnchantLevelCapDivisor))
        .ToList();

    private static LootGrade G(int stars) => Grades.Single(g => g.Stars == stars);

    private static LootItem Item(int id, int? stars, decimal weight = 1m, bool stackable = false, bool book = false,
        params LootEnchantment[] defaults) =>
        new(id, $"Item {id}", stars, weight, stackable ? 16 : 1, stackable, book, defaults);

    private static readonly LootEnchantRollSpec Sharpness = Roll(1, 101, "minecraft:sharpness", false, 5, 100m, 1, 5);
    private static readonly LootEnchantRollSpec Knockback = Roll(2, 102, "minecraft:knockback", false, 2, 66m, 1, 2);
    private static readonly LootEnchantRollSpec Poison = Roll(3, 201, "poison", true, 3, 30m, 1, 3, minBoxStars: 3);

    private static LootEnchantRollSpec Roll(int id, int definitionId, string key, bool custom, int definitionMax, decimal chance,
        int min, int max, int minBoxStars = 1, int sortOrder = 0) =>
        new(id, definitionId, key, custom, definitionMax, chance, min, max, minBoxStars, sortOrder == 0 ? id * 10 : sortOrder);

    private static LootboxRollInput Input(IEnumerable<LootItem> pool, int spread = 2,
        IEnumerable<LootEnchantRollSpec>? rolls = null, IEnumerable<LootSpecialSpec>? specials = null) =>
        new(spread, Grades, pool.ToList(), (rolls ?? Array.Empty<LootEnchantRollSpec>()).ToList(), (specials ?? Array.Empty<LootSpecialSpec>()).ToList());

    // DESIGN.md §3.1 worked example: Weapons ★3 Standard Bow / Steel Axe / Steel Sword, ★4 Bladed Steel Sword,
    // ★5 Golemheart Sword.
    private static readonly LootItem[] Weapons =
    {
        Item(1, 3), Item(2, 3), Item(3, 3), Item(4, 4), Item(5, 5),
    };

    // ===== Box grade =====

    [Fact]
    public void BoxGradeDistribution_DefaultsToDropChance_OverOneToFive()
    {
        var distribution = LootboxRollEngine.BoxGradeDistribution(Grades, 1, 5);

        // 70/60/40/25/15 of 210 (DESIGN.md §3.1 spawn-time roll).
        distribution.Select(d => d.Grade.Stars).Should().Equal(1, 2, 3, 4, 5);
        distribution.Select(d => Math.Round(d.Probability * 100, 1)).Should().Equal(33.3, 28.6, 19.0, 11.9, 7.1);
    }

    [Fact]
    public void BoxGradeDistribution_UsesOverridesWhereSet_AndDropsZeroWeights()
    {
        var overrides = new Dictionary<int, decimal> { [G(5).Id] = 70m, [G(1).Id] = 0m };

        var distribution = LootboxRollEngine.BoxGradeDistribution(Grades, 1, 5, overrides);

        distribution.Select(d => d.Grade.Stars).Should().Equal(2, 3, 4, 5);
        distribution.Single(d => d.Grade.Stars == 5).Probability.Should().BeApproximately(70.0 / 195, 1e-12);
    }

    [Fact]
    public void RollBoxGrade_PicksByCumulativeWeight_AndStaysInRange()
    {
        new LootboxRollEngine(new ScriptedRandom().Doubles(0.0)).RollBoxGrade(Grades, 1, 5)!.Stars.Should().Be(1);
        new LootboxRollEngine(new ScriptedRandom().Doubles(0.999)).RollBoxGrade(Grades, 1, 5)!.Stars.Should().Be(5);
        new LootboxRollEngine(new ScriptedRandom().Doubles(0.0)).RollBoxGrade(Grades, 3, 4)!.Stars.Should().Be(3);
        new LootboxRollEngine(new ScriptedRandom()).RollBoxGrade(Grades, 1, 5, Grades.ToDictionary(g => g.Id, _ => 0m))
            .Should().BeNull("nothing has weight");
    }

    // ===== Item grade window =====

    [Fact]
    public void Odds_WeaponsFiveStar_MatchesTheDesignExample()
    {
        var odds = LootboxRollEngine.ComputeOdds(Input(Weapons), 5);

        odds.WindowWidened.Should().BeFalse();
        odds.Grades.Select(g => (g.Grade.Stars, Math.Round(g.Probability * 100, 4), g.ItemCount))
            .Should().Equal((3, 50.0, 3), (4, 31.25, 1), (5, 18.75, 1));
        odds.Items.Single(i => i.Item.BlueprintId == 5).Probability.Should().BeApproximately(0.1875, 1e-12);
        odds.Items.Where(i => i.Grade.Stars == 3).Should().OnlyContain(i => Math.Abs(i.ProbabilityWithinGrade - 1.0 / 3) < 1e-12);
    }

    [Fact]
    public void Roll_TwoStage_GradeByDropChanceThenItemByWeight()
    {
        // 0.49 of the 40:25:15 grade weight is ★3; 0.5 of ★3's three items is the second one.
        var engine = new LootboxRollEngine(new ScriptedRandom().Doubles(0.49, 0.5));
        var result = engine.Roll(Input(Weapons), 5);
        (result.ItemGrade!.Stars, result.Item.BlueprintId, result.IsSpecial).Should().Be((3, 2, false));

        engine = new LootboxRollEngine(new ScriptedRandom().Doubles(0.51, 0.0));
        engine.Roll(Input(Weapons), 5).Item.BlueprintId.Should().Be(4);

        engine = new LootboxRollEngine(new ScriptedRandom().Doubles(0.82, 0.0));
        engine.Roll(Input(Weapons), 5).Item.BlueprintId.Should().Be(5);
    }

    [Fact]
    public void Roll_MoreItemsOfAGrade_DoNotDiluteTheOtherGrades()
    {
        var crowded = Weapons.Concat(Enumerable.Range(10, 20).Select(id => Item(id, 3))).ToList();

        var odds = LootboxRollEngine.ComputeOdds(Input(crowded), 5);

        odds.Grades.Single(g => g.Grade.Stars == 5).Probability.Should().BeApproximately(0.1875, 1e-12);
    }

    [Fact]
    public void Roll_ItemWeightOverride_SkewsTheItemWithinItsGrade()
    {
        var pool = new[] { Item(1, 3, weight: 3m), Item(2, 3, weight: 1m) };

        var odds = LootboxRollEngine.ComputeOdds(Input(pool), 3);

        odds.Items.Single(i => i.Item.BlueprintId == 1).ProbabilityWithinGrade.Should().BeApproximately(0.75, 1e-12);
        new LootboxRollEngine(new ScriptedRandom().Doubles(0.0, 0.8)).Roll(Input(pool), 3).Item.BlueprintId.Should().Be(2);
    }

    [Fact]
    public void Window_IsClampedAtOneStar()
    {
        var pool = new[] { Item(1, 1), Item(2, 2), Item(3, 3) };

        var odds = LootboxRollEngine.ComputeOdds(Input(pool, spread: 2), 1);

        odds.Grades.Select(g => g.Grade.Stars).Should().Equal(1);
        odds.WindowWidened.Should().BeFalse();
        LootboxRollEngine.ComputeOdds(Input(pool, spread: 2), 2).Grades.Select(g => g.Grade.Stars).Should().Equal(1, 2);
    }

    [Fact]
    public void Window_EmptyWidensDownwardFirst()
    {
        // ★5 box, spread 1: window ★4-★5 is empty; ★3 is the nearest lower grade with items (★1 isn't reached).
        var pool = new[] { Item(1, 1), Item(2, 3) };

        var odds = LootboxRollEngine.ComputeOdds(Input(pool, spread: 1), 5);

        odds.WindowWidened.Should().BeTrue();
        odds.Grades.Select(g => (g.Grade.Stars, g.Probability)).Should().Equal((3, 1.0));
        new LootboxRollEngine(new ScriptedRandom().Doubles(0.0, 0.0)).Roll(Input(pool, spread: 1), 5)
            .Should().Match<LootRollResult>(r => r.Item.BlueprintId == 2 && r.WindowWidened);
    }

    [Fact]
    public void Window_EmptyWithNothingBelow_WidensUpward()
    {
        var pool = new[] { Item(1, 4), Item(2, 5) };

        var odds = LootboxRollEngine.ComputeOdds(Input(pool, spread: 2), 2);

        odds.WindowWidened.Should().BeTrue();
        odds.Grades.Select(g => g.Grade.Stars).Should().Equal(4);
    }

    [Fact]
    public void Roll_EmptyPoolAndNoSpecialHit_Throws()
    {
        var engine = new LootboxRollEngine(new ScriptedRandom());

        var act = () => engine.Roll(Input(Array.Empty<LootItem>()), 5);

        act.Should().Throw<InvalidOperationException>();
        LootboxRollEngine.ComputeOdds(Input(Array.Empty<LootItem>()), 5).Grades.Should().BeEmpty();
    }

    // ===== Specials =====

    private static LootSpecialSpec Special(int entryId, int blueprintId, int? stars, int chancePerMillion, int minBoxStars = 5, int sortOrder = 0,
        params LootEnchantment[] defaults) =>
        new(entryId, Item(blueprintId, stars, defaults: defaults), chancePerMillion, minBoxStars, sortOrder);

    [Fact]
    public void Specials_AreCheckedInSortOrder_FirstHitWins_AsIs()
    {
        var samurai = Special(1, 900, 5, 500, sortOrder: 0,
            defaults: new[] { new LootEnchantment(101, "minecraft:sharpness", false, 5), new LootEnchantment(301, "strength", true, 2) });
        var skull = Special(2, 901, 5, 2000, sortOrder: 10);
        var random = new ScriptedRandom().Ints(Miss, Hit);

        var result = new LootboxRollEngine(random).Roll(Input(Weapons, rolls: new[] { Sharpness }, specials: new[] { skull, samurai }), 5);

        (result.IsSpecial, result.SpecialEntryId, result.Item.BlueprintId).Should().Be((true, 2, 901));
        random.IntsLeft.Should().Be(0);
        random.DoublesLeft.Should().Be(0, "a special skips the grade/item/enchant rolls");

        random = new ScriptedRandom().Ints(Hit);
        result = new LootboxRollEngine(random).Roll(Input(Weapons, rolls: new[] { Sharpness }, specials: new[] { skull, samurai }), 5);
        result.SpecialEntryId.Should().Be(1);
        result.Enchantments.Select(e => (e.Key, e.Level)).Should().Equal(("minecraft:sharpness", 5), ("strength", 2));
    }

    [Fact]
    public void Specials_BelowMinBoxStars_AreNotRolled()
    {
        var random = new ScriptedRandom().Doubles(0.0, 0.0);

        var result = new LootboxRollEngine(random).Roll(Input(Weapons, specials: new[] { Special(1, 900, 5, 1_000_000, minBoxStars: 5) }), 4);

        result.IsSpecial.Should().BeFalse();
    }

    [Fact]
    public void Specials_Ungraded_CountAsFiveStars()
    {
        var result = new LootboxRollEngine(new ScriptedRandom().Ints(Hit))
            .Roll(Input(Weapons, specials: new[] { Special(1, 900, null, 2000) }), 5);

        result.ItemGrade!.Stars.Should().Be(5);
    }

    [Fact]
    public void Odds_Specials_AreFirstHitProbabilities()
    {
        var input = Input(Weapons, specials: new[] { Special(1, 900, 5, 500, sortOrder: 0), Special(2, 901, 5, 2000, sortOrder: 10), Special(3, 902, 5, 2000, minBoxStars: 5, sortOrder: 20) });

        var odds = LootboxRollEngine.ComputeOdds(input, 5);

        odds.Specials.Select(s => s.Special.EntryId).Should().Equal(1, 2, 3);
        odds.Specials[0].Probability.Should().BeApproximately(0.0005, 1e-15);
        odds.Specials[1].Probability.Should().BeApproximately(0.9995 * 0.002, 1e-15);
        odds.NormalRollProbability.Should().BeApproximately(0.9995 * 0.998 * 0.998, 1e-15);
        odds.Items.Sum(i => i.Probability).Should().BeApproximately(odds.NormalRollProbability, 1e-12);
        LootboxRollEngine.ComputeOdds(input, 4).Specials.Should().BeEmpty();
    }

    // ===== Enchantments =====

    [Fact]
    public void Enchant_VanillaCapZero_DropsTheRoll_AndLowGradeSharpnessIsOne()
    {
        // ★3 item: knockback cap 2/3 = 0 -> dropped; sharpness cap 5/3 = 1.
        var pool = new[] { Item(1, 3) };
        var random = new ScriptedRandom().Doubles(0.0, 0.0).Ints(Hit, 5, Hit, 2);

        var result = new LootboxRollEngine(random).Roll(Input(pool, rolls: new[] { Sharpness, Knockback }), 3);

        result.Enchantments.Select(e => (e.Key, e.Level)).Should().Equal(("minecraft:sharpness", 1));
        random.IntsLeft.Should().Be(0);
    }

    [Fact]
    public void Enchant_FiveStarItem_KeepsTheRolledLevel_UpToTheDefinitionMax()
    {
        var pool = new[] { Item(1, 5) };
        var random = new ScriptedRandom().Doubles(0.0, 0.0).Ints(Hit, 4, Hit, 2);

        var result = new LootboxRollEngine(random).Roll(Input(pool, rolls: new[] { Sharpness, Knockback }), 5);

        result.Enchantments.Select(e => (e.Key, e.Level)).Should().Equal(("minecraft:sharpness", 4), ("minecraft:knockback", 2));
    }

    [Fact]
    public void Enchant_UncappedGrade_ClampsOnlyToTheDefinitionMax()
    {
        // ★6 has no divisor (KNG-6): cap = definition max. A roll allowed to 7 (legacy data) still stops at 5.
        var overMax = Roll(9, 101, "minecraft:sharpness", false, 5, 100m, 7, 7);

        LootboxRollEngine.LevelCap(overMax, G(6)).Should().Be(5);
        LootboxRollEngine.LevelCap(overMax, G(4)).Should().Be(2);
        var result = new LootboxRollEngine(new ScriptedRandom().Doubles(0.0, 0.0).Ints(Hit, 7))
            .Roll(Input(new[] { Item(1, 6) }, spread: 0, rolls: new[] { overMax }), 5);
        result.Enchantments.Single().Level.Should().Be(5);
    }

    [Fact]
    public void Enchant_Custom_IsCappedOnlyByTheDefinitionMax()
    {
        // ★3 item, poison (custom, max 3) rolled 3: vanilla would be capped to 1, custom is not (KNG-6 D3).
        var pool = new[] { Item(1, 3) };
        var random = new ScriptedRandom().Doubles(0.0, 0.0).Ints(Hit, 3);

        var result = new LootboxRollEngine(random).Roll(Input(pool, rolls: new[] { Poison }), 3);

        result.Enchantments.Should().ContainSingle().Which.Should().Be(new LootEnchantment(201, "poison", true, 3));
        LootboxRollEngine.LevelCap(Poison, G(1)).Should().Be(3);
    }

    [Fact]
    public void Enchant_RollsBelowTheirMinBoxStars_AreSkipped_AndMissesConsumeNoLevel()
    {
        var pool = new[] { Item(1, 2) };
        // ★2 box: poison (min ★3) never rolls; knockback misses, so no level is drawn.
        var random = new ScriptedRandom().Doubles(0.0, 0.0).Ints(Miss);

        var result = new LootboxRollEngine(random).Roll(Input(pool, rolls: new[] { Knockback, Poison }), 2);

        result.Enchantments.Should().BeEmpty();
        random.IntsLeft.Should().Be(0);
    }

    [Fact]
    public void Enchant_RolledLevels_MergeWithDefaults_AsMax()
    {
        var sharp2 = new LootEnchantment(101, "minecraft:sharpness", false, 2);
        var unbreaking3 = new LootEnchantment(103, "minecraft:unbreaking", false, 3);
        var pool = new[] { Item(1, 5, defaults: new[] { sharp2, unbreaking3 }) };

        // Rolled sharpness 4 beats the default 2; a rolled 1 wouldn't lower it.
        var result = new LootboxRollEngine(new ScriptedRandom().Doubles(0.0, 0.0).Ints(Hit, 4))
            .Roll(Input(pool, rolls: new[] { Sharpness }), 5);
        result.Enchantments.Select(e => (e.Key, e.Level)).Should().Equal(("minecraft:sharpness", 4), ("minecraft:unbreaking", 3));

        result = new LootboxRollEngine(new ScriptedRandom().Doubles(0.0, 0.0).Ints(Hit, 1))
            .Roll(Input(pool, rolls: new[] { Sharpness }), 5);
        result.Enchantments.Select(e => (e.Key, e.Level)).Should().Equal(("minecraft:sharpness", 2), ("minecraft:unbreaking", 3));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Enchant_BooksAndStackables_GetNoRolls_OnlyTheirDefaults(bool book, bool stackable)
    {
        var teaches = new LootEnchantment(101, "minecraft:sharpness", false, 3);
        var pool = new[] { Item(1, 5, stackable: stackable, book: book, defaults: teaches) };
        var random = new ScriptedRandom().Doubles(0.0, 0.0);

        var result = new LootboxRollEngine(random).Roll(Input(pool, rolls: new[] { Sharpness, Knockback }), 5);

        result.Enchantments.Should().Equal(teaches);
        random.IntsLeft.Should().Be(0, "no chance or level was drawn");
        LootboxRollEngine.CanRollEnchantments(pool[0]).Should().BeFalse();
    }

    [Fact]
    public void Roll_Quantity_IsTheBlueprintDefault()
    {
        var bread = Item(1, 3, stackable: true);

        new LootboxRollEngine(new ScriptedRandom().Doubles(0.0, 0.0)).Roll(Input(new[] { bread }), 3).Quantity.Should().Be(16);
    }

    [Fact]
    public void Odds_EnchantLevelRanges_ShowTheCapPerWindowGrade()
    {
        var odds = LootboxRollEngine.ComputeOdds(Input(Weapons, rolls: new[] { Sharpness, Knockback, Poison }), 5);

        var knockback = odds.Enchantments.Single(e => e.Roll.Key == "minecraft:knockback");
        knockback.HitProbability.Should().BeApproximately(0.66, 1e-12);
        knockback.Levels.Select(l => (l.Grade.Stars, l.MinLevel, l.MaxLevel))
            .Should().Equal((3, (int?)null, (int?)null), (4, 1, 1), (5, 1, 2));
        odds.Enchantments.Single(e => e.Roll.Key == "minecraft:sharpness").Levels.Select(l => l.MaxLevel).Should().Equal(1, 2, 5);
        odds.Enchantments.Single(e => e.Roll.Key == "poison").Levels.Select(l => l.MaxLevel).Should().Equal(3, 3, 3);
        LootboxRollEngine.ComputeOdds(Input(Weapons, rolls: new[] { Poison }), 2).Enchantments.Should().BeEmpty();
    }

    [Fact]
    public void Odds_MatchTheRoll_Statistically()
    {
        var specials = new[] { Special(1, 900, 5, 20_000, sortOrder: 0) }; // 2% so it shows up in a short run
        var input = Input(Weapons.Append(Item(6, 4, weight: 3m)), rolls: new[] { Sharpness, Knockback, Poison }, specials: specials);
        var odds = LootboxRollEngine.ComputeOdds(input, 5);
        var engine = new LootboxRollEngine(new SeededRandom(12345));

        const int n = 200_000;
        var counts = new Dictionary<int, int>();
        var knockbackHits = 0;
        var enchantable = 0;
        for (var i = 0; i < n; i++)
        {
            var result = engine.Roll(input, 5);
            counts[result.Item.BlueprintId] = counts.GetValueOrDefault(result.Item.BlueprintId) + 1;
            if (!result.IsSpecial && result.ItemGrade!.Stars == 5)
            {
                enchantable++;
                if (result.Enchantments.Any(e => e.Key == "minecraft:knockback")) knockbackHits++;
            }
        }

        foreach (var item in odds.Items)
            ((double)counts.GetValueOrDefault(item.Item.BlueprintId) / n).Should().BeApproximately(item.Probability, 0.005);
        ((double)counts.GetValueOrDefault(900) / n).Should().BeApproximately(odds.Specials.Single().Probability, 0.002);
        ((double)knockbackHits / enchantable).Should().BeApproximately(0.66, 0.02);
    }

    // ===== Pool (LootboxRollInputBuilder) =====

    private static Category Cat(int id, string name, int? parentId = null) => new() { Id = id, Name = name, ParentCategoryId = parentId };

    private static ItemBlueprint Bp(int id, int? categoryId, int? gradeId, int maxStack = 1, string? tag = null, string icon = "minecraft:iron_sword")
    {
        var blueprint = new ItemBlueprint
        {
            Id = id, Name = $"Bp {id}", CategoryId = categoryId, GradeId = gradeId, MaxStackSize = maxStack,
            IconMaterial = new MinecraftMaterialRef { NamespaceKey = icon },
        };
        if (tag != null) blueprint.Tags.Add(new ItemBlueprintTag { ItemBlueprint = blueprint, Tag = new Tag { Name = tag } });
        return blueprint;
    }

    [Fact]
    public void Pool_IsTheGradedNonSpecialItemsOfTheCategoryTree_AdjustedByEntries()
    {
        var categories = new[] { Cat(1, "Weapons"), Cat(2, "Swords", 1), Cat(3, "Armor"), Cat(4, "Daggers", 2) };
        var blueprints = new[]
        {
            Bp(10, 1, 3),                                        // in
            Bp(11, 2, 4),                                        // in (subcategory)
            Bp(12, 4, 5),                                        // in (grandchild)
            Bp(13, 1, null),                                     // ungraded: out
            Bp(14, 1, 5, tag: LootboxSeed.SpecialTag),           // special: out
            Bp(15, 3, 2),                                        // other category: out ...
            Bp(16, 3, null),                                     // ... unless included with a grade override
            Bp(17, 1, 3),                                        // excluded
            Bp(18, 1, 3),                                        // re-weighted
            Bp(19, null, null),                                  // included without any grade: out
        };
        var type = new LootboxType
        {
            Id = 7, CategoryId = 1, IncludeSubcategories = true,
            PoolEntries =
            {
                new LootboxPoolEntry { ItemBlueprintId = 15, Mode = LootboxPoolMode.Include },
                new LootboxPoolEntry { ItemBlueprintId = 16, Mode = LootboxPoolMode.Include, GradeIdOverride = 2 },
                new LootboxPoolEntry { ItemBlueprintId = 17, Mode = LootboxPoolMode.Exclude },
                new LootboxPoolEntry { ItemBlueprintId = 18, Mode = LootboxPoolMode.Include, WeightOverride = 5m, GradeIdOverride = 4 },
                new LootboxPoolEntry { ItemBlueprintId = 19, Mode = LootboxPoolMode.Include },
            },
        };

        var pool = LootboxRollInputBuilder.BuildPool(type, categories, blueprints);

        pool.Select(i => (i.BlueprintId, i.GradeId, i.Weight)).Should().Equal(
            (10, 3, 1m), (11, 4, 1m), (12, 5, 1m), (15, 2, 1m), (16, 2, 1m), (18, 4, 5m));

        type.IncludeSubcategories = false;
        LootboxRollInputBuilder.BuildPool(type, categories, blueprints).Select(i => i.BlueprintId).Should().Equal(10, 15, 16, 18);
    }

    [Fact]
    public void Pool_ExplicitInclude_CanPutASpecialTaggedBlueprintBackIn()
    {
        var type = new LootboxType { Id = 1, CategoryId = 1, PoolEntries = { new LootboxPoolEntry { ItemBlueprintId = 14, Mode = LootboxPoolMode.Include } } };

        var pool = LootboxRollInputBuilder.BuildPool(type, new[] { Cat(1, "Weapons") }, new[] { Bp(14, 1, 5, tag: "lootbox special") });

        pool.Should().ContainSingle().Which.BlueprintId.Should().Be(14);
    }

    [Fact]
    public void Pool_FlagsBooksAndStackables()
    {
        var type = new LootboxType { Id = 1, CategoryId = 1 };
        var blueprints = new[] { Bp(1, 1, 3, icon: EnchantBookSeed.BookMaterialKey), Bp(2, 1, 3, maxStack: 64), Bp(3, 1, 3) };

        var pool = LootboxRollInputBuilder.BuildPool(type, new[] { Cat(1, "Mixed") }, blueprints);

        pool.Select(i => (i.BlueprintId, i.IsBook, i.IsStackable)).Should().Equal((1, true, false), (2, false, true), (3, false, false));
    }

    [Fact]
    public void CategoryScope_SurvivesAParentCycle()
    {
        var categories = new[] { Cat(1, "A", 2), Cat(2, "B", 1) };

        LootboxRollInputBuilder.CategoryScope(1, true, categories).Should().BeEquivalentTo(new[] { 1, 2 });
    }
}
