namespace knkwebapi_v2.Services.Lootbox;

/// <summary>
/// The lootbox roll (docs/specs/lootboxes/DESIGN.md §3.1). Pure: every input is a plain record, all randomness comes
/// from the injected <see cref="ILootRandom"/>, so the admin odds preview (<see cref="ComputeOdds"/>) and the real
/// roll (<see cref="Roll"/>) share one set of rules and can't drift.
/// <list type="number">
/// <item>Box grade (spawn time): weighted among grades with stars in [min, max], by the type's override weight or
/// else <c>Grade.DropChance</c>.</item>
/// <item>Specials: in SortOrder, each an independent ChancePerMillion roll for boxes of at least MinBoxStars; the
/// first hit wins and is given as-is.</item>
/// <item>Item grade (two-stage): the window [max(1, B - spread), B], keeping only grades with pool items, weighted by
/// DropChance; widened downward, then upward, when empty. Then an item of that grade, weighted by its pool weight.
/// Two-stage so more ★3 items don't dilute the ★5 odds.</item>
/// <item>Enchantments: each roll with MinBoxStars &lt;= B hits on ChancePercent with a uniform level; vanilla levels
/// are clamped to <c>definitionMax / itemGrade.EnchantLevelCapDivisor</c> (KNG-6, dropped when below 1), custom
/// ones only to the definition max; merged with the blueprint defaults as max(default, rolled). A vanilla roll the
/// item's material can't carry, or that conflicts with an enchantment already on it (a default or an earlier roll),
/// is skipped without drawing (<see cref="VanillaEnchantmentRules"/>), so the minted instance never records an
/// enchantment the ItemStack can't hold. Books and stackable items get no rolls.</item>
/// </list>
/// </summary>
public sealed class LootboxRollEngine
{
    public const int MaxBoxStars = 5;
    public const int SpecialFallbackStars = 5;

    private const int PerMillion = 1_000_000;

    private readonly ILootRandom _random;

    public LootboxRollEngine(ILootRandom random)
    {
        _random = random ?? throw new ArgumentNullException(nameof(random));
    }

    // ===== Box grade (spawn time) =====

    /// <summary>
    /// The box-grade distribution: grades with stars in [<paramref name="minStars"/>, <paramref name="maxStars"/>],
    /// weighted by <paramref name="weightOverrides"/> (grade id → weight) where set, else DropChance. Grades with
    /// weight 0 are left out; empty when nothing has weight.
    /// </summary>
    public static IReadOnlyList<LootGradeChance> BoxGradeDistribution(
        IReadOnlyList<LootGrade> grades,
        int minStars,
        int maxStars,
        IReadOnlyDictionary<int, decimal>? weightOverrides = null)
    {
        var weighted = grades
            .Where(g => g.Stars >= minStars && g.Stars <= maxStars)
            .OrderBy(g => g.Stars).ThenBy(g => g.Id)
            .Select(g => (Grade: g, Weight: weightOverrides != null && weightOverrides.TryGetValue(g.Id, out var w) ? w : g.DropChance ?? 0m))
            .Where(x => x.Weight > 0m)
            .ToList();
        var total = weighted.Sum(x => x.Weight);
        if (total <= 0m) return Array.Empty<LootGradeChance>();
        return weighted.Select(x => new LootGradeChance(x.Grade, (double)(x.Weight / total))).ToList();
    }

    /// <summary>Rolls a box grade from <see cref="BoxGradeDistribution"/>; null when no grade has weight.</summary>
    public LootGrade? RollBoxGrade(
        IReadOnlyList<LootGrade> grades,
        int minStars,
        int maxStars,
        IReadOnlyDictionary<int, decimal>? weightOverrides = null)
    {
        var distribution = BoxGradeDistribution(grades, minStars, maxStars, weightOverrides);
        if (distribution.Count == 0) return null;
        return PickWeighted(distribution, c => c.Probability).Grade;
    }

    // ===== Item (claim time) =====

