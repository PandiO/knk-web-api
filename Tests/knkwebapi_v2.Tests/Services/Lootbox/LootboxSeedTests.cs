using FluentAssertions;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace knkwebapi_v2.Tests.Services.Lootbox;

/// <summary>
/// Lootboxes Phase 1 (docs/specs/lootboxes/IMPLEMENTATION_PLAN.md "LootboxSeedTests", DESIGN.md §3.5): one disabled type
/// per category, the specials (v1 one-offs minus the Donator pickaxe, plus the new Flaming Samurai), the enchant rolls
/// and the singleton settings; create-only and idempotent.
/// </summary>
public class LootboxSeedTests
{
    private readonly string _dbName = $"LootboxSeedTestDb_{Guid.NewGuid()}";

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    private static readonly MinecraftEnchantmentCatalogService EnchantmentCatalog =
        new(NullLogger<MinecraftEnchantmentCatalogService>.Instance);

    private async Task SeedAllAsync(bool abilities = true)
    {
        await using var context = NewContext();
        if (abilities) await AbilityDefinition.SeedCanonicalAsync(context);
        await ItemBlueprintV1Seed.SeedCanonicalAsync(context, enchantmentCatalog: EnchantmentCatalog);
        await EnchantBookSeed.SeedCanonicalAsync(context);
        await LootboxSeed.SeedCanonicalAsync(context, enchantmentCatalog: EnchantmentCatalog);
    }

    private static async Task<object> SnapshotAsync(KnKDbContext db) => new
    {
        Types = await db.LootboxTypes.CountAsync(),
        Rolls = await db.LootboxEnchantRolls.CountAsync(),
        Specials = await db.LootboxSpecialEntries.CountAsync(),
        Blueprints = await db.ItemBlueprints.CountAsync(),
        Tags = await db.Tags.CountAsync(),
        BlueprintTags = await db.Set<ItemBlueprintTag>().CountAsync(),
        Definitions = await db.EnchantmentDefinitions.CountAsync(),
        Materials = await db.MinecraftMaterialRefs.CountAsync(),
        Configs = await db.LootboxConfigurations.CountAsync(),
    };

    [Fact]
    public async Task FirstRun_CreatesOneDisabledTypePerCategory()
    {
        await SeedAllAsync();

        await using var db = NewContext();
        var categories = await db.Categories.Select(c => c.Id).ToListAsync();
        var types = await db.LootboxTypes.Include(t => t.Category).ToListAsync();
        types.Select(t => t.CategoryId).Should().BeEquivalentTo(categories);
        types.Should().OnlyContain(t => !t.Enabled && t.IncludeSubcategories && t.MinBoxStars == 1 && t.MaxBoxStars == 5
                                        && t.ItemStarSpread == 2 && t.SpawnWeight == 10);
        types.Single(t => t.Category.Name == "Weapons").Name.Should().Be("Weapons Lootbox");
        (await db.LootboxConfigurations.SingleAsync()).Should().BeEquivalentTo(new
        {
            Id = "global", Enabled = true, GlobalMaxActive = 15, MaxClaimsPerPlayerPerDay = (int?)10,
            AnnounceMinItemStars = 5, AnnounceSpawnMinBoxStars = 6,
        });
    }

    [Fact]
    public async Task MiddleCategories_GetNoType()
    {
        await using (var db = NewContext())
        {
            var weapons = new Category { Name = "Gear" };
            var swords = new Category { Name = "Blades", ParentCategory = weapons };
            db.Categories.AddRange(weapons, swords, new Category { Name = "Daggers", ParentCategory = swords });
            await db.SaveChangesAsync();
        }

        await using (var db = NewContext()) await LootboxSeed.SeedCanonicalAsync(db);

        await using var read = NewContext();
        (await read.LootboxTypes.Select(t => t.Category.Name).ToListAsync()).Should().BeEquivalentTo("Gear", "Daggers");
    }

