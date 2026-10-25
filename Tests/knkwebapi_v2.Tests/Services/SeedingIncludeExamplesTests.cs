using knkwebapi_v2.Configuration;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace knkwebapi_v2.Tests.Services;

/// <summary>
/// Seeding:IncludeExamples (<see cref="SeedingOptions"/>): the startup seeders in Program.cs order, with and without
/// their example rows (the <c>example.*</c> menus, the example siege lobby, the 18 trade goods, the <c>Test</c> tag).
/// The real content is the same either way. Program.cs runs the Minecraft catalogue backfill between the menu and kit
/// seeds; it needs the full service and changes no rows these tests look at, so it is left out.
/// </summary>
public class SeedingIncludeExamplesTests
{
    private static readonly string[] ExampleMenuKeys =
    {
        "example.placeholder", "example.pagination", "example.permissions", "example.search", "example.conditions",
        "example.presets", "example.catalog", "example.domain", "example.domain.detail",
    };

    private static KnKDbContext NewContext(string name) =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(name).Options);

    private static async Task SeedAllAsync(string dbName, bool includeExamples)
    {
        await using var context = NewContext(dbName);
        await AbilityDefinition.SeedCanonicalAsync(context);
        await ItemBlueprintExampleCatalogSeed.SeedCanonicalAsync(context, includeExamples: includeExamples);
        await MenuTemplateSeed.SeedCanonicalAsync(context, includeExamples: includeExamples);
        await KitSeed.SeedCanonicalAsync(context, includeExamples: includeExamples);
        await ItemBlueprintV1Seed.SeedCanonicalAsync(context);
        await EnchantBookSeed.SeedCanonicalAsync(context);
        await LootboxSeed.SeedCanonicalAsync(context);
        await SiegeLobbySeed.SeedCanonicalAsync(context, includeExamples: includeExamples);
    }

    private sealed record Seeded(
        List<string> MenuKeys, List<string> Blueprints, List<string> Tags, List<string> Kits,
        List<string> Categories, List<string> LootboxTypes, int LootboxSpecials, List<string> Lobbies);

    private static async Task<Seeded> ReadAsync(string dbName)
    {
        await using var db = NewContext(dbName);
        return new Seeded(
            await db.MenuTemplates.Select(m => m.Key).OrderBy(k => k).ToListAsync(),
            await db.ItemBlueprints.Select(b => b.Name).OrderBy(n => n).ToListAsync(),
            await db.Tags.Select(t => t.Name).OrderBy(n => n).ToListAsync(),
            await db.Kits.Select(k => k.Name).OrderBy(n => n).ToListAsync(),
            await db.Categories.Select(c => c.Name).OrderBy(n => n).ToListAsync(),
            await db.LootboxTypes.Select(t => t.Name).OrderBy(n => n).ToListAsync(),
            await db.LootboxSpecialEntries.CountAsync(),
            await db.SiegeLobbies.Select(l => l.Key).ToListAsync());
    }

    [Fact]
    public async Task ExamplesOn_SeedsTheExampleRows()
    {
        var dbName = $"SeedingExamplesOn_{Guid.NewGuid()}";
        await SeedAllAsync(dbName, includeExamples: true);

        var seeded = await ReadAsync(dbName);
        Assert.Equal(ExampleMenuKeys.OrderBy(k => k), seeded.MenuKeys.Where(MenuTemplateSeed.IsExampleKey));
        Assert.Equal(new[] { SiegeLobbySeed.ExampleKey }, seeded.Lobbies);
        Assert.Equal(18, ItemBlueprintExampleCatalogSeed.Names.Count);
        Assert.All(ItemBlueprintExampleCatalogSeed.Names, name => Assert.Contains(name, seeded.Blueprints));
        Assert.Contains(KitSeed.ExampleTag, seeded.Tags);
    }

    [Fact]
    public async Task ExamplesOff_SkipsTheExampleRows_OnEveryStart()
    {
        var dbName = $"SeedingExamplesOff_{Guid.NewGuid()}";
        await SeedAllAsync(dbName, includeExamples: false);
        await SeedAllAsync(dbName, includeExamples: false);

        var seeded = await ReadAsync(dbName);
        Assert.DoesNotContain(seeded.MenuKeys, MenuTemplateSeed.IsExampleKey);
        Assert.Empty(seeded.Lobbies);
        Assert.Empty(seeded.Blueprints.Intersect(ItemBlueprintExampleCatalogSeed.Names));
        Assert.DoesNotContain(KitSeed.ExampleTag, seeded.Tags);
    }

    [Fact]
    public async Task ExamplesOff_SeedsTheSameRealContent()
    {
        var withName = $"SeedingExamplesWith_{Guid.NewGuid()}";
        var withoutName = $"SeedingExamplesWithout_{Guid.NewGuid()}";
        await SeedAllAsync(withName, includeExamples: true);
        await SeedAllAsync(withoutName, includeExamples: false);
        var with = await ReadAsync(withName);
        var without = await ReadAsync(withoutName);

        Assert.Equal(with.MenuKeys.Where(k => !MenuTemplateSeed.IsExampleKey(k)), without.MenuKeys);
        Assert.Equal(with.Blueprints.Where(n => !ItemBlueprintExampleCatalogSeed.Names.Contains(n)), without.Blueprints);
        Assert.Equal(with.Tags.Where(t => t != KitSeed.ExampleTag), without.Tags);
        Assert.Equal(with.Kits, without.Kits);
        Assert.Equal(with.Categories, without.Categories);
        Assert.Equal(with.LootboxTypes, without.LootboxTypes);
        Assert.Equal(with.LootboxSpecials, without.LootboxSpecials);

        // The content the alpha needs, named.
        Assert.Contains(MenuTemplateSeed.HubMenuKey, without.MenuKeys);
        Assert.Contains(MenuTemplateSeed.KitsOverviewMenuKey, without.MenuKeys);
        Assert.Contains(MenuTemplateSeed.SiegeOverviewMenuKey, without.MenuKeys);
        Assert.Equal(new[] { "Archer", "Default" }, without.Kits);
        Assert.Contains(EnchantBookSeed.BookName("Cleaving", 4), without.Blueprints);
        Assert.Contains(LootboxSeed.FlamingSamuraiName, without.Blueprints);
        Assert.NotEmpty(without.LootboxTypes);
        Assert.Contains("Open Beta", without.Tags);
    }

    [Fact]
    public async Task ExamplesOff_LeavesExampleRowsAnEarlierStartCreated()
    {
        // Create-only: turning the setting off stops new example rows; removing old ones is the maintenance script's job.
        var dbName = $"SeedingExamplesLater_{Guid.NewGuid()}";
        await SeedAllAsync(dbName, includeExamples: true);
        var before = await ReadAsync(dbName);

        await SeedAllAsync(dbName, includeExamples: false);

        Assert.Equivalent(before, await ReadAsync(dbName), strict: true);
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("False", false)]
    public void Options_DefaultToIncludingExamples(string? value, bool expected)
    {
        var settings = new Dictionary<string, string?>();
        if (value != null) settings["Seeding:IncludeExamples"] = value;
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        Assert.Equal(expected, SeedingOptions.From(configuration).IncludeExamples);
    }
}
