using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Models;

/// <summary>
/// Kits IMPLEMENTATION_PLAN.md Phase 8: an additive, create-only seed of the two example kits
/// ("Default", "Archer") and everything they reference, straight from
/// <c>docs/specs/kits/SEED_DATA.md</c> (the v2 dev-DB backup mapped into v3). Same convention as
/// <see cref="ItemBlueprintExampleCatalogSeed"/>/<see cref="MenuTemplateSeed"/>: every row is looked
/// up by its natural key (<c>Name</c>, or <c>NamespaceKey</c> for <see cref="MinecraftMaterialRef"/>)
/// and reused if it already exists - never updated, diffed or upserted - so a hand-authored
/// "Iron Sword" or "Default" kit is left exactly as the admin made it.
/// <para>
/// Join rows (<see cref="CategoryTag"/>, <see cref="KitContent"/>) are only seeded under a parent
/// (Category/Kit) that this run itself created. A reused parent is treated as admin-owned: adding
/// seed tags/contents to it would change a hand-authored row, and re-adding a slot an admin
/// deliberately removed from a seeded kit on every restart would fight their edits.
/// </para>
/// <para>
/// Unlike the example catalog seed, icons are resolved (<see cref="MinecraftMaterialRef"/>
/// get-or-create by namespace key, same semantics as
/// <c>MinecraftMaterialRefService.GetOrCreateAsync</c>, with Category/LegacyName/IconUrl taken
/// from the static material catalog when it has the key) - these are real, grantable kit items.
/// </para>
/// <para>
/// This is dev/example content, not final balance (SEED_DATA.md's own framing), and it runs in
/// every environment like the other startup seeds. Note "Default" has
/// <see cref="Kit.GrantOnFirstJoin"/> = true, so every new player on any environment receives it.
/// </para>
/// </summary>
public static class KitSeed
{
    // Fallback MinecraftMaterialRef.Category when the static catalog doesn't know the key - every
    // material below is an item, and the catalog itself files them all under "ITEM".
    private const string FallbackMaterialCategory = "ITEM";

    private static readonly (string Name, string IconKey, string[] Tags)[] Categories =
    {
        ("Weapons", "minecraft:iron_sword", new[] { "Open Beta", "Melee" }),
        ("Armor", "minecraft:iron_chestplate", new[] { "Open Beta", "Protection" }),
        ("Food", "minecraft:bread", new[] { "Open Beta" }),
    };

    private static readonly string[] Tags = { "Open Beta", "Test", "Melee", "Protection" };

    private static readonly (string Name, int Stars)[] Grades =
    {
        ("Common", 1),
        ("Uncommon", 2),
    };

    // MaxStackSize is not in SEED_DATA.md; it is set to the vanilla stack size rather than
    // ItemBlueprint's default of 64, since KitGrantPlacer caps each placed stack by it.
    private static readonly (string Name, string IconKey, string Category, string Grade, int DefaultQuantity, int MaxStackSize)[] ItemBlueprints =
    {
        ("Iron Sword", "minecraft:iron_sword", "Weapons", "Common", 1, 1),
        ("Iron Helmet", "minecraft:iron_helmet", "Armor", "Common", 1, 1),
        ("Iron Chestplate", "minecraft:iron_chestplate", "Armor", "Common", 1, 1),
        ("Iron Leggings", "minecraft:iron_leggings", "Armor", "Common", 1, 1),
        ("Iron Boots", "minecraft:iron_boots", "Armor", "Common", 1, 1),
        ("Maggoty Bread", "minecraft:bread", "Food", "Common", 1, 64),
        ("Wooden Bow", "minecraft:bow", "Weapons", "Common", 1, 1),
        ("Arrow", "minecraft:arrow", "Weapons", "Common", 64, 64),
        ("Iron Axe", "minecraft:iron_axe", "Weapons", "Uncommon", 1, 1),
    };

    private sealed record KitSpec(
        string Name,
        string? Helmet,
        string? Chestplate,
        string? Leggings,
        string? Boots,
        string? Shield,
        string? Hand,
        bool GrantOnFirstJoin,
        (int SlotIndex, string ItemBlueprint, int Quantity)[] Contents);

    private static readonly KitSpec[] Kits =
    {
        new("Default",
            "Iron Helmet", "Iron Chestplate", "Iron Leggings", "Iron Boots", Shield: null, Hand: "Iron Sword",
            GrantOnFirstJoin: true,
            new[] { (9, "Maggoty Bread", 1), (10, "Arrow", 64), (11, "Wooden Bow", 1), (12, "Iron Axe", 1) }),
        // HandId = Maggoty Bread is verbatim from the v2 backup, confirmed with the developer
        // (SEED_DATA.md §6) - do not "correct" it to the bow.
        new("Archer",
            "Iron Helmet", "Iron Chestplate", "Iron Leggings", "Iron Boots", Shield: null, Hand: "Maggoty Bread",
            GrantOnFirstJoin: false,
            new[] { (9, "Wooden Bow", 1), (10, "Arrow", 64) }),
    };

