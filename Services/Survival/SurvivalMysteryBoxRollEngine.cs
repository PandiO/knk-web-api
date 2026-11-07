using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Lootbox;

namespace knkwebapi_v2.Services.Survival
{
    /// <summary>The outcome of one Mystery Box roll: the picked entry, the item entry's enchantments that passed, and the reel (entry ids, last = result).</summary>
    public sealed record SurvivalMysteryBoxRollOutcome(
        SurvivalMysteryBoxEntry Entry,
        IReadOnlyList<SurvivalMysteryBoxEntryEnchantment> RolledEnchantments,
        IReadOnlyList<int> Reel);

    /// <summary>
    /// The pure odds of a Siege Survival Mystery Box (knk-workspace docs/specs/siege-survival/DESIGN.md §8a, S24,
    /// S59; PandiO/knk-workspace#41, link S7a) - the lootbox roll engine's weighted draw adapted to percentage
    /// chances: every entry's ChancePercent is integer basis points (1 % = 100 bp, S1-15), the pool's total must be
    /// exactly 10 000 (the caller validates with SiegeSurvivalReadiness.EvaluatePool), and one uniform draw in
    /// [0, 10 000) picks the entry. For an item entry every SurvivalMysteryBoxEntryEnchantment is then rolled on its
    /// own, by its own chance, so the chances of one entry need not add up. The reel is the in-world carousel
    /// (lootbox DESIGN §3.9): reelLength - 1 passing entries drawn with the same real odds, then the result; never a
    /// fake near miss. Randomness comes from <see cref="ILootRandom"/> so tests can script it.
    /// </summary>
    public static class SurvivalMysteryBoxRollEngine
    {
        public const int TotalBasisPoints = 10_000;

        public static SurvivalMysteryBoxRollOutcome Roll(SurvivalMysteryBoxPool pool, ILootRandom random, int reelLength)
        {
            if (pool == null) throw new ArgumentNullException(nameof(pool));
            if (random == null) throw new ArgumentNullException(nameof(random));
            if (reelLength < 1) throw new ArgumentOutOfRangeException(nameof(reelLength), "The reel needs at least the result.");

            var entries = pool.Entries.OrderBy(e => e.SortOrder).ThenBy(e => e.Id).ToList();
            var weights = entries.Select(e => SurvivalScaling.ToBasisPoints(e.ChancePercent)).ToList();
            var total = weights.Sum();
            if (entries.Count == 0 || total != TotalBasisPoints)
                throw new InvalidOperationException($"Mystery Box pool '{pool.Name}' chances add up to {total / 100.0:0.##} %, not exactly 100 %.");

            var result = Pick(entries, weights, random);

            var rolled = new List<SurvivalMysteryBoxEntryEnchantment>();
            if (pool.Variant == SurvivalMysteryBoxVariant.Items && result.ItemBlueprintId.HasValue)
            {
                foreach (var chance in result.Enchantments.OrderBy(c => c.Id))
                {
                    var bp = SurvivalScaling.ToBasisPoints(chance.ChancePercent);
                    if (random.NextInt(0, TotalBasisPoints) < bp) rolled.Add(chance);
                }
            }

            var reel = new List<int>(reelLength);
            for (var i = 0; i < reelLength - 1; i++) reel.Add(Pick(entries, weights, random).Id);
            reel.Add(result.Id);

            return new SurvivalMysteryBoxRollOutcome(result, rolled, reel);
        }

        /// <summary>One weighted draw: a uniform integer in [0, total) walked through the cumulative basis points.</summary>
        public static SurvivalMysteryBoxEntry Pick(IReadOnlyList<SurvivalMysteryBoxEntry> entries, IReadOnlyList<int> basisPoints, ILootRandom random)
        {
            var draw = random.NextInt(0, TotalBasisPoints);
            var cumulative = 0;
            for (var i = 0; i < entries.Count; i++)
            {
                cumulative += basisPoints[i];
                if (draw < cumulative) return entries[i];
            }
            return entries[entries.Count - 1];
        }
    }
}
