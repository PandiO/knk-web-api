using FluentAssertions;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Kits IMPLEMENTATION_PLAN.md Phase 8: KitSeed creates exactly SEED_DATA.md's rows with the right
/// links, is idempotent, and reuses (never overwrites) pre-existing rows with the same natural key.
/// </summary>
public class KitSeedTests
{
    private readonly string _dbName = $"KitSeedTestDb_{Guid.NewGuid()}";

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    private static IMinecraftMaterialCatalogService Catalog()
    {
        var catalog = new Mock<IMinecraftMaterialCatalogService>();
        catalog.Setup(c => c.GetAll()).Returns(new[]
        {
            new MinecraftMaterialCatalogEntry
            {
                NamespaceKey = "minecraft:iron_sword", DisplayName = "Iron Sword", Category = "ITEM",
                TexturePath = "items/iron_sword", IconUrl = "https://example.test/iron_sword.png",
            },
        });
        return catalog.Object;
    }

    private async Task SeedAsync()
    {
        await using var context = NewContext();
        await KitSeed.SeedCanonicalAsync(context, Catalog());
    }

    [Fact]
    public async Task FirstRun_CreatesExactlyTheSeedRows()
    {
        await SeedAsync();

        await using var db = NewContext();
        (await db.Categories.CountAsync()).Should().Be(3);
        (await db.Tags.CountAsync()).Should().Be(4);
        (await db.Set<CategoryTag>().CountAsync()).Should().Be(5);
        (await db.Grades.CountAsync()).Should().Be(10); // all of GradeDefaults (KNG-6)
        (await db.ItemBlueprints.CountAsync()).Should().Be(9);
        (await db.Kits.CountAsync()).Should().Be(2);
        (await db.Set<KitContent>().CountAsync()).Should().Be(6);
        (await db.MinecraftMaterialRefs.CountAsync()).Should().Be(9);
    }

    [Fact]
    public async Task FirstRun_LinksEverythingCorrectly()
    {
        await SeedAsync();

        await using var db = NewContext();
        var blueprints = await db.ItemBlueprints
            .Include(b => b.IconMaterial).Include(b => b.Category).Include(b => b.Grade)
            .ToDictionaryAsync(b => b.Name!);

        var sword = blueprints["Iron Sword"];
        sword.DefaultDisplayName.Should().Be("§7Iron Sword");
        sword.IconMaterial!.NamespaceKey.Should().Be("minecraft:iron_sword");
        sword.IconMaterial.IconUrl.Should().Be("https://example.test/iron_sword.png");
        sword.Category!.Name.Should().Be("Weapons");
        sword.Grade!.Name.Should().Be("Common");
        blueprints["Iron Axe"].Grade!.Name.Should().Be("Uncommon");
        blueprints["Iron Chestplate"].Category!.Name.Should().Be("Armor");
        blueprints["Maggoty Bread"].Category!.Name.Should().Be("Food");
        blueprints["Wooden Bow"].IconMaterial!.NamespaceKey.Should().Be("minecraft:bow");
        blueprints.Values.Should().OnlyContain(b => b.DefaultDisplayName.StartsWith("§7") && !b.DefaultDisplayName.Contains('Â'));
        blueprints.Values.Should().OnlyContain(b => b.DefaultQuantity == (b.Name == "Arrow" ? 64 : 1));
        blueprints.Values.Should().OnlyContain(b => b.BasePriceMin == 0 && b.BasePriceMax == 0);
        (await db.Set<ItemBlueprintTag>().CountAsync()).Should().Be(0);
        (await db.Set<ItemBlueprintOrigin>().CountAsync()).Should().Be(0);

        // Material not in the catalog falls back to the ITEM category.
        (await db.MinecraftMaterialRefs.SingleAsync(m => m.NamespaceKey == "minecraft:arrow")).Category.Should().Be("ITEM");

        var weapons = await db.Categories.Include(c => c.IconMaterialRef).Include(c => c.Tags).ThenInclude(t => t.Tag)
            .SingleAsync(c => c.Name == "Weapons");
        weapons.IconMaterialRef!.NamespaceKey.Should().Be("minecraft:iron_sword");
        weapons.Tags.Select(t => t.Tag.Name).Should().BeEquivalentTo("Open Beta", "Melee");
        var food = await db.Categories.Include(c => c.Tags).ThenInclude(t => t.Tag).SingleAsync(c => c.Name == "Food");
        food.Tags.Select(t => t.Tag.Name).Should().BeEquivalentTo("Open Beta");

        var kits = await db.Kits.Include(k => k.Contents).ToDictionaryAsync(k => k.Name);
        var def = kits["Default"];
        def.GrantOnFirstJoin.Should().BeTrue();
        def.HelmetId.Should().Be(blueprints["Iron Helmet"].Id);
        def.ChestplateId.Should().Be(blueprints["Iron Chestplate"].Id);
        def.LeggingsId.Should().Be(blueprints["Iron Leggings"].Id);
        def.BootsId.Should().Be(blueprints["Iron Boots"].Id);
        def.ShieldId.Should().BeNull();
        def.HandId.Should().Be(blueprints["Iron Sword"].Id);
        def.CooldownSeconds.Should().Be(0);
        def.CostAmount.Should().BeNull();
        def.MinTitleBracketId.Should().BeNull();
        def.RequiredPermissionGroupId.Should().BeNull();
        def.RequiredPermissionNode.Should().BeNull();
        def.Contents.OrderBy(c => c.SlotIndex).Select(c => (c.SlotIndex, c.ItemBlueprintId, c.Quantity)).Should().Equal(
            (9, blueprints["Maggoty Bread"].Id, 1),
            (10, blueprints["Arrow"].Id, 64),
            (11, blueprints["Wooden Bow"].Id, 1),
            (12, blueprints["Iron Axe"].Id, 1));

        var archer = kits["Archer"];
        archer.GrantOnFirstJoin.Should().BeFalse();
        archer.HandId.Should().Be(blueprints["Maggoty Bread"].Id); // verbatim from SEED_DATA.md
        archer.HelmetId.Should().Be(blueprints["Iron Helmet"].Id);
        archer.ShieldId.Should().BeNull();
        archer.Contents.OrderBy(c => c.SlotIndex).Select(c => (c.SlotIndex, c.ItemBlueprintId, c.Quantity)).Should().Equal(
            (9, blueprints["Wooden Bow"].Id, 1),
            (10, blueprints["Arrow"].Id, 64));
    }

