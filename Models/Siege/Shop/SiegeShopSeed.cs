using knkwebapi_v2.Enums;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.MinecraftCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Models;

/// <summary>
/// Siege shop default catalogue (knk-workspace docs/specs/siege-shop/DESIGN.md § 12.2, answered 2026-11-20;
/// PandiO/knk-workspace#867). PvP Siege sells ender pearl ×1, splash potion of healing II ×1, golden apple ×1 and potion
/// of regeneration ×1 with their brakes, plus one entry per siege weapon template at the template's price; Siege
/// Survival sells the same consumables at ×10 prices. The three categories come from the migration (HasData).
/// <para>
/// Same convention as <see cref="KitSeed"/> for the four item blueprints: looked up by name and reused if they exist,
/// never updated; icons get-or-create by namespace key. The entries are seeded per mode only while that mode has no
/// default entry at all, so an admin who edits or deletes seeded entries is never overridden on the next start. On a
/// fresh database no weapon template exists yet, so the weapon entries only appear where templates were already made;
/// later templates are added to the shop by hand (the seeded lists are not re-run).
/// </para>
/// Real content (not an example): it runs with Seeding:IncludeExamples false too.
/// </summary>
public static class SiegeShopSeed
{
    public const string WeaponsCategoryKey = "siege-weapons";
    public const string ConsumablesCategoryKey = "consumables";

    // MinecraftMaterialRef.Category for a material this seed creates - every material below is an item.
    private const string FallbackMaterialCategory = "ITEM";

    public sealed record BlueprintSpec(string Name, string MaterialKey, string? PotionType, int MaxStackSize);

    public static readonly BlueprintSpec EnderPearl = new("Ender Pearl", "minecraft:ender_pearl", null, 16);
    public static readonly BlueprintSpec HealingSplash = new("Splash Potion of Healing II", "minecraft:splash_potion", "minecraft:strong_healing", 1);
    public static readonly BlueprintSpec GoldenApple = new("Golden Apple", "minecraft:golden_apple", null, 64);
    public static readonly BlueprintSpec Regeneration = new("Potion of Regeneration", "minecraft:potion", "minecraft:regeneration", 1);

    public sealed record EntrySpec(
        BlueprintSpec Blueprint, int Price, int? PriceStep, SiegeShopCurve PriceCurve,
        int? CooldownSeconds, int? CooldownStepSeconds, SiegeShopCurve CooldownCurve, double? CooldownGrowthFactor,
        int? MaxPerMatch, int SortOrder);

    // § 12.2: the PvP Siege consumables. Survival multiplies the price and the price step by 10.
    public static readonly IReadOnlyList<EntrySpec> Consumables = new[]
    {
        new EntrySpec(EnderPearl, 300, 50, SiegeShopCurve.Linear, 20, 10, SiegeShopCurve.Exponential, 1.5, 8, 0),
        new EntrySpec(HealingSplash, 150, 25, SiegeShopCurve.Linear, 10, null, SiegeShopCurve.None, null, 10, 1),
        new EntrySpec(GoldenApple, 200, 50, SiegeShopCurve.Linear, null, null, SiegeShopCurve.None, null, 6, 2),
        new EntrySpec(Regeneration, 250, null, SiegeShopCurve.None, null, null, SiegeShopCurve.None, null, 4, 3),
    };

    public const int SurvivalPriceFactor = 10;

