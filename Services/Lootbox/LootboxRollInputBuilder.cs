using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;

namespace knkwebapi_v2.Services.Lootbox;

/// <summary>
/// Turns a type's EF rows into the engine's plain <see cref="LootboxRollInput"/> (docs/specs/lootboxes/DESIGN.md
/// §3.1 step 3). Pure, so the pool rules are unit-testable without a database:
/// <list type="bullet">
/// <item>the pool is every blueprint in the type's category (plus descendants when IncludeSubcategories) that has a
/// grade and isn't tagged <c>Lootbox Special</c>;</item>
/// <item>an Include entry adds any blueprint (or re-weights one already in), with its GradeIdOverride if set - an
/// ungraded blueprint without an override stays out;</item>
/// <item>an Exclude entry removes one.</item>
/// </list>
/// </summary>
public static class LootboxRollInputBuilder
{
    public static LootboxRollInput Build(
        LootboxType type,
        IEnumerable<Category> categories,
        IEnumerable<Grade> grades,
        IEnumerable<ItemBlueprint> blueprints,
        IEnumerable<LootboxSpecialEntry> specials)
    {
        var lootGrades = grades.Select(ToLootGrade).ToList();
        var pool = BuildPool(type, categories, blueprints);
        var rolls = type.EnchantRolls
            .Where(r => r.EnchantmentDefinition != null)
            .Select(ToRollSpec)
            .ToList();
        var specialSpecs = specials
            .Where(s => s.Enabled && (s.LootboxTypeId == null || s.LootboxTypeId == type.Id) && s.ItemBlueprint != null)
            .Select(s => new LootSpecialSpec(s.Id, ToLootItem(s.ItemBlueprint, s.ItemBlueprint.GradeId, 1m), s.ChancePerMillion, s.MinBoxStars, s.SortOrder))
            .ToList();
        return new LootboxRollInput(type.ItemStarSpread, lootGrades, pool, rolls, specialSpecs);
    }

    /// <summary>The category and, when <paramref name="includeSubcategories"/>, all its descendants.</summary>
    public static HashSet<int> CategoryScope(int categoryId, bool includeSubcategories, IEnumerable<Category> categories)
    {
        var scope = new HashSet<int> { categoryId };
        if (!includeSubcategories) return scope;

        var children = categories
            .Where(c => c.ParentCategoryId != null)
            .GroupBy(c => c.ParentCategoryId!.Value)
            .ToDictionary(g => g.Key, g => g.Select(c => c.Id).ToList());
        var queue = new Queue<int>(new[] { categoryId });
        while (queue.Count > 0)
        {
            if (!children.TryGetValue(queue.Dequeue(), out var kids)) continue;
            // HashSet.Add guards against a (malformed) parent cycle.
            foreach (var kid in kids.Where(scope.Add)) queue.Enqueue(kid);
        }
        return scope;
    }

    public static List<LootItem> BuildPool(LootboxType type, IEnumerable<Category> categories, IEnumerable<ItemBlueprint> blueprints)
    {
        var scope = CategoryScope(type.CategoryId, type.IncludeSubcategories, categories);
        var byId = blueprints.GroupBy(b => b.Id).ToDictionary(g => g.Key, g => g.First());

        var pool = new Dictionary<int, LootItem>();
        foreach (var blueprint in byId.Values)
        {
            if (blueprint.CategoryId is not int categoryId || !scope.Contains(categoryId)) continue;
            if (blueprint.GradeId == null || IsSpecial(blueprint)) continue;
            pool[blueprint.Id] = ToLootItem(blueprint, blueprint.GradeId, 1m);
        }

        foreach (var entry in type.PoolEntries.OrderBy(p => p.ItemBlueprintId))
        {
            if (entry.Mode == LootboxPoolMode.Exclude || !byId.TryGetValue(entry.ItemBlueprintId, out var blueprint))
            {
                pool.Remove(entry.ItemBlueprintId);
                continue;
            }

            var gradeId = entry.GradeIdOverride ?? blueprint.GradeId;
            if (gradeId == null)
            {
                pool.Remove(entry.ItemBlueprintId);
                continue;
            }
            pool[blueprint.Id] = ToLootItem(blueprint, gradeId, entry.WeightOverride ?? 1m);
        }

        return pool.Values.OrderBy(i => i.BlueprintId).ToList();
    }

    public static bool IsSpecial(ItemBlueprint blueprint) =>
        blueprint.Tags.Any(t => t.Tag != null && string.Equals(t.Tag.Name, LootboxSeed.SpecialTag, StringComparison.OrdinalIgnoreCase));

    // Same test as knk-plugin's EnchantBookItems.isBookBlueprint: an enchanted_book blueprint teaches its default
    // enchantment instead of carrying it.
    public static bool IsBook(ItemBlueprint blueprint) =>
        string.Equals(blueprint.IconMaterial?.NamespaceKey, EnchantBookSeed.BookMaterialKey, StringComparison.OrdinalIgnoreCase);

    public static LootItem ToLootItem(ItemBlueprint blueprint, int? gradeId, decimal weight) => new(
        blueprint.Id,
        blueprint.Name ?? blueprint.DefaultDisplayName,
        gradeId,
        weight,
        Math.Max(1, blueprint.DefaultQuantity),
        blueprint.MaxStackSize > 1,
        IsBook(blueprint),
        blueprint.DefaultEnchantments
            .Where(e => e.EnchantmentDefinition != null && e.Level > 0)
            .Select(e => new LootEnchantment(e.EnchantmentDefinitionId, e.EnchantmentDefinition.Key, e.EnchantmentDefinition.IsCustom, e.Level))
            .ToList(),
        blueprint.IconMaterial?.NamespaceKey);

    public static LootGrade ToLootGrade(Grade grade) =>
        new(grade.Id, grade.Name, grade.Stars, grade.DropChance, grade.EnchantLevelCapDivisor);

    public static LootEnchantRollSpec ToRollSpec(LootboxEnchantRoll roll) => new(
        roll.Id,
        roll.EnchantmentDefinitionId,
        roll.EnchantmentDefinition.Key,
        roll.EnchantmentDefinition.IsCustom,
        roll.EnchantmentDefinition.MaxLevel,
        roll.ChancePercent,
        roll.MinLevel,
        roll.MaxLevel,
        roll.MinBoxStars,
        roll.SortOrder);
}