    [Fact]
    public async Task SecondRun_CreatesNothing()
    {
        await SeedAsync();
        await SeedAsync();

        await using var db = NewContext();
        (await db.Categories.CountAsync()).Should().Be(3);
        (await db.Tags.CountAsync()).Should().Be(4);
        (await db.Set<CategoryTag>().CountAsync()).Should().Be(5);
        (await db.Grades.CountAsync()).Should().Be(10); // all of GradeDefaults (KNG-6)
        (await db.ItemBlueprints.CountAsync()).Should().Be(9);
        (await db.Kits.CountAsync()).Should().Be(2);
        (await db.Set<KitContent>().CountAsync()).Should().Be(6);
        (await db.MinecraftMaterialRefs.CountAsync()).Should().Be(9);
    }

    [Fact]
    public async Task PreExistingRows_AreReusedAndLeftUnchanged()
    {
        int swordId, defaultKitId, weaponsId, commonId, openBetaId, swordIconId;
        await using (var db = NewContext())
        {
            var authoredSwordIcon = new MinecraftMaterialRef { NamespaceKey = "minecraft:iron_sword", Category = "CUSTOM", IconUrl = "hand-picked" };
            var authoredWeapons = new Category { Name = "Weapons" }; // no icon, no tags - hand-authored
            var authoredCommon = new Grade { Name = "Common", Stars = 5 };
            var authoredOpenBeta = new Tag { Name = "Open Beta" };
            var authoredSword = new ItemBlueprint { Name = "Iron Sword", DefaultDisplayName = "My Sword", DefaultQuantity = 3, MaxStackSize = 16 };
            var authoredCustom = new ItemBlueprint { Name = "Custom Thing", DefaultDisplayName = "Custom Thing" };
            var authoredDefaultKit = new Kit { Name = "Default", GrantOnFirstJoin = false, CooldownSeconds = 60 };
            authoredDefaultKit.Contents.Add(new KitContent { Kit = authoredDefaultKit, SlotIndex = 0, ItemBlueprint = authoredCustom, Quantity = 2 });
            db.AddRange(authoredSwordIcon, authoredWeapons, authoredCommon, authoredOpenBeta, authoredSword, authoredCustom, authoredDefaultKit);
            await db.SaveChangesAsync();
            (swordId, defaultKitId, weaponsId, commonId, openBetaId, swordIconId) =
                (authoredSword.Id, authoredDefaultKit.Id, authoredWeapons.Id, authoredCommon.Id, authoredOpenBeta.Id, authoredSwordIcon.Id);
        }

        await SeedAsync();
        await SeedAsync();

        await using var check = NewContext();
        (await check.Categories.CountAsync()).Should().Be(3);
        (await check.Tags.CountAsync()).Should().Be(4);
        (await check.Grades.CountAsync()).Should().Be(9); // authored Common holds 5 stars, so Legendary is skipped
        (await check.ItemBlueprints.CountAsync()).Should().Be(10); // 8 seeded + reused sword + Custom Thing
        (await check.Kits.CountAsync()).Should().Be(2);
        (await check.MinecraftMaterialRefs.CountAsync()).Should().Be(9);

        var sword = await check.ItemBlueprints.SingleAsync(b => b.Name == "Iron Sword");
        sword.Id.Should().Be(swordId);
        sword.DefaultDisplayName.Should().Be("My Sword");
        sword.DefaultQuantity.Should().Be(3);
        sword.MaxStackSize.Should().Be(16);
        sword.IconMaterialRefId.Should().BeNull();
        sword.CategoryId.Should().BeNull();
        sword.GradeId.Should().BeNull();

        var swordIcon = await check.MinecraftMaterialRefs.SingleAsync(m => m.NamespaceKey == "minecraft:iron_sword");
        swordIcon.Id.Should().Be(swordIconId);
        swordIcon.Category.Should().Be("CUSTOM");
        swordIcon.IconUrl.Should().Be("hand-picked");

        var weapons = await check.Categories.Include(c => c.Tags).SingleAsync(c => c.Name == "Weapons");
        weapons.Id.Should().Be(weaponsId);
        weapons.IconMaterialRefId.Should().BeNull();
        weapons.Tags.Should().BeEmpty(); // reused category: no seed tags added to it
        (await check.Set<CategoryTag>().CountAsync()).Should().Be(3); // Armor ×2 + Food ×1

        var common = await check.Grades.SingleAsync(g => g.Name == "Common");
        common.Id.Should().Be(commonId);
        common.Stars.Should().Be(5);
        common.DropChance.Should().BeNull(); // reused as authored, not backfilled by the seed
        (await check.Grades.CountAsync(g => g.Stars == 5)).Should().Be(1);
        (await check.Tags.SingleAsync(t => t.Name == "Open Beta")).Id.Should().Be(openBetaId);

        // Seeded rows point at the reused rows.
        var bow = await check.ItemBlueprints.SingleAsync(b => b.Name == "Wooden Bow");
        bow.CategoryId.Should().Be(weaponsId);
        bow.GradeId.Should().Be(commonId);

        var defaultKit = await check.Kits.Include(k => k.Contents).SingleAsync(k => k.Name == "Default");
        defaultKit.Id.Should().Be(defaultKitId);
        defaultKit.GrantOnFirstJoin.Should().BeFalse();
        defaultKit.CooldownSeconds.Should().Be(60);
        defaultKit.HandId.Should().BeNull();
        defaultKit.Contents.Should().ContainSingle().Which.SlotIndex.Should().Be(0); // untouched

        var archer = await check.Kits.Include(k => k.Contents).SingleAsync(k => k.Name == "Archer");
        archer.HandId.Should().NotBeNull();
        archer.HelmetId.Should().NotBeNull();
        archer.Contents.Should().HaveCount(2);
    }