    public static async Task SeedCanonicalAsync(KnKDbContext context, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        var categories = await context.SiegeShopCategories.ToListAsync(cancellationToken);
        var consumables = categories.FirstOrDefault(c => c.Key == ConsumablesCategoryKey);
        var weapons = categories.FirstOrDefault(c => c.Key == WeaponsCategoryKey);

        var modesToSeed = new List<SiegeGameMode>();
        foreach (var mode in new[] { SiegeGameMode.Siege, SiegeGameMode.SiegeSurvival })
        {
            if (!await context.SiegeShopEntries.AnyAsync(e => e.GameMode == mode && e.SiegeScenarioId == null, cancellationToken))
                modesToSeed.Add(mode);
        }
        if (modesToSeed.Count == 0) return;
        if (consumables == null)
        {
            logger?.LogWarning("SiegeShop seed skipped: the '{Key}' category is missing", ConsumablesCategoryKey);
            return;
        }

        var created = new Dictionary<string, int>();
        void Count(string what) => created[what] = created.GetValueOrDefault(what) + 1;

        // --- Item blueprints (and their icon materials) ---
        var specs = Consumables.Select(c => c.Blueprint).ToList();
        var materialKeys = specs.Select(s => s.MaterialKey).Distinct().ToList();
        var materials = (await context.MinecraftMaterialRefs.Where(m => materialKeys.Contains(m.NamespaceKey)).ToListAsync(cancellationToken))
            .GroupBy(m => m.NamespaceKey, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(m => m.Id).First(), StringComparer.OrdinalIgnoreCase);
        foreach (var key in materialKeys.Where(k => !materials.ContainsKey(k)))
        {
            var material = MinecraftMaterialRefFactory.NewUnverified(key, FallbackMaterialCategory);
            context.MinecraftMaterialRefs.Add(material);
            materials[key] = material;
            Count(nameof(MinecraftMaterialRef));
        }

        var names = specs.Select(s => s.Name.ToLower()).ToList();
        var blueprints = (await context.ItemBlueprints.Where(b => b.Name != null && names.Contains(b.Name.ToLower())).ToListAsync(cancellationToken))
            .GroupBy(b => b.Name!.Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.OrderBy(b => b.Id).First(), StringComparer.OrdinalIgnoreCase);
        foreach (var spec in specs.Where(s => !blueprints.ContainsKey(s.Name)))
        {
            var blueprint = new ItemBlueprint
            {
                Name = spec.Name,
                Description = "Siege shop consumable (siege-shop DESIGN § 12.2).",
                DefaultDisplayName = spec.Name,
                IconMaterial = materials[spec.MaterialKey],
                DefaultQuantity = 1,
                MaxStackSize = spec.MaxStackSize,
                PotionType = spec.PotionType,
            };
            context.ItemBlueprints.Add(blueprint);
            blueprints[spec.Name] = blueprint;
            Count(nameof(ItemBlueprint));
        }

        // --- Entries, per mode while it has none ---
        var templates = await context.SiegeWeaponTemplates.OrderBy(t => t.Id).ToListAsync(cancellationToken);
        foreach (var mode in modesToSeed)
        {
            var factor = mode == SiegeGameMode.SiegeSurvival ? SurvivalPriceFactor : 1;
            foreach (var spec in Consumables)
            {
                context.SiegeShopEntries.Add(new SiegeShopEntry
                {
                    GameMode = mode,
                    Category = consumables,
                    Kind = SiegeShopEntryKind.Item,
                    ItemBlueprint = blueprints[spec.Blueprint.Name],
                    Quantity = 1,
                    Price = spec.Price * factor,
                    PriceStep = spec.PriceStep * factor,
                    PriceCurve = spec.PriceCurve,
                    CooldownSeconds = spec.CooldownSeconds,
                    CooldownStepSeconds = spec.CooldownStepSeconds,
                    CooldownCurve = spec.CooldownCurve,
                    CooldownGrowthFactor = spec.CooldownGrowthFactor,
                    MaxPerMatch = spec.MaxPerMatch,
                    SortOrder = spec.SortOrder,
                    Enabled = true,
                });
                Count($"{mode} {nameof(SiegeShopEntry)}");
            }

            // § 12.2: the weapon entries at their template prices, PvP Siege only.
            if (mode != SiegeGameMode.Siege || weapons == null) continue;
            var sortOrder = 0;
            foreach (var template in templates)
            {
                context.SiegeShopEntries.Add(new SiegeShopEntry
                {
                    GameMode = mode,
                    Category = weapons,
                    Kind = SiegeShopEntryKind.SiegeWeapon,
                    SiegeWeaponTemplate = template,
                    Quantity = 1,
                    Price = null, // the template's MatchPrice (P19)
                    SortOrder = sortOrder++,
                    Enabled = true,
                });
                Count($"{mode} weapon {nameof(SiegeShopEntry)}");
            }
        }

        await context.SaveChangesAsync(cancellationToken);
        logger?.LogInformation(
            "SiegeShop seed complete. Created: {Created}",
            created.Count == 0 ? "nothing" : string.Join(", ", created.Select(kv => $"{kv.Value} {kv.Key}")));
    }
}
