using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Models;

/// <summary>
/// An additive, create-only seed of permanent enchantment books (Linear KNG-5;
/// <c>knk-workspace/docs/specs/enchantment-books/ENCHANTMENT_BOOK_APPLICATION.md</c> §3.3). A book is an
/// ordinary <see cref="ItemBlueprint"/> whose icon material is <c>minecraft:enchanted_book</c> and whose
/// first <see cref="ItemBlueprint.DefaultEnchantments"/> entry is the enchantment it teaches - knk-plugin
/// turns such a blueprint into a usable book wherever it builds items (kits, catalog, give command).
/// <para>
/// Same convention as <see cref="ItemBlueprintV1Seed"/>: rows are looked up by natural key (<c>Name</c>,
/// <c>NamespaceKey</c>, enchantment <c>Key</c>) and reused, never updated. Runs after
/// <see cref="AbilityDefinition.SeedCanonicalAsync"/> (custom enchantment definitions) and
/// <see cref="ItemBlueprintV1Seed"/> (the vanilla ones); a definition that doesn't exist is logged and its
/// books skipped, never invented here.
/// </para>
/// </summary>
public static class EnchantBookSeed
{
    public const string CategoryName = "Enchantment Books";
    public const string BookMaterialKey = "minecraft:enchanted_book";

    // Custom levels mirror knk-plugin's EnchantmentRegistry max levels; vanilla ones the vanilla maximum.
    private static readonly (string Key, int MaxLevel)[] Books =
    {
        ("poison", 3),
        ("wither", 3),
        ("freeze", 3),
        ("blindness", 3),
        ("confusion", 3),
        ("strength", 2),
        ("chaos", 1),
        ("flash_chaos", 1),
        ("health_boost", 1),
        ("armor_repair", 1),
        ("resistance", 2),
        ("invisibility", 1),
        ("minecraft:sharpness", 5),
        ("minecraft:protection", 4),
        ("minecraft:efficiency", 5),
        ("minecraft:unbreaking", 3),
        ("minecraft:power", 5),
    };

    private static readonly string[] Roman = { "I", "II", "III", "IV", "V" };

    public static int BookCount => Books.Sum(b => b.MaxLevel);

    public static string BookName(string displayName, int level) => $"Enchanted Book ({displayName} {Roman[level - 1]})";

    public static async Task SeedCanonicalAsync(
        KnKDbContext context,
        IMinecraftMaterialCatalogService? materialCatalog = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var created = new Dictionary<string, int>();
        void Count(string what) => created[what] = created.GetValueOrDefault(what) + 1;

        // --- MinecraftMaterialRef ---
        var material = await context.MinecraftMaterialRefs
            .Where(m => m.NamespaceKey == BookMaterialKey)
            .OrderBy(m => m.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (material == null)
        {
            var entry = materialCatalog?.GetAll()
                .FirstOrDefault(e => string.Equals(e.NamespaceKey, BookMaterialKey, StringComparison.OrdinalIgnoreCase));
            material = new MinecraftMaterialRef
            {
                NamespaceKey = BookMaterialKey,
                Category = entry?.Category ?? "ITEM",
                LegacyName = entry?.LegacyName,
                IconUrl = entry?.IconUrl,
            };
            context.MinecraftMaterialRefs.Add(material);
            Count(nameof(MinecraftMaterialRef));
        }

        // --- Category ---
        var category = await context.Categories
            .Where(c => c.Name == CategoryName)
            .OrderBy(c => c.Id)
            .FirstOrDefaultAsync(cancellationToken);
        if (category == null)
        {
            category = new Category { Name = CategoryName, IconMaterialRef = material };
            context.Categories.Add(category);
            Count(nameof(Category));
        }

        // --- ItemBlueprint + its one default enchantment ---
        var definitions = (await context.EnchantmentDefinitions.ToListAsync(cancellationToken))
            .Where(d => !string.IsNullOrWhiteSpace(d.Key))
            .GroupBy(d => NormalizeEnchantmentKey(d.Key!))
            .ToDictionary(g => g.Key, g => g.OrderBy(d => d.Id).First());
        var existingNames = new HashSet<string>(
            (await context.ItemBlueprints.Select(b => b.Name).ToListAsync(cancellationToken)).Where(n => n != null)!,
            StringComparer.OrdinalIgnoreCase);
        var missing = new SortedSet<string>(StringComparer.Ordinal);
        var reused = 0;

        foreach (var (key, maxLevel) in Books)
        {
            if (!definitions.TryGetValue(NormalizeEnchantmentKey(key), out var definition))
            {
                missing.Add(key);
                continue;
            }

            var displayName = string.IsNullOrWhiteSpace(definition.DisplayName) ? key : definition.DisplayName;
            for (var level = 1; level <= maxLevel; level++)
            {
                var name = BookName(displayName, level);
                if (existingNames.Contains(name))
                {
                    reused++;
                    continue;
                }

                var blueprint = new ItemBlueprint
                {
                    Name = name,
                    Description = $"Teaches {displayName} {Roman[level - 1]}.",
                    DefaultDisplayName = $"&d{name}",
                    IconMaterial = material,
                    Category = category,
                    DefaultQuantity = 1,
                    MaxStackSize = 1,
                };
                blueprint.DefaultEnchantments.Add(new ItemBlueprintDefaultEnchantment
                {
                    ItemBlueprint = blueprint,
                    EnchantmentDefinition = definition,
                    Level = level,
                });
                context.ItemBlueprints.Add(blueprint);
                existingNames.Add(name);
                Count(nameof(ItemBlueprint));
            }
        }

        if (missing.Count > 0)
        {
            logger?.LogWarning(
                "EnchantBookSeed: enchantment definitions not found; skipping their books: {Keys}",
                string.Join(", ", missing));
        }

        if (created.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        logger?.LogInformation(
            "EnchantBookSeed complete. Created: {Created}. Existing books left as-is: {Reused}",
            created.Count == 0 ? "nothing" : string.Join(", ", created.Select(kv => $"{kv.Value} {kv.Key}")),
            reused);
    }

    // Same matching as ItemBlueprintV1Seed and the plugin's EnchantmentDefinitionBukkitMapper:
    // "minecraft:*" as-is; custom "poison", "knk:poison" and "Poison" are the same enchantment.
    private static string NormalizeEnchantmentKey(string key)
    {
        var trimmed = key.Trim().ToLowerInvariant();
        if (trimmed.StartsWith("minecraft:", StringComparison.Ordinal)) return trimmed;
        var colon = trimmed.IndexOf(':');
        if (colon >= 0) trimmed = trimmed[(colon + 1)..];
        return trimmed.Replace('-', '_').Replace(' ', '_');
    }
}