    [Fact]
    public async Task WithoutCatalog_StillResolvesIcons()
    {
        await using (var context = NewContext())
        {
            await KitSeed.SeedCanonicalAsync(context);
        }

        await using var db = NewContext();
        (await db.ItemBlueprints.CountAsync(b => b.IconMaterialRefId == null)).Should().Be(0);
        (await db.Categories.CountAsync(c => c.IconMaterialRefId == null)).Should().Be(0);
        (await db.MinecraftMaterialRefs.Select(m => m.Category).Distinct().ToListAsync()).Should().Equal("ITEM");
    }

    [Fact]
    public async Task FirstRun_SeedsAllTenGradesWithDropChanceAndCapDivisor()
    {
        await SeedAsync();

        await using var db = NewContext();
        var grades = await db.Grades.OrderBy(g => g.Stars).ToListAsync();
        grades.Select(g => (g.Name, g.Stars, g.DropChance, g.EnchantLevelCapDivisor)).Should().Equal(
            ("Common", 1, 70m, 5),
            ("Uncommon", 2, 60m, 4),
            ("Rare", 3, 40m, 3),
            ("Epic", 4, 25m, 2),
            ("Legendary", 5, 15m, 1),
            ("Mythic", 6, 8m, (int?)null),
            ("Ascended", 7, 5m, null),
            ("Relic", 8, 1m, null),
            ("Exalted", 9, 0.5m, null),
            ("Divine", 10, 0.05m, null));
    }

    [Fact]
    public async Task ExistingGradeWithTheSameStars_IsNotDuplicated()
    {
        await using (var db = NewContext())
        {
            db.Grades.Add(new Grade { Name = "Starter", Stars = 1 });
            await db.SaveChangesAsync();
        }

        await SeedAsync();

        await using var check = NewContext();
        (await check.Grades.CountAsync(g => g.Stars == 1)).Should().Be(1);
        (await check.Grades.AnyAsync(g => g.Name == "Common")).Should().BeFalse();
        // The kit items that wanted "Common" are left ungraded rather than failing the seed.
        (await check.ItemBlueprints.SingleAsync(b => b.Name == "Iron Sword")).GradeId.Should().BeNull();
        (await check.ItemBlueprints.Include(b => b.Grade).SingleAsync(b => b.Name == "Iron Axe")).Grade!.Name.Should().Be("Uncommon");
    }
}
