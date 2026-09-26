using FluentAssertions;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// docs/specs/items/V1_SEED_DATA.md: ItemBlueprintV1Seed creates the ported v1 catalog with the
/// right grades/categories/enchantments, is idempotent, and reuses (never overwrites) rows that
/// already exist - in particular KitSeed's "Iron Sword"/"Arrow".
/// </summary>
public class ItemBlueprintV1SeedTests
{
    private const int BlueprintCount = 80;

    private readonly string _dbName = $"ItemBlueprintV1SeedTestDb_{Guid.NewGuid()}";

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    private static IMinecraftEnchantmentCatalogService EnchantmentCatalog()
    {
        var catalog = new Mock<IMinecraftEnchantmentCatalogService>();
        catalog.Setup(c => c.GetByNamespaceKey(It.IsAny<string>())).Returns((MinecraftEnchantmentCatalogEntry?)null);
        catalog.Setup(c => c.GetByNamespaceKey("minecraft:power")).Returns(new MinecraftEnchantmentCatalogEntry
        {
            NamespaceKey = "minecraft:power", DisplayName = "Power", Category = "Ranged", MaxLevel = 5, LegacyName = "Power",
        });
        return catalog.Object;
    }

    private async Task SeedAsync(bool withCustomEnchantments = true)
    {
        await using var context = NewContext();
        if (withCustomEnchantments)
        {
            await AbilityDefinition.SeedCanonicalAsync(context);
        }
        await ItemBlueprintV1Seed.SeedCanonicalAsync(context, enchantmentCatalog: EnchantmentCatalog());
    }

    private async Task<Dictionary<string, ItemBlueprint>> LoadBlueprintsAsync(KnKDbContext db) =>
        await db.ItemBlueprints
            .Include(b => b.IconMaterial).Include(b => b.Category).Include(b => b.Grade)
            .Include(b => b.Tags).ThenInclude(t => t.Tag)
            .Include(b => b.DefaultEnchantments).ThenInclude(e => e.EnchantmentDefinition)
            .ToDictionaryAsync(b => b.Name!);

    [Fact]
    public async Task FirstRun_CreatesTheV1Catalog()
    {
        await SeedAsync();

        await using var db = NewContext();
        (await db.ItemBlueprints.CountAsync()).Should().Be(BlueprintCount);
        (await db.Categories.CountAsync()).Should().Be(6);
        (await db.Grades.OrderBy(g => g.Stars).Select(g => g.Name).ToListAsync())
            .Should().Equal("Common", "Uncommon", "Rare", "Epic", "Legendary", "Mythic", "Ascended", "Relic", "Exalted", "Divine");
        (await db.Tags.Select(t => t.Name).ToListAsync()).Should().Equal(ItemBlueprintV1Seed.LegacyTag);
        (await db.Set<ItemBlueprintTag>().CountAsync()).Should().Be(BlueprintCount);
        (await db.Set<ItemBlueprintDefaultEnchantment>().CountAsync()).Should().Be(28);
        (await db.MinecraftMaterialRefs.CountAsync()).Should().Be(55);
        (await db.EnchantmentDefinitions.CountAsync(e => !e.IsCustom)).Should().Be(8);
        (await db.MinecraftEnchantmentRefs.CountAsync()).Should().Be(8);
    }

    [Fact]
    public async Task FirstRun_MapsV1FieldsCorrectly()
    {
        await SeedAsync();

        await using var db = NewContext();
        var blueprints = await LoadBlueprintsAsync(db);

        var golemheart = blueprints["Golemheart Sword"];
        golemheart.IconMaterial!.NamespaceKey.Should().Be("minecraft:diamond_sword");
        golemheart.DefaultDisplayName.Should().Be("&aGolemheart Sword");
        golemheart.Grade!.Stars.Should().Be(5);
        golemheart.Category!.Name.Should().Be("Weapons");
        golemheart.MaxStackSize.Should().Be(1);
        golemheart.DefaultEnchantments.Should().BeEmpty(); // v1 rolled its enchantments per copy

        // Pre-1.13 id + damage value mapped to the flattened material.
        blueprints["Oak Wood"].IconMaterial!.NamespaceKey.Should().Be("minecraft:oak_log");
        blueprints["Oak Wood"].Category!.Name.Should().Be("Resources");
        blueprints["Life amulet"].Category!.Name.Should().Be("Trinkets");
        blueprints["Bread"].MaxStackSize.Should().Be(64);
        blueprints["Halloween Armor Boots"].DefaultDisplayName.Should().Be("&5Halloween armor");
        blueprints["Halloween Armor Boots"].Grade.Should().BeNull();

        // Vanilla + custom (v1 lore "§7poison II") default enchantments.
        blueprints["Poison Pickaxe"].DefaultEnchantments
            .Select(e => (e.EnchantmentDefinition.Key, e.EnchantmentDefinition.IsCustom, e.Level))
            .Should().BeEquivalentTo(new[] { ("minecraft:sharpness", false, 5), ("poison", true, 2) });
        blueprints["Golemheart Helmet"].DefaultEnchantments.Select(e => e.EnchantmentDefinition.Key)
            .Should().BeEquivalentTo("minecraft:aqua_affinity", "minecraft:protection", "minecraft:respiration", "minecraft:unbreaking", "health_boost");
        // v1's above-vanilla levels are kept as-is.
        blueprints["Lavonian Bow"].DefaultEnchantments.Single(e => e.EnchantmentDefinition.Key == "minecraft:power").Level.Should().Be(6);

        blueprints.Values.Should().OnlyContain(b => b.Tags.Single().Tag.Name == ItemBlueprintV1Seed.LegacyTag);
        blueprints.Values.Should().OnlyContain(b => b.DefaultQuantity == 1 && b.BasePriceMin == 0 && b.BasePriceMax == 0);
        blueprints.Values.Should().OnlyContain(b => !b.DefaultDisplayName.Contains('§') && !b.DefaultDisplayName.Contains('Â'));

        // New vanilla definitions get a catalog-backed base ref (falling back to a humanized name).
        var power = await db.EnchantmentDefinitions.Include(e => e.BaseEnchantmentRef).SingleAsync(e => e.Key == "minecraft:power");
        power.DisplayName.Should().Be("Power");
        power.MaxLevel.Should().Be(5);
        power.BaseEnchantmentRef!.NamespaceKey.Should().Be("minecraft:power");
        power.BaseEnchantmentRef.Category.Should().Be("Ranged");
        (await db.EnchantmentDefinitions.SingleAsync(e => e.Key == "minecraft:aqua_affinity")).DisplayName.Should().Be("Aqua Affinity");
    }

