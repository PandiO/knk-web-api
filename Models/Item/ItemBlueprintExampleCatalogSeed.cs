using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace knkwebapi_v2.Models;

/// <summary>
/// IMPLEMENTATION_PLAN.md Phase 8: an additive, create-only seed of real
/// <see cref="ItemBlueprint"/> rows (same convention as <see
/// cref="MenuTemplateSeed"/> - skip if a row with the same <see
/// cref="ItemBlueprint.Name"/> already exists, never diffed/upserted) so
/// <c>example.catalog</c>'s content-source-backed section has something real
/// to page through.
/// <para>
/// This is real production data in a real production entity/table
/// (<c>ItemBlueprint</c> already has full CRUD, a paged search endpoint,
/// and a plugin-side cache-first gateway - see <c>ItemBlueprintsDataAccess</c>
/// and <c>ItemBlueprintsController</c>), not a synthetic table invented for
/// this phase - the "clearly labeled smoke test" is the 18 example rows
/// themselves (a "Trade Goods" catalog nobody asked for), the same way
/// Phase 7's <c>example.presets</c> doubled its Fruit/Berry catalog to 16
/// items specifically to make pagination observable, not because 16 fruits
/// is real game content either. <see cref="IconMaterialRefId"/> is left null
/// on every row, matching Phase 2's own established fallback (a <c>MenuItemTemplate</c>/
/// item with no material ref renders as <c>Material.PAPER</c> with a logged
/// warning) - this phase is demonstrating the paging mechanism, not icon
/// fidelity, so no <c>MinecraftMaterialRef</c> catalog lookup/creation is
/// needed here.
/// </para>
/// </summary>
public static class ItemBlueprintExampleCatalogSeed
{
    private static readonly (string Name, string Description)[] CanonicalCatalog =
    {
        ("Bolt of Silk", "A shimmering bolt of fine silk cloth."),
        ("Sack of Grain", "A heavy burlap sack, full of grain."),
        ("Crate of Spices", "An assortment of rare spices from distant ports."),
        ("Barrel of Ale", "A sealed oak barrel, sloshing when moved."),
        ("Ingot of Iron", "A dense bar of refined iron."),
        ("Ingot of Copper", "A dense bar of refined copper."),
        ("Coil of Rope", "A tightly wound coil of hempen rope."),
        ("Bundle of Furs", "Warm furs bundled and tied with leather cord."),
        ("Jar of Honey", "A clay jar sealed with wax, full of honey."),
        ("Crate of Pottery", "Fragile pottery, carefully packed in straw."),
        ("Bolt of Wool", "A coarse bolt of woven wool cloth."),
        ("Sack of Salt", "A sack of coarse sea salt."),
        ("Crate of Timber", "Freshly cut planks, bundled for transport."),
        ("Ingot of Tin", "A dense bar of refined tin."),
        ("Jug of Oil", "A ceramic jug of pressed olive oil."),
        ("Bundle of Herbs", "Dried herbs tied in a neat bundle."),
        ("Crate of Glassware", "Delicate glassware, carefully packed."),
        ("Sack of Flour", "A sack of finely milled flour."),
    };

    public static async Task SeedCanonicalAsync(KnKDbContext context, ILogger? logger = null, CancellationToken cancellationToken = default)
    {
        var existingNames = await context.ItemBlueprints
            .Select(b => b.Name)
            .ToListAsync(cancellationToken);
        var existingNameSet = new HashSet<string>(existingNames.Where(n => n != null)!, StringComparer.Ordinal);

        var created = 0;
        foreach (var (name, description) in CanonicalCatalog)
        {
            if (existingNameSet.Contains(name)) continue;

            await context.ItemBlueprints.AddAsync(new ItemBlueprint
            {
                Name = name,
                Description = description,
                DefaultDisplayName = name,
                DefaultDisplayDescription = description,
                DefaultQuantity = 1,
                MaxStackSize = 64,
            }, cancellationToken);
            created++;
        }

        if (created > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
        }

        logger?.LogInformation("ItemBlueprint example catalog seed complete. Created: {Created}", created);
    }
}