    /// <summary>
    /// Rolls the item for a box of <paramref name="boxStars"/>. Throws <see cref="InvalidOperationException"/> when
    /// no special hits and the pool has no item at all.
    /// </summary>
    public LootRollResult Roll(LootboxRollInput input, int boxStars)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));

        foreach (var special in ApplicableSpecials(input, boxStars))
        {
            if (_random.NextInt(0, PerMillion) < special.ChancePerMillion)
            {
                var grade = SpecialGrade(input.Grades, special.Item);
                return new LootRollResult(special.Item, grade, true, special.EntryId, QuantityOf(special.Item),
                    special.Item.DefaultEnchantments.OrderBy(e => e.DefinitionId).ToList(), false);
            }
        }

        var window = GradeWindow(input, boxStars);
        if (window.Grades.Count == 0)
            throw new InvalidOperationException("The lootbox pool has no graded item to give.");

        var itemGrade = PickWeighted(window.Grades, g => g.Probability).Grade;
        var candidates = PoolOf(input, itemGrade);
        var item = PickWeighted(candidates, i => (double)i.Weight);

        var enchantments = RollEnchantments(input, boxStars, item, itemGrade);
        return new LootRollResult(item, itemGrade, false, null, QuantityOf(item), enchantments, window.Widened);
    }

    private IReadOnlyList<LootEnchantment> RollEnchantments(LootboxRollInput input, int boxStars, LootItem item, LootGrade itemGrade)
    {
        var result = item.DefaultEnchantments
            .GroupBy(e => e.DefinitionId)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(e => e.Level).First());
        if (!CanRollEnchantments(item)) return result.Values.OrderBy(e => e.DefinitionId).ToList();

        foreach (var roll in ApplicableRolls(input, boxStars))
        {
            if (!CanCarry(roll, item) || ConflictsWithAny(roll, result.Values)) continue;
            if (_random.NextInt(0, PerMillion) >= ChancePerMillionOf(roll.ChancePercent)) continue;

            var min = Math.Max(1, Math.Min(roll.MinLevel, roll.MaxLevel));
            var max = Math.Max(min, roll.MaxLevel);
            var level = Clamp(roll, itemGrade, _random.NextInt(min, max + 1));
            if (level < 1) continue;

            if (!result.TryGetValue(roll.DefinitionId, out var current) || current.Level < level)
                result[roll.DefinitionId] = new LootEnchantment(roll.DefinitionId, roll.Key, roll.IsCustom, level);
        }
        return result.Values.OrderBy(e => e.DefinitionId).ToList();
    }

    // ===== Odds preview =====

    /// <summary>The odds of a box of <paramref name="boxStars"/>, by the same rules as <see cref="Roll"/>.</summary>
    public static LootOdds ComputeOdds(LootboxRollInput input, int boxStars)
    {
        if (input == null) throw new ArgumentNullException(nameof(input));

        var specials = new List<LootSpecialOdds>();
        var noHitYet = 1.0;
        foreach (var special in ApplicableSpecials(input, boxStars))
        {
            var p = (double)special.ChancePerMillion / PerMillion;
            specials.Add(new LootSpecialOdds(special, noHitYet * p));
            noHitYet *= 1 - p;
        }

        var window = GradeWindow(input, boxStars);
        var grades = new List<LootGradeOdds>();
        var items = new List<LootItemOdds>();
        foreach (var chance in window.Grades)
        {
            var pool = PoolOf(input, chance.Grade);
            grades.Add(new LootGradeOdds(chance.Grade, chance.Probability, pool.Count));
            var total = pool.Sum(i => (double)i.Weight);
            foreach (var item in pool)
            {
                var withinGrade = (double)item.Weight / total;
                items.Add(new LootItemOdds(item, chance.Grade, withinGrade, noHitYet * chance.Probability * withinGrade));
            }
        }

        var rolls = ApplicableRolls(input, boxStars).ToList();
        var land = new double[rolls.Count];
        var applicableItems = new int[rolls.Count];
        foreach (var itemOdds in items)
        {
            var perItem = LandProbabilities(rolls, itemOdds.Item, itemOdds.Grade);
            for (var r = 0; r < rolls.Count; r++)
            {
                land[r] += itemOdds.Probability * perItem[r];
                if (CanRollEnchantments(itemOdds.Item) && CanCarry(rolls[r], itemOdds.Item)) applicableItems[r]++;
            }
        }

        var enchantments = rolls
            .Select((roll, r) => new LootEnchantOdds(
                roll,
                ChancePerMillionOf(roll.ChancePercent) / (double)PerMillion,
                window.Grades.Select(g => LevelRange(roll, g.Grade)).ToList(),
                applicableItems[r],
                land[r]))
            .ToList();

        return new LootOdds(boxStars, specials, noHitYet, window.Widened, grades, items, enchantments);
    }

    // Largest set of mutually dependent rolls whose hit patterns are enumerated exactly (2^n); beyond it, each
    // conflict is treated as independent. Real configs have a handful of rolls, so this is never reached.
    private const int MaxExactConflictRolls = 16;

    /// <summary>
    /// Per roll (same order as <paramref name="rolls"/>): the chance it lands on <paramref name="item"/> of
    /// <paramref name="grade"/>, by the rules of <see cref="RollEnchantments"/>. A roll that the material can carry
    /// and the cap leaves at least level 1 is eligible; an eligible roll lands when it hits and no enchantment already
    /// on the item conflicts with it. Rolls that can't conflict with another eligible roll land on their own hit
    /// chance (or never, when a default excludes them); the others are enumerated over their hit patterns in order.
    /// </summary>
    private static double[] LandProbabilities(IReadOnlyList<LootEnchantRollSpec> rolls, LootItem item, LootGrade grade)
    {
        var land = new double[rolls.Count];
        if (!CanRollEnchantments(item)) return land;

        var hit = rolls.Select(r => ChancePerMillionOf(r.ChancePercent) / (double)PerMillion).ToArray();
        var eligible = rolls.Select(r => CanCarry(r, item) && LevelCap(r, grade) >= 1).ToArray();
        var involved = Enumerable.Range(0, rolls.Count)
            .Where(i => eligible[i] && Enumerable.Range(0, rolls.Count).Any(j => j != i && eligible[j] && Conflicts(rolls[i], rolls[j])))
            .ToList();

        for (var i = 0; i < rolls.Count; i++)
        {
            if (!eligible[i] || involved.Contains(i)) continue;
            land[i] = ConflictsWithAny(rolls[i], item.DefaultEnchantments) ? 0 : hit[i];
        }
        if (involved.Count == 0) return land;

        if (involved.Count > MaxExactConflictRolls)
        {
            foreach (var i in involved)
            {
                if (ConflictsWithAny(rolls[i], item.DefaultEnchantments)) continue;
                land[i] = involved.Where(j => j < i && Conflicts(rolls[i], rolls[j]))
                    .Aggregate(hit[i], (p, j) => p * (1 - hit[j]));
            }
            return land;
        }

        for (var mask = 0; mask < 1 << involved.Count; mask++)
        {
            var probability = 1.0;
            for (var b = 0; b < involved.Count; b++)
                probability *= (mask & (1 << b)) != 0 ? hit[involved[b]] : 1 - hit[involved[b]];
            if (probability == 0) continue;

            var onItem = item.DefaultEnchantments.ToList();
            for (var b = 0; b < involved.Count; b++)
            {
                var roll = rolls[involved[b]];
                if ((mask & (1 << b)) == 0 || ConflictsWithAny(roll, onItem)) continue;
                land[involved[b]] += probability;
                onItem.Add(new LootEnchantment(roll.DefinitionId, roll.Key, roll.IsCustom, 1));
            }
        }
        return land;
    }

    private static LootEnchantLevelRange LevelRange(LootEnchantRollSpec roll, LootGrade grade)
    {
        var min = Math.Max(1, Math.Min(roll.MinLevel, roll.MaxLevel));
        var max = Math.Max(min, roll.MaxLevel);
        var clampedMin = Clamp(roll, grade, min);
        var clampedMax = Clamp(roll, grade, max);
        return clampedMax < 1
            ? new LootEnchantLevelRange(grade, null, null)
            : new LootEnchantLevelRange(grade, clampedMin, clampedMax);
    }

    // ===== Shared rules =====

    /// <summary>
    /// The level cap of an enchantment on an item of <paramref name="grade"/> (KNG-6, same formula as knk-core's
    /// <c>KnkGrade.capEnchantLevel</c>): vanilla = definitionMax / divisor (integer division; no divisor = the
    /// definition max), custom = the definition max only (KNG-6 D3).
    /// </summary>
    public static int LevelCap(LootEnchantRollSpec roll, LootGrade? grade)
    {
        var definitionMax = Math.Max(0, roll.DefinitionMaxLevel);
        if (roll.IsCustom || grade?.EnchantLevelCapDivisor is not int divisor || divisor < 1) return definitionMax;
        return definitionMax / divisor;
    }

    private static int Clamp(LootEnchantRollSpec roll, LootGrade grade, int level) => Math.Min(level, LevelCap(roll, grade));

    /// <summary>
    /// Whether <paramref name="item"/> can carry the roll's enchantment: custom ones always (lore), vanilla ones when
    /// the item's material supports it; an item of unknown material isn't filtered.
    /// </summary>
    public static bool CanCarry(LootEnchantRollSpec roll, LootItem item) =>
        roll.IsCustom || item.MaterialKey == null || VanillaEnchantmentRules.CanApply(roll.Key, item.MaterialKey);

    private static bool Conflicts(LootEnchantRollSpec a, LootEnchantRollSpec b) =>
        !a.IsCustom && !b.IsCustom && a.DefinitionId != b.DefinitionId && VanillaEnchantmentRules.Conflicts(a.Key, b.Key);

    // Whether a vanilla enchantment of a different definition already on the item excludes the roll.
    private static bool ConflictsWithAny(LootEnchantRollSpec roll, IEnumerable<LootEnchantment> onItem) =>
        !roll.IsCustom && onItem.Any(e => !e.IsCustom && e.DefinitionId != roll.DefinitionId && VanillaEnchantmentRules.Conflicts(e.Key, roll.Key));

    /// <summary>Books teach their enchantment and stackables have no instance to hold one: neither gets rolls.</summary>
    public static bool CanRollEnchantments(LootItem item) => !item.IsBook && !item.IsStackable;

    private static IEnumerable<LootSpecialSpec> ApplicableSpecials(LootboxRollInput input, int boxStars) =>
        input.Specials
            .Where(s => s.MinBoxStars <= boxStars && s.ChancePerMillion > 0)
            .OrderBy(s => s.SortOrder).ThenBy(s => s.EntryId);

    private static IEnumerable<LootEnchantRollSpec> ApplicableRolls(LootboxRollInput input, int boxStars) =>
        input.EnchantRolls
            .Where(r => r.MinBoxStars <= boxStars)
            .OrderBy(r => r.SortOrder).ThenBy(r => r.RollId);

    private static List<LootItem> PoolOf(LootboxRollInput input, LootGrade grade) =>
        input.Pool.Where(i => i.GradeId == grade.Id && i.Weight > 0m).OrderBy(i => i.BlueprintId).ToList();

    private sealed record Window(IReadOnlyList<LootGradeChance> Grades, bool Widened);

    // Grades of the window [max(1, B - spread), B] that have pool items, weighted by DropChance. When none has items,
    // take the nearest lower star count that does, then the nearest higher one.
    private static Window GradeWindow(LootboxRollInput input, int boxStars)
    {
        var withItems = input.Grades
            .Where(g => input.Pool.Any(i => i.GradeId == g.Id && i.Weight > 0m))
            .ToList();

        var low = Math.Max(1, boxStars - Math.Max(0, input.ItemStarSpread));
        var inWindow = withItems.Where(g => g.Stars >= low && g.Stars <= boxStars).ToList();
        var widened = false;
        if (inWindow.Count == 0)
        {
            widened = true;
            var below = withItems.Where(g => g.Stars < low).Select(g => g.Stars).DefaultIfEmpty(0).Max();
            if (below > 0)
            {
                inWindow = withItems.Where(g => g.Stars == below).ToList();
            }
            else
            {
                var above = withItems.Where(g => g.Stars > boxStars).Select(g => g.Stars).DefaultIfEmpty(0).Min();
                inWindow = withItems.Where(g => g.Stars == above).ToList();
            }
        }

        inWindow = inWindow.OrderBy(g => g.Stars).ThenBy(g => g.Id).ToList();
        var total = inWindow.Sum(g => g.DropChance ?? 0m);
        // Every grade in the window has items; if none of them has a DropChance, give them equal odds rather than
        // nothing.
        var chances = total > 0m
            ? inWindow.Where(g => (g.DropChance ?? 0m) > 0m).Select(g => new LootGradeChance(g, (double)(g.DropChance!.Value / total))).ToList()
            : inWindow.Select(g => new LootGradeChance(g, 1.0 / inWindow.Count)).ToList();
        return new Window(chances, widened);
    }

    // An ungraded special counts as ★5 for the cap and the announcement (DESIGN.md §3.5).
    private static LootGrade? SpecialGrade(IReadOnlyList<LootGrade> grades, LootItem item)
    {
        if (item.GradeId is int gradeId)
        {
            var own = grades.FirstOrDefault(g => g.Id == gradeId);
            if (own != null) return own;
        }
        return grades.Where(g => g.Stars == SpecialFallbackStars).OrderBy(g => g.Id).FirstOrDefault();
    }

    private static int QuantityOf(LootItem item) => Math.Max(1, item.Quantity);

    // ChancePercent is decimal(7,4): as parts per million it is an exact integer.
    private static int ChancePerMillionOf(decimal chancePercent) =>
        (int)Math.Round(Math.Clamp(chancePercent, 0m, 100m) * 10_000m, MidpointRounding.AwayFromZero);

    private T PickWeighted<T>(IReadOnlyList<T> options, Func<T, double> weight)
    {
        var total = options.Sum(weight);
        var target = _random.NextDouble() * total;
        foreach (var option in options)
        {
            target -= weight(option);
            if (target < 0) return option;
        }
        return options[^1];
    }
}