    [Fact]
    public async Task FlamingSamurai_IsCreatedOnce_WithItsFiveEnchantments_TaggedSpecialNotLegacy()
    {
        await SeedAllAsync();
        await using (var again = NewContext()) await LootboxSeed.SeedCanonicalAsync(again, enchantmentCatalog: EnchantmentCatalog);

        await using var db = NewContext();
        var samurai = await db.ItemBlueprints
            .Include(b => b.IconMaterial).Include(b => b.Category).Include(b => b.Grade)
            .Include(b => b.Tags).ThenInclude(t => t.Tag)
            .Include(b => b.DefaultEnchantments).ThenInclude(e => e.EnchantmentDefinition)
            .SingleAsync(b => b.Name == LootboxSeed.FlamingSamuraiName);

        (samurai.DefaultDisplayName, samurai.IconMaterial!.NamespaceKey, samurai.Category!.Name, samurai.Grade!.Stars, samurai.DefaultQuantity, samurai.MaxStackSize)
            .Should().Be(("&cFlaming Samurai", "minecraft:netherite_sword", "Weapons", 5, 1, 1));
        samurai.DefaultDisplayDescription.Should().Be("&7Forged in the last fire of a fallen dojo.\n&7Its edge never cools.");
        samurai.Description.Should().Contain("Not a v1 port");
        samurai.DefaultEnchantments.Select(e => (e.EnchantmentDefinition.Key, e.Level)).Should().BeEquivalentTo(new[]
        {
            ("minecraft:sharpness", 5), ("minecraft:fire_aspect", 2), ("minecraft:sweeping_edge", 3), ("minecraft:unbreaking", 3), ("strength", 2),
        });
        samurai.DefaultEnchantments.Where(e => !e.EnchantmentDefinition.IsCustom)
            .Should().OnlyContain(e => e.Level == e.EnchantmentDefinition.MaxLevel, "every vanilla level is its definition max, the ★5 cap");
        samurai.Tags.Select(t => t.Tag.Name).Should().Equal(LootboxSeed.SpecialTag);

        var entry = await db.LootboxSpecialEntries.Include(s => s.LootboxType).ThenInclude(t => t!.Category).SingleAsync(s => s.ItemBlueprintId == samurai.Id);
        (entry.ChancePerMillion, entry.MinBoxStars, entry.SortOrder, entry.Enabled, entry.LootboxType!.Category.Name)
            .Should().Be((500, 5, 0, true, "Weapons"));
    }

    [Fact]
    public async Task LegacySpecials_GetAnEntryInTheirOwnCategorysBox_MinusTheDonatorPickaxe()
    {
        await SeedAllAsync();

        await using var db = NewContext();
        var entries = await db.LootboxSpecialEntries
            .Include(s => s.ItemBlueprint).ThenInclude(b => b.Category)
            .Include(s => s.ItemBlueprint).ThenInclude(b => b.Tags).ThenInclude(t => t.Tag)
            .Include(s => s.LootboxType)
            .OrderBy(s => s.SortOrder)
            .ToListAsync();

        entries.Select(e => e.ItemBlueprint.Name).Should().Equal(
            "Flaming Samurai", "Skull splitter", "Lavonian Bow", "Golemheart Helmet", "Halloween Armor Boots", "Halloween Armor Leggings",
            "Pickaxe of Good Health", "Poison Pickaxe", "Wither Pickaxe", "Blindness Pickaxe");
        entries.Skip(1).Should().OnlyContain(e => e.ChancePerMillion == 2000 && e.MinBoxStars == 5);
        entries.Skip(1).Select(e => e.SortOrder).Should().Equal(10, 20, 30, 40, 50, 60, 70, 80, 90);
        entries.Should().OnlyContain(e => e.LootboxType!.CategoryId == e.ItemBlueprint.CategoryId);
        entries.Should().OnlyContain(e => e.ItemBlueprint.Tags.Any(t => t.Tag.Name == LootboxSeed.SpecialTag));
        entries.Skip(1).Should().OnlyContain(e => e.ItemBlueprint.Tags.Any(t => t.Tag.Name == ItemBlueprintV1Seed.LegacyTag));

        var donator = await db.ItemBlueprints.Include(b => b.Tags).ThenInclude(t => t.Tag).SingleAsync(b => b.Name == "Donator pickaxe");
        donator.Tags.Select(t => t.Tag.Name).Should().NotContain(LootboxSeed.SpecialTag);
    }

    [Fact]
    public async Task EnchantRolls_WeaponsArmorAndTools_PerTheDesignProfile()
    {
        await SeedAllAsync();

        await using var db = NewContext();
        var rolls = await db.LootboxEnchantRolls.Include(r => r.EnchantmentDefinition).Include(r => r.LootboxType).ThenInclude(t => t.Category)
            .OrderBy(r => r.SortOrder).ToListAsync();

        rolls.Where(r => r.LootboxType.Category.Name == "Weapons")
            .Select(r => (r.EnchantmentDefinition.Key, r.ChancePercent, r.MinLevel, r.MaxLevel, r.MinBoxStars))
            .Should().Equal(
                ("minecraft:sharpness", 100m, 1, 5, 1), ("minecraft:knockback", 66m, 1, 2, 1), ("minecraft:fire_aspect", 51m, 1, 2, 1),
                ("minecraft:unbreaking", 46m, 1, 3, 1), ("poison", 30m, 1, 3, 3), ("blindness", 26m, 1, 3, 3), ("confusion", 31m, 1, 3, 3),
                ("armor_repair", 6m, 1, 1, 4), ("chaos", 6m, 1, 1, 5));
        rolls.Where(r => r.LootboxType.Category.Name == "Armor").Select(r => (r.EnchantmentDefinition.Key, r.MaxLevel))
            .Should().Equal(("minecraft:protection", 4), ("minecraft:unbreaking", 3));
        rolls.Where(r => r.LootboxType.Category.Name == "Tools").Select(r => (r.EnchantmentDefinition.Key, r.MaxLevel))
            .Should().Equal(("minecraft:efficiency", 4), ("minecraft:unbreaking", 3));
        rolls.Select(r => r.LootboxType.Category.Name).Distinct().Should().BeEquivalentTo("Weapons", "Armor", "Tools");
        rolls.Should().OnlyContain(r => r.MaxLevel <= r.EnchantmentDefinition.MaxLevel);
    }

