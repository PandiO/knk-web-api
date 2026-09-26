using FluentAssertions;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// KNG-5 (docs/specs/enchantment-books/ENCHANTMENT_BOOK_APPLICATION.md §3.3): EnchantBookSeed creates one
/// enchanted_book blueprint per (enchantment, level) with exactly that one default enchantment, reuses the
/// ability/V1 seeds' definitions, is idempotent, and skips (never invents) missing definitions.
/// </summary>
public class EnchantBookSeedTests
{
    // 12 custom (24 books) + 5 vanilla (22 books).
    private const int ExpectedBooks = 46;

    private readonly string _dbName = $"EnchantBookSeedTestDb_{Guid.NewGuid()}";

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    private async Task SeedAllAsync()
    {
        await using var context = NewContext();
        await AbilityDefinition.SeedCanonicalAsync(context);
        await ItemBlueprintV1Seed.SeedCanonicalAsync(context);
        await EnchantBookSeed.SeedCanonicalAsync(context);
    }

    private static IQueryable<ItemBlueprint> Books(KnKDbContext db) =>
        db.ItemBlueprints
            .Include(b => b.IconMaterial).Include(b => b.Category)
            .Include(b => b.DefaultEnchantments).ThenInclude(e => e.EnchantmentDefinition)
            .Where(b => b.Category != null && b.Category.Name == EnchantBookSeed.CategoryName);

    [Fact]
    public async Task FirstRun_CreatesOneBookPerEnchantmentLevel()
    {
        await SeedAllAsync();

        await using var db = NewContext();
        var books = await Books(db).ToListAsync();
        EnchantBookSeed.BookCount.Should().Be(ExpectedBooks);
        books.Should().HaveCount(ExpectedBooks);
        books.Should().OnlyContain(b => b.IconMaterial!.NamespaceKey == EnchantBookSeed.BookMaterialKey);
        books.Should().OnlyContain(b => b.DefaultEnchantments.Count == 1 && b.MaxStackSize == 1 && b.Grade == null);
        (await db.Categories.CountAsync(c => c.Name == EnchantBookSeed.CategoryName)).Should().Be(1);
        (await db.MinecraftMaterialRefs.CountAsync(m => m.NamespaceKey == EnchantBookSeed.BookMaterialKey)).Should().Be(1);

        var poison2 = books.Single(b => b.Name == "Enchanted Book (Poison II)").DefaultEnchantments.Single();
        (poison2.EnchantmentDefinition.Key, poison2.EnchantmentDefinition.IsCustom, poison2.Level).Should().Be(("poison", true, 2));

        var sharpness5 = books.Single(b => b.Name == "Enchanted Book (Sharpness V)");
        sharpness5.DefaultDisplayName.Should().Be("&dEnchanted Book (Sharpness V)");
        var sharpness = sharpness5.DefaultEnchantments.Single();
        (sharpness.EnchantmentDefinition.Key, sharpness.EnchantmentDefinition.IsCustom, sharpness.Level).Should().Be(("minecraft:sharpness", false, 5));

        books.Select(b => b.Name).Should().Contain("Enchanted Book (Flash Chaos I)").And.NotContain("Enchanted Book (Flash Chaos II)");
    }

    [Fact]
    public async Task FirstRun_ReusesExistingDefinitions()
    {
        await using (var context = NewContext())
        {
            await AbilityDefinition.SeedCanonicalAsync(context);
            await ItemBlueprintV1Seed.SeedCanonicalAsync(context);
        }
        int definitionsBefore;
        await using (var db = NewContext()) definitionsBefore = await db.EnchantmentDefinitions.CountAsync();

        await using (var context = NewContext()) await EnchantBookSeed.SeedCanonicalAsync(context);

        await using var after = NewContext();
        (await after.EnchantmentDefinitions.CountAsync()).Should().Be(definitionsBefore);
    }

    [Fact]
    public async Task SecondRun_CreatesNothing()
    {
        await SeedAllAsync();
        int blueprints, joins;
        await using (var db = NewContext())
        {
            blueprints = await db.ItemBlueprints.CountAsync();
            joins = await db.Set<ItemBlueprintDefaultEnchantment>().CountAsync();
        }

        await using (var context = NewContext()) await EnchantBookSeed.SeedCanonicalAsync(context);

        await using var again = NewContext();
        (await again.ItemBlueprints.CountAsync()).Should().Be(blueprints);
        (await again.Set<ItemBlueprintDefaultEnchantment>().CountAsync()).Should().Be(joins);
        (await again.Categories.CountAsync(c => c.Name == EnchantBookSeed.CategoryName)).Should().Be(1);
        (await again.MinecraftMaterialRefs.CountAsync(m => m.NamespaceKey == EnchantBookSeed.BookMaterialKey)).Should().Be(1);
    }

    [Fact]
    public async Task MissingDefinitions_AreSkippedNotInvented()
    {
        // Only the custom definitions exist: the vanilla books are skipped.
        await using (var context = NewContext())
        {
            await AbilityDefinition.SeedCanonicalAsync(context);
            await EnchantBookSeed.SeedCanonicalAsync(context);
        }

        await using var db = NewContext();
        (await Books(db).CountAsync()).Should().Be(24);
        (await db.EnchantmentDefinitions.CountAsync(e => !e.IsCustom)).Should().Be(0);
    }

    [Fact]
    public async Task ExistingBook_IsLeftAsIs()
    {
        await using (var context = NewContext())
        {
            await AbilityDefinition.SeedCanonicalAsync(context);
            context.ItemBlueprints.Add(new ItemBlueprint { Name = "Enchanted Book (Poison I)", DefaultDisplayName = "Admin's own" });
            await context.SaveChangesAsync();
            await EnchantBookSeed.SeedCanonicalAsync(context);
        }

        await using var db = NewContext();
        var own = await db.ItemBlueprints.Include(b => b.DefaultEnchantments).SingleAsync(b => b.Name == "Enchanted Book (Poison I)");
        own.DefaultDisplayName.Should().Be("Admin's own");
        own.DefaultEnchantments.Should().BeEmpty();
        (await Books(db).CountAsync()).Should().Be(23);
    }
}
