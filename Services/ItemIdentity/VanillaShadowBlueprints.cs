using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.MinecraftCatalog;
using Microsoft.EntityFrameworkCore;

namespace knkwebapi_v2.Services.ItemIdentity;

/// <summary>
/// Item identity (PandiO/knk-workspace#141, production DESIGN §8, decision D11/G4): every obtainable vanilla material
/// gets exactly one blueprint. A material nobody curated gets a generated "vanilla shadow": a plain blueprint (no
/// display name, no lore, so the plugin builds it as the plain vanilla item) in the "Uncurated" category, holding the
/// material's <see cref="ItemBlueprint.VanillaMaterialMatchRefId"/>. Admins curate it later or move the match.
/// Generation only adds: it never changes or removes a blueprint or a match, so running it twice creates nothing new.
/// </summary>
public static class VanillaShadowBlueprints
{
    public const string UncuratedCategoryName = "Uncurated";

    /// <summary>
    /// Items a survival player can't obtain (operator and debug items, technical blocks). Agent decision on #141: a
    /// short fixed list; spawn eggs are excluded by suffix. A shadow for any other creative-only item is harmless
    /// and admins can delete it.
    /// </summary>
    public static readonly IReadOnlySet<string> NotObtainable = new HashSet<string>(StringComparer.Ordinal)
    {
        "minecraft:air", "minecraft:cave_air", "minecraft:void_air",
        "minecraft:barrier", "minecraft:bedrock", "minecraft:light", "minecraft:structure_void",
        "minecraft:command_block", "minecraft:chain_command_block", "minecraft:repeating_command_block",
        "minecraft:command_block_minecart", "minecraft:structure_block", "minecraft:jigsaw",
        "minecraft:debug_stick", "minecraft:knowledge_book", "minecraft:test_block", "minecraft:test_instance_block",
        "minecraft:end_portal_frame", "minecraft:reinforced_deepslate", "minecraft:budding_amethyst",
        "minecraft:petrified_oak_slab", "minecraft:spawner", "minecraft:trial_spawner", "minecraft:vault",
        "minecraft:farmland", "minecraft:dirt_path", "minecraft:frogspawn",
    };

    /// <summary>An item in the latest completed scan that a survival player can obtain.</summary>
    public static bool IsObtainable(string namespaceKey, bool? isItem, MinecraftCatalogStatus status)
    {
        if (isItem != true || status != MinecraftCatalogStatus.Active) return false;
        var key = MinecraftNamespaceKey.TryNormalize(namespaceKey);
        return key != null && !NotObtainable.Contains(key) && !key.EndsWith("_spawn_egg", StringComparison.Ordinal);
    }

    /// <summary>The top-level "Uncurated" category, or null when it doesn't exist yet.</summary>
    public static Task<Category?> FindUncuratedCategoryAsync(KnKDbContext db, CancellationToken ct) =>
        db.Categories.Where(c => c.Name == UncuratedCategoryName && c.ParentCategoryId == null)
            .OrderBy(c => c.Id).FirstOrDefaultAsync(ct);

    /// <summary>
    /// Creates a shadow blueprint for every obtainable material without a match (and the "Uncurated" category when
    /// missing), in one transaction. Two runs at the same time can't both create a material's shadow: the unique
    /// index refuses the second, which then fails as a whole (a <see cref="DbUpdateException"/>; run it again).
    /// </summary>
    public static async Task<ItemShadowGenerationResultDto> GenerateAsync(KnKDbContext db, ILogger logger, CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational() ? await db.Database.BeginTransactionAsync(ct) : null;

        var materials = await db.MinecraftMaterialRefs.AsNoTracking()
            .Select(m => new { m.Id, m.NamespaceKey, m.DisplayName, m.IsItem, m.CatalogStatus, m.MaxStackSize })
            .ToListAsync(ct);
        var obtainable = materials.Where(m => IsObtainable(m.NamespaceKey, m.IsItem, m.CatalogStatus)).ToList();
        var matched = (await db.ItemBlueprints.AsNoTracking()
                .Where(b => b.VanillaMaterialMatchRefId != null)
                .Select(b => b.VanillaMaterialMatchRefId!.Value)
                .ToListAsync(ct))
            .ToHashSet();

        var result = new ItemShadowGenerationResultDto
        {
            ObtainableMaterials = obtainable.Count,
            AlreadyMatched = obtainable.Count(m => matched.Contains(m.Id)),
        };

        var category = await FindUncuratedCategoryAsync(db, ct);
        var missing = obtainable.Where(m => !matched.Contains(m.Id)).OrderBy(m => m.NamespaceKey, StringComparer.Ordinal).ToList();
        if (category == null && missing.Count > 0)
        {
            category = new Category { Name = UncuratedCategoryName };
            db.Categories.Add(category);
            await db.SaveChangesAsync(ct);
            result.CategoryCreated = true;
        }

        foreach (var material in missing)
        {
            var key = MinecraftNamespaceKey.TryNormalize(material.NamespaceKey) ?? material.NamespaceKey;
            db.ItemBlueprints.Add(new ItemBlueprint
            {
                Name = string.IsNullOrWhiteSpace(material.DisplayName) ? MinecraftNamespaceKey.DisplayNameFromKey(key) : material.DisplayName,
                Description = $"Generated vanilla shadow of {key}. Curate it, or move the vanilla match to a curated blueprint.",
                // Plain on purpose: no display name or lore, so the plugin builds exactly the vanilla item.
                DefaultDisplayName = string.Empty,
                DefaultDisplayDescription = string.Empty,
                DefaultQuantity = 1,
                MaxStackSize = material.MaxStackSize is > 0 ? material.MaxStackSize.Value : 64,
                IconMaterialRefId = material.Id,
                VanillaMaterialMatchRefId = material.Id,
                CategoryId = category!.Id,
            });
            result.Created++;
        }
        if (result.Created > 0) await db.SaveChangesAsync(ct);
        if (transaction != null) await transaction.CommitAsync(ct);

        result.UncuratedCategoryId = category?.Id ?? 0;
        logger.LogInformation(
            "Item identity: {Obtainable} obtainable materials, {AlreadyMatched} already matched, {Created} vanilla shadow blueprints created (category {CategoryId})",
            result.ObtainableMaterials, result.AlreadyMatched, result.Created, result.UncuratedCategoryId);
        return result;
    }
}