    public static async Task SeedCanonicalAsync(
        KnKDbContext context,
        IMinecraftMaterialCatalogService? materialCatalog = null,
        ILogger? logger = null,
        CancellationToken cancellationToken = default)
    {
        var created = new Dictionary<string, int>();
        void Count(string what) => created[what] = created.GetValueOrDefault(what) + 1;

        // --- MinecraftMaterialRef (icons) ---
        var iconKeys = Categories.Select(c => c.IconKey)
            .Concat(ItemBlueprints.Select(b => b.IconKey))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
        var materials = ByKey(
            await context.MinecraftMaterialRefs.Where(m => iconKeys.Contains(m.NamespaceKey)).ToListAsync(cancellationToken),
            m => m.NamespaceKey, m => m.Id);
        var catalogByKey = materialCatalog?.GetAll()
            .GroupBy(e => e.NamespaceKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.OrdinalIgnoreCase)
            ?? new Dictionary<string, MinecraftMaterialCatalogEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (var key in iconKeys)
        {
            if (materials.ContainsKey(key)) continue;
            catalogByKey.TryGetValue(key, out var entry);
            var material = new MinecraftMaterialRef
            {
                NamespaceKey = key,
                Category = entry?.Category ?? FallbackMaterialCategory,
                LegacyName = entry?.LegacyName,
                IconUrl = entry?.IconUrl,
            };
            context.MinecraftMaterialRefs.Add(material);
            materials[key] = material;
            Count(nameof(MinecraftMaterialRef));
        }

        // --- Tag ---
        var tags = ByKey(await context.Tags.ToListAsync(cancellationToken), t => t.Name, t => t.Id);
        foreach (var name in Tags)
        {
            if (tags.ContainsKey(name)) continue;
            var tag = new Tag { Name = name };
            context.Tags.Add(tag);
            tags[name] = tag;
            Count(nameof(Tag));
        }

        // --- Category (+ CategoryTag for newly created categories only) ---
        var categories = ByKey(await context.Categories.ToListAsync(cancellationToken), c => c.Name, c => c.Id);
        foreach (var (name, iconKey, tagNames) in Categories)
        {
            if (categories.ContainsKey(name)) continue;
            var category = new Category { Name = name, IconMaterialRef = materials[iconKey] };
            foreach (var tagName in tagNames)
            {
                category.Tags.Add(new CategoryTag { Category = category, Tag = tags[tagName] });
                Count(nameof(CategoryTag));
            }
            context.Categories.Add(category);
            categories[name] = category;
            Count(nameof(Category));
        }

        // --- Grade ---
        var grades = ByKey(await context.Grades.ToListAsync(cancellationToken), g => g.Name, g => g.Id);
        foreach (var (name, stars) in Grades)
        {
            if (grades.ContainsKey(name)) continue;
            var grade = new Grade { Name = name, Stars = stars };
            context.Grades.Add(grade);
            grades[name] = grade;
            Count(nameof(Grade));
        }

        // --- ItemBlueprint ---
        // Prices, tags and origins are deliberately left unset (not in the source backup).
        var blueprints = ByKey(await context.ItemBlueprints.ToListAsync(cancellationToken), b => b.Name, b => b.Id);
        foreach (var (name, iconKey, categoryName, gradeName, defaultQuantity, maxStackSize) in ItemBlueprints)
        {
            if (blueprints.ContainsKey(name)) continue;
            var blueprint = new ItemBlueprint
            {
                Name = name,
                DefaultDisplayName = "§7" + name,
                IconMaterial = materials[iconKey],
                Category = categories[categoryName],
                Grade = grades[gradeName],
                DefaultQuantity = defaultQuantity,
                MaxStackSize = maxStackSize,
            };
            context.ItemBlueprints.Add(blueprint);
            blueprints[name] = blueprint;
            Count(nameof(ItemBlueprint));
        }

        // --- Kit (+ KitContent for newly created kits only) ---
        // Cooldown, cost and gating are deliberately left unset: freely claimable, as in legacy.
        var kits = ByKey(await context.Kits.ToListAsync(cancellationToken), k => k.Name, k => k.Id);
        foreach (var spec in Kits)
        {
            if (kits.ContainsKey(spec.Name)) continue;
            var kit = new Kit
            {
                Name = spec.Name,
                Helmet = Blueprint(spec.Helmet),
                Chestplate = Blueprint(spec.Chestplate),
                Leggings = Blueprint(spec.Leggings),
                Boots = Blueprint(spec.Boots),
                Shield = Blueprint(spec.Shield),
                Hand = Blueprint(spec.Hand),
                GrantOnFirstJoin = spec.GrantOnFirstJoin,
            };
            foreach (var (slotIndex, blueprintName, quantity) in spec.Contents)
            {
                kit.Contents.Add(new KitContent
                {
                    Kit = kit,
                    SlotIndex = slotIndex,
                    ItemBlueprint = blueprints[blueprintName],
                    Quantity = quantity,
                });
                Count(nameof(KitContent));
            }
            context.Kits.Add(kit);
            kits[spec.Name] = kit;
            Count(nameof(Kit));
        }

        if (created.Count > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        logger?.LogInformation(
            "Kit seed complete. Created: {Created}",
            created.Count == 0 ? "nothing" : string.Join(", ", created.Select(kv => $"{kv.Value} {kv.Key}")));

        ItemBlueprint? Blueprint(string? name) => name == null ? null : blueprints[name];
    }

    // Natural-key lookup: case-insensitive, and if an admin has somehow created two rows with the
    // same name (none of these columns has a unique index), the oldest one wins.
    private static Dictionary<string, T> ByKey<T>(IEnumerable<T> rows, Func<T, string?> key, Func<T, int> id) =>
        rows.Where(r => !string.IsNullOrWhiteSpace(key(r)))
            .GroupBy(r => key(r)!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(id).First(), StringComparer.OrdinalIgnoreCase);
}
