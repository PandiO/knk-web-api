using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Models;

/// <summary>
/// An additive, create-only seed of the lootbox configuration (knk-workspace docs/specs/lootboxes/DESIGN.md §3.5,
/// IMPLEMENTATION_PLAN.md Phase 1). Runs after <see cref="EnchantBookSeed"/>, so the categories, grades, the v1
/// one-offs and the enchantment definitions it builds on already exist. Same convention as
/// <see cref="ItemBlueprintV1Seed"/>: rows are looked up by natural key and reused, never updated.
/// <list type="bullet">
/// <item>one <b>disabled</b> <see cref="LootboxType"/> per top-level or leaf <see cref="Category"/> (D9), and for the
/// Weapons/Armor/Tools types this run creates, their enchant rolls (§3.5);</item>
/// <item>the <see cref="SpecialTag"/> tag, the new v3 <b>Flaming Samurai</b> blueprint (§3.5) and a special entry per
/// v1 one-off (§1.4, minus the Donator pickaxe) limited to its own category's box. A special entry's blueprint gets
/// the tag when the entry is created, which keeps it out of the normal pools;</item>
/// <item>the <see cref="LootboxConfiguration"/> singleton.</item>
/// </list>
/// Missing vanilla definitions and the netherite_sword material are created from the <c>Data/</c> catalogs; a missing
/// custom definition (<see cref="AbilityDefinition.SeedCanonicalAsync"/> owns those) is logged and skipped.
/// </summary>
public static class LootboxSeed
{
    public const string SpecialTag = "Lootbox Special";

    public const string FlamingSamuraiName = "Flaming Samurai";
    public const int LegacySpecialChancePerMillion = 2000; // 0.2%
    public const int FlamingSamuraiChancePerMillion = 500; // 0.05%, = ★10's DropChance

    private const string FlamingSamuraiIconKey = "minecraft:netherite_sword";
    private const string WeaponsCategory = "Weapons";
    private const int SpecialMinBoxStars = 5;

    // The v1 one-offs recovered from playerdata (DESIGN.md §1.4); developer Q1: all but the Donator pickaxe.
    private static readonly string[] LegacySpecials =
    {
        "Skull splitter",
        "Lavonian Bow",
        "Golemheart Helmet",
        "Halloween Armor Boots",
        "Halloween Armor Leggings",
        "Pickaxe of Good Health",
        "Poison Pickaxe",
        "Wither Pickaxe",
        "Blindness Pickaxe",
    };

    private static readonly (string Key, int Level)[] FlamingSamuraiEnchantments =
    {
        ("minecraft:sharpness", 5),
        ("minecraft:fire_aspect", 2),
        ("minecraft:sweeping_edge", 3),
        ("minecraft:unbreaking", 3),
        ("strength", 2),
    };

    private sealed record RollSpec(string Key, decimal ChancePercent, int MinLevel, int MaxLevel, int MinBoxStars);

