using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Lootbox;
using knkwebapi_v2.Services.Survival;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Survival;

/// <summary>
/// The pure Mystery Box odds (DESIGN §8a, S24, S59; PandiO/knk-workspace#41, link S7a): one weighted draw in basis
/// points, independent enchantment chances, the reel with real odds and the result last, and the exactly-100 % rule.
/// </summary>
public class SurvivalMysteryBoxRollEngineTests
{
    /// <summary>Returns queued values; fails the test if the engine asks for more randomness than expected.</summary>
    private sealed class ScriptedRandom : ILootRandom
    {
        private readonly Queue<int> _ints = new();
        public ScriptedRandom(params int[] values) { foreach (var v in values) _ints.Enqueue(v); }
        public int IntsLeft => _ints.Count;

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (_ints.Count == 0) throw new InvalidOperationException($"Unexpected NextInt({minInclusive}, {maxExclusive}).");
            var value = _ints.Dequeue();
            if (value < minInclusive || value >= maxExclusive)
                throw new InvalidOperationException($"Scripted int {value} is outside [{minInclusive}, {maxExclusive}).");
            return value;
        }

        public double NextDouble() => throw new InvalidOperationException("Unexpected NextDouble().");
    }

    private sealed class SeededRandom : ILootRandom
    {
        private readonly Random _random;
        public SeededRandom(int seed) { _random = new Random(seed); }
        public int NextInt(int minInclusive, int maxExclusive) => _random.Next(minInclusive, maxExclusive);
        public double NextDouble() => _random.NextDouble();
    }

    private static SurvivalMysteryBoxPool ItemsPool() => SurvivalTestData.ItemsPool();   // sword 60.5 % (sharpness II at 25 %), bow 39.5 %

    [Theory]
    [InlineData(0, 401)]
    [InlineData(6049, 401)]
    [InlineData(6050, 402)]
    [InlineData(9999, 402)]
    public void Pick_WalksTheCumulativeBasisPoints(int draw, int expectedEntryId)
    {
        var random = new ScriptedRandom(draw, 9999);   // the draw, then the sharpness chance (missed)
        var outcome = SurvivalMysteryBoxRollEngine.Roll(ItemsPool(), random, 1);
        Assert.Equal(expectedEntryId, outcome.Entry.Id);
        Assert.Equal(new[] { expectedEntryId }, outcome.Reel);
    }

    [Fact]
    public void ItemEntry_RollsEveryEnchantmentOnItsOwn_AndABookEntryNone()
    {
        var pool = ItemsPool();
        pool.Entries.First().Enchantments.Add(new SurvivalMysteryBoxEntryEnchantment { Id = 4099, EnchantmentDefinitionId = SurvivalTestData.EnchantPower, Level = 1, ChancePercent = 50 });

        // Sword (draw 0); sharpness 25 % hits at 2499, misses at 2500; power 50 % hits at 4999.
        var hitBoth = SurvivalMysteryBoxRollEngine.Roll(pool, new ScriptedRandom(0, 2499, 4999), 1);
        Assert.Equal(new[] { SurvivalTestData.EnchantSharpness, SurvivalTestData.EnchantPower }, hitBoth.RolledEnchantments.Select(c => c.EnchantmentDefinitionId));
        Assert.Equal(new[] { 2, 1 }, hitBoth.RolledEnchantments.Select(c => c.Level));

        var onlyPower = SurvivalMysteryBoxRollEngine.Roll(pool, new ScriptedRandom(0, 2500, 0), 1);
        Assert.Equal(new[] { SurvivalTestData.EnchantPower }, onlyPower.RolledEnchantments.Select(c => c.EnchantmentDefinitionId));

        var none = SurvivalMysteryBoxRollEngine.Roll(pool, new ScriptedRandom(0, 2500, 5000), 1);
        Assert.Empty(none.RolledEnchantments);

        // The bow has no enchantment chances: nothing more is drawn.
        var bow = new ScriptedRandom(9999);
        Assert.Empty(SurvivalMysteryBoxRollEngine.Roll(pool, bow, 1).RolledEnchantments);
        Assert.Equal(0, bow.IntsLeft);

        // An enchanted-book pool never rolls extra enchantments: the book's enchantment is the entry itself.
        var book = new ScriptedRandom(7000);
        var bookOutcome = SurvivalMysteryBoxRollEngine.Roll(SurvivalTestData.BooksPool(), book, 1);
        Assert.Equal(412, bookOutcome.Entry.Id);
        Assert.Empty(bookOutcome.RolledEnchantments);
        Assert.Equal(0, book.IntsLeft);
    }

    [Fact]
    public void Reel_HasTheConfiguredLength_PassingEntriesWithRealOdds_AndTheResultLast()
    {
        // Result: sword (0), sharpness missed (9999); then four passing draws, then nothing else.
        var random = new ScriptedRandom(0, 9999, 9999, 0, 6050, 100);
        var outcome = SurvivalMysteryBoxRollEngine.Roll(ItemsPool(), random, 5);
        Assert.Equal(new[] { 402, 401, 402, 401, 401 }, outcome.Reel);
        Assert.Equal(401, outcome.Entry.Id);
        Assert.Equal(0, random.IntsLeft);
    }

    [Fact]
    public void Reel_NeedsAtLeastTheResult()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SurvivalMysteryBoxRollEngine.Roll(ItemsPool(), new ScriptedRandom(0), 0));
    }

    [Theory]
    [InlineData(60.49)]
    [InlineData(60.51)]
    public void Pool_NotExactly100Percent_IsRejected(double swordPercent)
    {
        var pool = ItemsPool();
        pool.Entries.First().ChancePercent = (decimal)swordPercent;
        var ex = Assert.Throws<InvalidOperationException>(() => SurvivalMysteryBoxRollEngine.Roll(pool, new ScriptedRandom(0), 1));
        Assert.Contains("not exactly 100 %", ex.Message);
    }

    [Fact]
    public void EmptyPool_IsRejected()
    {
        var pool = new SurvivalMysteryBoxPool { Id = 1, Name = "Empty", Variant = SurvivalMysteryBoxVariant.Items };
        Assert.Throws<InvalidOperationException>(() => SurvivalMysteryBoxRollEngine.Roll(pool, new ScriptedRandom(0), 1));
    }

    [Fact]
    public void Odds_OverManyRolls_FollowTheChances()
    {
        var random = new SeededRandom(2026);
        var pool = ItemsPool();
        var swords = 0;
        var sharp = 0;
        var reelSwords = 0;
        const int rolls = 20_000;
        for (var i = 0; i < rolls; i++)
        {
            var outcome = SurvivalMysteryBoxRollEngine.Roll(pool, random, 4);
            Assert.Equal(4, outcome.Reel.Count);
            Assert.Equal(outcome.Entry.Id, outcome.Reel[^1]);
            if (outcome.Entry.Id == 401)
            {
                swords++;
                if (outcome.RolledEnchantments.Count == 1) sharp++;
            }
            else
            {
                Assert.Empty(outcome.RolledEnchantments);
            }
            reelSwords += outcome.Reel.Take(3).Count(id => id == 401);
        }
        Assert.InRange(swords / (double)rolls, 0.59, 0.62);                 // 60.5 %
        Assert.InRange(sharp / (double)swords, 0.23, 0.27);                 // 25 % of the swords
        Assert.InRange(reelSwords / (3.0 * rolls), 0.59, 0.62);             // passing items drawn with the same odds
    }
}
