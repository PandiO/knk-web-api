namespace knkwebapi_v2.Services.Lootbox;

// Plain inputs and outputs of LootboxRollEngine (docs/specs/lootboxes/DESIGN.md §3.1). No EF types, so the engine
// stays pure and the odds preview and the real roll run the same code.

/// <summary>A grade row as the engine sees it. A null DropChance weighs 0.</summary>
public sealed record LootGrade(int Id, string Name, int Stars, decimal? DropChance, int? EnchantLevelCapDivisor);

/// <summary>An enchantment on an item: a blueprint default or a rolled one.</summary>
public sealed record LootEnchantment(int DefinitionId, string Key, bool IsCustom, int Level);

/// <summary>
/// A blueprint that can come out of a box. <paramref name="GradeId"/> is the pool grade (the blueprint's grade or a
/// pool entry's override); null only for an ungraded special. <paramref name="Weight"/> is relative among the pool
/// items of the same grade. Books and stackable items never get rolled enchantments.
/// </summary>
public sealed record LootItem(
    int BlueprintId,
    string Name,
    int? GradeId,
    decimal Weight,
    int Quantity,
    bool IsStackable,
    bool IsBook,
    IReadOnlyList<LootEnchantment> DefaultEnchantments);

/// <summary>One <c>LootboxEnchantRoll</c> row plus its definition's key, kind and max level.</summary>
public sealed record LootEnchantRollSpec(
    int RollId,
    int DefinitionId,
    string Key,
    bool IsCustom,
    int DefinitionMaxLevel,
    decimal ChancePercent,
    int MinLevel,
    int MaxLevel,
    int MinBoxStars,
    int SortOrder);

/// <summary>One enabled special entry that applies to the type (its own or type-less).</summary>
public sealed record LootSpecialSpec(int EntryId, LootItem Item, int ChancePerMillion, int MinBoxStars, int SortOrder);

/// <summary>Everything a claim-time roll needs for one type.</summary>
public sealed record LootboxRollInput(
    int ItemStarSpread,
    IReadOnlyList<LootGrade> Grades,
    IReadOnlyList<LootItem> Pool,
    IReadOnlyList<LootEnchantRollSpec> EnchantRolls,
    IReadOnlyList<LootSpecialSpec> Specials);

/// <summary>
/// The outcome of one claim roll. <paramref name="Enchantments"/> is the item's final set: the blueprint defaults
/// merged with the rolled ones as max(default, rolled). <paramref name="ItemGrade"/> is null only when a special is
/// ungraded and no ★5 grade exists. <paramref name="WindowWidened"/> is true when no grade in the box's window had
/// items and the engine fell back to a nearer one.
/// </summary>
public sealed record LootRollResult(
    LootItem Item,
    LootGrade? ItemGrade,
    bool IsSpecial,
    int? SpecialEntryId,
    int Quantity,
    IReadOnlyList<LootEnchantment> Enchantments,
    bool WindowWidened);

/// <summary>A grade and its probability.</summary>
public sealed record LootGradeChance(LootGrade Grade, double Probability);

/// <summary>A special's chance of being the first hit, given the ones before it missed.</summary>
public sealed record LootSpecialOdds(LootSpecialSpec Special, double Probability);

/// <summary>
/// A pool grade in the item roll: <paramref name="Probability"/> is within the normal (non-special) roll, as the
/// admin sets it up; <paramref name="ItemCount"/> is how many pool items it has.
/// </summary>
public sealed record LootGradeOdds(LootGrade Grade, double Probability, int ItemCount);

/// <summary>An item's chance within its grade and overall (after the special check).</summary>
public sealed record LootItemOdds(LootItem Item, LootGrade Grade, double ProbabilityWithinGrade, double Probability);

/// <summary>The level range an enchant roll can actually produce on an item of <paramref name="Grade"/>; null = always dropped.</summary>
public sealed record LootEnchantLevelRange(LootGrade Grade, int? MinLevel, int? MaxLevel);

/// <summary>An enchant roll that applies to this box grade: its hit chance and effective levels per window grade.</summary>
public sealed record LootEnchantOdds(LootEnchantRollSpec Roll, double HitProbability, IReadOnlyList<LootEnchantLevelRange> Levels);

/// <summary>The full preview for one box grade, computed from the same rules as the roll.</summary>
public sealed record LootOdds(
    int BoxStars,
    IReadOnlyList<LootSpecialOdds> Specials,
    double NormalRollProbability,
    bool WindowWidened,
    IReadOnlyList<LootGradeOdds> Grades,
    IReadOnlyList<LootItemOdds> Items,
    IReadOnlyList<LootEnchantOdds> Enchantments);