    [Fact]
    public async Task SecondRun_IsANoOp()
    {
        await SeedAllAsync();
        object before;
        await using (var db = NewContext()) before = await SnapshotAsync(db);

        await using (var again = NewContext()) await LootboxSeed.SeedCanonicalAsync(again, enchantmentCatalog: EnchantmentCatalog);

        await using var after = NewContext();
        (await SnapshotAsync(after)).Should().BeEquivalentTo(before);
    }

    [Fact]
    public async Task ExistingRows_AreReused_NotOverwritten()
    {
        await SeedAllAsync();
        await using (var db = NewContext())
        {
            var weapons = await db.LootboxTypes.SingleAsync(t => t.Category.Name == "Weapons");
            weapons.Enabled = true;
            weapons.EnchantRolls.Clear();
            db.LootboxEnchantRolls.RemoveRange(db.LootboxEnchantRolls.Where(r => r.LootboxTypeId == weapons.Id));
            db.LootboxSpecialEntries.RemoveRange(db.LootboxSpecialEntries.Where(s => s.ItemBlueprint.Name == "Skull splitter"));
            (await db.LootboxConfigurations.SingleAsync()).GlobalMaxActive = 2;
            await db.SaveChangesAsync();
        }

        await using (var again = NewContext()) await LootboxSeed.SeedCanonicalAsync(again, enchantmentCatalog: EnchantmentCatalog);

        await using var read = NewContext();
        var type = await read.LootboxTypes.Include(t => t.EnchantRolls).SingleAsync(t => t.Category.Name == "Weapons");
        type.Enabled.Should().BeTrue();
        type.EnchantRolls.Should().BeEmpty("rolls are only seeded under a type this run creates");
        (await read.LootboxConfigurations.SingleAsync()).GlobalMaxActive.Should().Be(2);
        (await read.LootboxSpecialEntries.CountAsync(s => s.ItemBlueprint.Name == "Skull splitter"))
            .Should().Be(1, "a missing special entry is (re)created; the blueprint keeps a single tag");
        (await read.Set<ItemBlueprintTag>().CountAsync(t => t.ItemBlueprint.Name == "Skull splitter" && t.Tag.Name == LootboxSeed.SpecialTag))
            .Should().Be(1);
    }

    [Fact]
    public async Task MissingBlueprintsAndCustomDefinitions_AreSkipped_NotInvented()
    {
        // No AbilityDefinition seed: the custom enchantments (strength, poison, ...) don't exist.
        await SeedAllAsync(abilities: false);

        await using var db = NewContext();
        (await db.EnchantmentDefinitions.AnyAsync(d => d.IsCustom)).Should().BeFalse();
        var samurai = await db.ItemBlueprints.Include(b => b.DefaultEnchantments).SingleAsync(b => b.Name == LootboxSeed.FlamingSamuraiName);
        samurai.DefaultEnchantments.Should().HaveCount(4, "strength is skipped");
        (await db.LootboxEnchantRolls.CountAsync(r => r.LootboxType.Category.Name == "Weapons")).Should().Be(4, "only the vanilla weapon rolls");
    }

    [Fact]
    public async Task EmptyDatabase_CreatesOnlyTheTagGradesAndSettings()
    {
        await using (var db = NewContext()) await LootboxSeed.SeedCanonicalAsync(db);

        await using var read = NewContext();
        (await read.LootboxTypes.CountAsync()).Should().Be(0);
        (await read.LootboxSpecialEntries.CountAsync()).Should().Be(0);
        (await read.ItemBlueprints.CountAsync()).Should().Be(0, "no Weapons category, so no Flaming Samurai");
        (await read.Tags.Select(t => t.Name).ToListAsync()).Should().Equal(LootboxSeed.SpecialTag);
        (await read.Grades.CountAsync()).Should().Be(10);
        (await read.LootboxConfigurations.CountAsync()).Should().Be(1);
    }

    [Fact]
    public void Model_NoCascadeIntoTheCatalog()
    {
        using var db = NewContext();
        var catalog = new[] { typeof(ItemBlueprint), typeof(Category), typeof(Grade), typeof(EnchantmentDefinition), typeof(ItemInstance) };
        var lootboxEntities = db.Model.GetEntityTypes().Where(e => e.ClrType.Name.StartsWith("Lootbox"));

        var intoCatalog = lootboxEntities.SelectMany(e => e.GetForeignKeys()).Where(fk => catalog.Contains(fk.PrincipalEntityType.ClrType)).ToList();

        intoCatalog.Should().NotBeEmpty();
        intoCatalog.Should().OnlyContain(fk => fk.DeleteBehavior == DeleteBehavior.Restrict,
            "vision §9.2: deleting a lootbox row never touches the catalog, and the catalog can't vanish under it");
    }
}