    [Fact]
    public async Task SecondRun_CreatesNothing()
    {
        await SeedAsync();
        await SeedAsync();

        await using var db = NewContext();
        (await db.ItemBlueprints.CountAsync()).Should().Be(BlueprintCount);
        (await db.Categories.CountAsync()).Should().Be(6);
        (await db.Grades.CountAsync()).Should().Be(10);
        (await db.Tags.CountAsync()).Should().Be(1);
        (await db.Set<ItemBlueprintTag>().CountAsync()).Should().Be(BlueprintCount);
        (await db.Set<ItemBlueprintDefaultEnchantment>().CountAsync()).Should().Be(28);
        (await db.MinecraftMaterialRefs.CountAsync()).Should().Be(55);
        (await db.EnchantmentDefinitions.CountAsync()).Should().Be(8 + AbilityDefinition.CanonicalCatalog.Count);
        (await db.MinecraftEnchantmentRefs.CountAsync()).Should().Be(8);
    }

    [Fact]
    public async Task AfterKitSeed_KitRowsAreReusedAndLeftUnchanged()
    {
        await using (var context = NewContext())
        {
            await KitSeed.SeedCanonicalAsync(context);
            // A pre-existing vanilla definition keyed the way the dev DB has it.
            context.EnchantmentDefinitions.Add(new EnchantmentDefinition { Key = "minecraft:sharpness", DisplayName = "Sharpness", MaxLevel = 5 });
            await context.SaveChangesAsync();
        }

        await SeedAsync();

        await using var db = NewContext();
        var blueprints = await LoadBlueprintsAsync(db);
        (await db.ItemBlueprints.CountAsync()).Should().Be(BlueprintCount + 9 - 2); // Iron Sword + Arrow shared
        blueprints["Iron Sword"].DefaultDisplayName.Should().Be("§7Iron Sword");
        blueprints["Iron Sword"].Tags.Should().BeEmpty();
        blueprints["Arrow"].Tags.Should().BeEmpty();

        // Existing grades (by stars), categories and enchantment definitions are reused.
        (await db.Grades.CountAsync()).Should().Be(10);
        (await db.Categories.CountAsync()).Should().Be(6);
        blueprints["Steel Sword"].Category!.Id.Should().Be(blueprints["Iron Sword"].Category!.Id);
        (await db.EnchantmentDefinitions.CountAsync(e => e.Key == "minecraft:sharpness")).Should().Be(1);
    }

    [Fact]
    public async Task MissingCustomDefinitions_AreSkippedNotInvented()
    {
        await SeedAsync(withCustomEnchantments: false);

        await using var db = NewContext();
        (await db.EnchantmentDefinitions.CountAsync(e => e.IsCustom)).Should().Be(0);
        (await db.ItemBlueprints.CountAsync()).Should().Be(BlueprintCount);
        (await db.Set<ItemBlueprintDefaultEnchantment>().CountAsync()).Should().Be(28 - 7);
    }

    [Fact]
    public async Task FirstRun_SeedsDropChanceAndCapDivisorForEveryGrade()
    {
        await SeedAsync();

        await using var db = NewContext();
        var grades = await db.Grades.OrderBy(g => g.Stars).ToListAsync();
        grades.Select(g => g.DropChance).Should().Equal(70m, 60m, 40m, 25m, 15m, 8m, 5m, 1m, 0.5m, 0.05m);
        grades.Select(g => g.EnchantLevelCapDivisor).Should().Equal(5, 4, 3, 2, 1, null, null, null, null, null);
    }

    [Fact]
    public async Task ExistingGrades_AreNotUpdated_OnlyMissingStarsAreAdded()
    {
        // A pre-KNG-6 DB: grades 1-5 without the new fields (the migration backfills those, not the seed).
        await using (var context = NewContext())
        {
            for (var stars = 1; stars <= 5; stars++)
                context.Grades.Add(new Grade { Name = "Old " + stars, Stars = stars });
            await context.SaveChangesAsync();
        }

        await SeedAsync();

        await using var db = NewContext();
        var grades = await db.Grades.OrderBy(g => g.Stars).ToListAsync();
        grades.Should().HaveCount(10);
        grades.Take(5).Should().OnlyContain(g => g.Name.StartsWith("Old ") && g.DropChance == null && g.EnchantLevelCapDivisor == null);
        grades.Skip(5).Select(g => g.Name).Should().Equal("Mythic", "Ascended", "Relic", "Exalted", "Divine");
    }
}