    // DESIGN.md §3.5: the Weapons profile derives from v1's Legendary sword box, bounded by the v3 grade cap; Armor and
    // Tools get a small suggested profile; other categories get none.
    private static readonly Dictionary<string, RollSpec[]> EnchantRolls = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Weapons"] = new RollSpec[]
        {
            new("minecraft:sharpness", 100m, 1, 5, 1),
            new("minecraft:knockback", 66m, 1, 2, 1),
            new("minecraft:fire_aspect", 51m, 1, 2, 1),
            new("minecraft:unbreaking", 46m, 1, 3, 1),
            new("poison", 30m, 1, 3, 3),
            new("blindness", 26m, 1, 3, 3),
            new("confusion", 31m, 1, 3, 3),
            new("armor_repair", 6m, 1, 1, 4),
            new("chaos", 6m, 1, 1, 5),
        },
        ["Armor"] = new RollSpec[]
        {
            new("minecraft:protection", 50m, 1, 4, 1),
            new("minecraft:unbreaking", 40m, 1, 3, 1),
        },
        ["Tools"] = new RollSpec[]
        {
            new("minecraft:efficiency", 50m, 1, 4, 1),
            new("minecraft:unbreaking", 40m, 1, 3, 1),
        },
    };

    public static async Task SeedCanonicalAsync(
        KnKDbContext context,
        IMinecraftMaterialCatalogService? materialCatalog = null,
        IMinecraftEnchantmentCatalogService? enchantmentCatalog = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var created = new Dictionary<string, int>();
        void Count(string what) => created[what] = created.GetValueOrDefault(what) + 1;

        // --- Tag ---
        var specialTag = await context.Tags.OrderBy(t => t.Id).FirstOrDefaultAsync(t => t.Name == SpecialTag, cancellationToken);
        if (specialTag == null)
        {
            specialTag = new Tag { Name = SpecialTag };
            context.Tags.Add(specialTag);
            Count(nameof(Tag));
        }

        // --- Grade (by stars; the earlier seeds normally created all ten) ---
        var grades = (await context.Grades.ToListAsync(cancellationToken))
            .GroupBy(g => g.Stars)
            .ToDictionary(g => g.Key, g => g.OrderBy(x => x.Id).First());
        foreach (var spec in GradeDefaults.All)
        {
            if (grades.ContainsKey(spec.Stars)) continue;
            var grade = spec.ToGrade();
            context.Grades.Add(grade);
            grades[spec.Stars] = grade;
            Count(nameof(Grade));
        }

        // --- EnchantmentDefinition (vanilla ones from the catalog when missing; custom ones only looked up) ---
        var definitions = (await context.EnchantmentDefinitions.ToListAsync(cancellationToken))
            .Where(d => !string.IsNullOrWhiteSpace(d.Key))
            .GroupBy(d => NormalizeEnchantmentKey(d.Key))
            .ToDictionary(g => g.Key, g => g.OrderBy(d => d.Id).First());
        var enchantmentRefs = (await context.MinecraftEnchantmentRefs.ToListAsync(cancellationToken))
            .GroupBy(r => r.NamespaceKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(r => r.Id).First(), StringComparer.OrdinalIgnoreCase);
        var missingCustom = new SortedSet<string>(StringComparer.Ordinal);
        var neededKeys = FlamingSamuraiEnchantments.Select(e => e.Key)
            .Concat(EnchantRolls.Values.SelectMany(r => r).Select(r => r.Key))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        foreach (var key in neededKeys)
        {
            if (definitions.ContainsKey(NormalizeEnchantmentKey(key))) continue;
            if (!key.StartsWith("minecraft:", StringComparison.OrdinalIgnoreCase))
            {
                missingCustom.Add(key);
                continue;
            }

            var catalogEntry = enchantmentCatalog?.GetByNamespaceKey(key);
            if (!enchantmentRefs.TryGetValue(key, out var enchantmentRef))
            {
                enchantmentRef = new MinecraftEnchantmentRef
                {
                    NamespaceKey = key,
                    LegacyName = catalogEntry?.LegacyName,
                    Category = catalogEntry?.Category,
                    IconUrl = catalogEntry?.IconUrl,
                    MaxLevel = catalogEntry?.MaxLevel ?? 1,
                    DisplayName = catalogEntry?.DisplayName ?? ToDisplayName(key),
                    IsCustom = false,
                };
                context.MinecraftEnchantmentRefs.Add(enchantmentRef);
                enchantmentRefs[key] = enchantmentRef;
                Count(nameof(MinecraftEnchantmentRef));
            }

            var definition = new EnchantmentDefinition
            {
                Key = key,
                DisplayName = catalogEntry?.DisplayName ?? ToDisplayName(key),
                Description = string.Empty,
                IsCustom = false,
                MaxLevel = catalogEntry?.MaxLevel ?? 1,
                BaseEnchantmentRef = enchantmentRef,
            };
            context.EnchantmentDefinitions.Add(definition);
            definitions[NormalizeEnchantmentKey(key)] = definition;
            Count(nameof(EnchantmentDefinition));
        }
        if (missingCustom.Count > 0)
        {
            logger?.LogWarning(
                "LootboxSeed: custom enchantment definitions not found (seeded by AbilityDefinition.SeedCanonicalAsync); skipping them: {Keys}",
                string.Join(", ", missingCustom));
        }

        // --- LootboxType: one disabled type per top-level or leaf category (D9) ---
        var categories = await context.Categories.ToListAsync(cancellationToken);
        var types = (await context.LootboxTypes.ToListAsync(cancellationToken)).ToDictionary(t => t.CategoryId);
        var parentIds = categories.Where(c => c.ParentCategoryId != null).Select(c => c.ParentCategoryId!.Value).ToHashSet();
        var typeByCategoryName = new Dictionary<string, LootboxType>(StringComparer.OrdinalIgnoreCase);
        foreach (var category in categories.OrderBy(c => c.Id))
        {
            if (types.TryGetValue(category.Id, out var existingType))
            {
                typeByCategoryName.TryAdd(category.Name, existingType);
                continue;
            }
            if (category.ParentCategoryId != null && parentIds.Contains(category.Id)) continue; // a middle category

            var type = new LootboxType
            {
                Name = $"{category.Name} Lootbox",
                Category = category,
                CategoryId = category.Id,
                Enabled = false,
            };
            AddEnchantRolls(type, category.Name, definitions);
            context.LootboxTypes.Add(type);
            types[category.Id] = type;
            typeByCategoryName.TryAdd(category.Name, type);
            Count(nameof(LootboxType));
            created[nameof(LootboxEnchantRoll)] = created.GetValueOrDefault(nameof(LootboxEnchantRoll)) + type.EnchantRolls.Count;
        }

        // --- Flaming Samurai (a new v3 item, DESIGN.md §3.5) ---
        var blueprints = (await context.ItemBlueprints.Include(b => b.Category).ToListAsync(cancellationToken))
            .Where(b => !string.IsNullOrWhiteSpace(b.Name))
            .GroupBy(b => b.Name!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(b => b.Id).First(), StringComparer.OrdinalIgnoreCase);
        var weapons = categories.Where(c => c.Name == WeaponsCategory).OrderBy(c => c.Id).FirstOrDefault();
        if (!blueprints.ContainsKey(FlamingSamuraiName))
        {
            if (weapons == null)
            {
                logger?.LogWarning("LootboxSeed: no {Category} category; the {Item} blueprint is not created.", WeaponsCategory, FlamingSamuraiName);
            }
            else
            {
                var samurai = new ItemBlueprint
                {
                    Name = FlamingSamuraiName,
                    DefaultDisplayName = "&cFlaming Samurai",
                    DefaultDisplayDescription = "&7Forged in the last fire of a fallen dojo.\n&7Its edge never cools.",
                    Description = "New v3 lootbox special (2026-09-26). Not a v1 port.",
                    IconMaterial = await MaterialAsync(context, materialCatalog, FlamingSamuraiIconKey, Count, cancellationToken),
                    Category = weapons,
                    Grade = grades[5],
                    DefaultQuantity = 1,
                    MaxStackSize = 1,
                };
                samurai.Tags.Add(new ItemBlueprintTag { ItemBlueprint = samurai, Tag = specialTag });
                Count(nameof(ItemBlueprintTag));
                foreach (var (key, level) in FlamingSamuraiEnchantments)
                {
                    if (!definitions.TryGetValue(NormalizeEnchantmentKey(key), out var definition)) continue;
                    samurai.DefaultEnchantments.Add(new ItemBlueprintDefaultEnchantment
                    {
                        ItemBlueprint = samurai,
                        EnchantmentDefinition = definition,
                        Level = level,
                    });
                }
                context.ItemBlueprints.Add(samurai);
                blueprints[FlamingSamuraiName] = samurai;
                Count(nameof(ItemBlueprint));
            }
        }

        // --- LootboxSpecialEntry: the Flaming Samurai first, then the v1 one-offs, each in its own category's box ---
        // Natural key: the blueprint. An entry an admin moved to another type (or made type-less) is left alone.
        var existingEntries = (await context.LootboxSpecialEntries.Select(e => e.ItemBlueprintId).ToListAsync(cancellationToken))
            .ToHashSet();
        var taggedBlueprintIds = specialTag.Id == 0
            ? new HashSet<int>()
            : (await context.Set<ItemBlueprintTag>().Where(t => t.TagId == specialTag.Id).Select(t => t.ItemBlueprintId).ToListAsync(cancellationToken)).ToHashSet();
        var specials = new List<(string Name, int ChancePerMillion, int SortOrder)> { (FlamingSamuraiName, FlamingSamuraiChancePerMillion, 0) };
        specials.AddRange(LegacySpecials.Select((name, i) => (name, LegacySpecialChancePerMillion, (i + 1) * 10)));
        var skipped = new List<string>();
        foreach (var (name, chance, sortOrder) in specials)
        {
            if (!blueprints.TryGetValue(name, out var blueprint))
            {
                skipped.Add($"{name} (no blueprint)");
                continue;
            }
            var categoryName = blueprint.Category?.Name;
            if (categoryName == null || !typeByCategoryName.TryGetValue(categoryName, out var type))
            {
                skipped.Add($"{name} (no lootbox type for its category)");
                continue;
            }
            if (existingEntries.Contains(blueprint.Id)) continue;

            context.LootboxSpecialEntries.Add(new LootboxSpecialEntry
            {
                LootboxType = type,
                ItemBlueprint = blueprint,
                ChancePerMillion = chance,
                MinBoxStars = SpecialMinBoxStars,
                Enabled = true,
                SortOrder = sortOrder,
            });
            Count(nameof(LootboxSpecialEntry));

            // A new special entry takes its blueprint out of the normal pools. Existing blueprints are checked against
            // the stored tags; one created by this run (the Flaming Samurai) carries the tag already.
            var alreadyTagged = taggedBlueprintIds.Contains(blueprint.Id) || blueprint.Tags.Any(t => t.Tag == specialTag);
            if (!alreadyTagged)
            {
                context.Set<ItemBlueprintTag>().Add(new ItemBlueprintTag { ItemBlueprint = blueprint, Tag = specialTag });
                Count(nameof(ItemBlueprintTag));
            }
        }
        if (skipped.Count > 0)
        {
            logger?.LogWarning("LootboxSeed: special entries skipped: {Skipped}", string.Join(", ", skipped));
        }

        // --- LootboxConfiguration singleton ---
        if (!await context.LootboxConfigurations.AnyAsync(cancellationToken))
        {
            context.LootboxConfigurations.Add(new LootboxConfiguration { Id = "global" });
            Count(nameof(LootboxConfiguration));
        }

        if (created.Values.Any(v => v > 0))
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        logger?.LogInformation(
            "LootboxSeed complete. Created: {Created}",
            created.Count == 0 ? "nothing" : string.Join(", ", created.Where(kv => kv.Value > 0).Select(kv => $"{kv.Value} {kv.Key}")));
    }

    private static void AddEnchantRolls(LootboxType type, string categoryName, Dictionary<string, EnchantmentDefinition> definitions)
    {
        if (!EnchantRolls.TryGetValue(categoryName, out var rolls)) return;
        var sortOrder = 0;
        foreach (var roll in rolls)
        {
            sortOrder += 10;
            if (!definitions.TryGetValue(NormalizeEnchantmentKey(roll.Key), out var definition)) continue;
            // Stay inside the service's own validation (MaxLevel <= definition max) even if a definition was edited.
            var maxLevel = Math.Min(roll.MaxLevel, Math.Max(1, definition.MaxLevel));
            type.EnchantRolls.Add(new LootboxEnchantRoll
            {
                LootboxType = type,
                EnchantmentDefinition = definition,
                ChancePercent = roll.ChancePercent,
                MinLevel = Math.Min(roll.MinLevel, maxLevel),
                MaxLevel = maxLevel,
                MinBoxStars = roll.MinBoxStars,
                SortOrder = sortOrder,
            });
        }
    }

    private static async Task<MinecraftMaterialRef> MaterialAsync(
        KnKDbContext context,
        IMinecraftMaterialCatalogService? materialCatalog,
        string key,
        Action<string> count,
        CancellationToken cancellationToken)
    {
        var material = await context.MinecraftMaterialRefs
            .Where(m => m.NamespaceKey == key)
            .OrderBy(m => m.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (material != null) return material;

        var entry = materialCatalog?.GetAll()
            .FirstOrDefault(e => string.Equals(e.NamespaceKey, key, StringComparison.OrdinalIgnoreCase));
        material = new MinecraftMaterialRef
        {
            NamespaceKey = key,
            Category = entry?.Category ?? "ITEM",
            LegacyName = entry?.LegacyName,
            IconUrl = entry?.IconUrl,
        };
        context.MinecraftMaterialRefs.Add(material);
        count(nameof(MinecraftMaterialRef));
        return material;
    }

    // Same matching as ItemBlueprintV1Seed/EnchantBookSeed and the plugin's EnchantmentDefinitionBukkitMapper:
    // "minecraft:*" as-is; custom "poison", "knk:poison" and "Poison" are the same enchantment.
    private static string NormalizeEnchantmentKey(string key)
    {
        var trimmed = key.Trim().ToLowerInvariant();
        if (trimmed.StartsWith("minecraft:", StringComparison.Ordinal)) return trimmed;
        var colon = trimmed.IndexOf(':');
        if (colon >= 0) trimmed = trimmed[(colon + 1)..];
        return trimmed.Replace('-', '_').Replace(' ', '_');
    }

    private static string ToDisplayName(string namespaceKey)
    {
        var key = namespaceKey[(namespaceKey.IndexOf(':') + 1)..];
        return string.Join(' ', key.Split('_', StringSplitOptions.RemoveEmptyEntries)
            .Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }
}
