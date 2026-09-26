using Microsoft.EntityFrameworkCore;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using Xunit;

namespace knkwebapi_v2.Tests.Repositories;

/// <summary>Menu follow-up 2026-09-26: the item catalogue filters blueprints by category (incl. sub-categories).</summary>
public class ItemBlueprintCategoryFilterTests
{
    private static async Task<ItemBlueprintRepository> SeedAsync()
    {
        var context = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase($"CategoryFilter_{Guid.NewGuid()}").Options);
        context.Categories.AddRange(
            new Category { Id = 1, Name = "Weapons" },
            new Category { Id = 2, Name = "Swords", ParentCategoryId = 1 },
            new Category { Id = 3, Name = "Food" });
        context.ItemBlueprints.AddRange(
            new ItemBlueprint { Id = 10, Name = "Bow", DefaultDisplayName = "Bow", CategoryId = 1 },
            new ItemBlueprint { Id = 11, Name = "Iron Sword", DefaultDisplayName = "Iron Sword", CategoryId = 2 },
            new ItemBlueprint { Id = 12, Name = "Bread", DefaultDisplayName = "Bread", CategoryId = 3 },
            new ItemBlueprint { Id = 13, Name = "Rock", DefaultDisplayName = "Rock" });
        await context.SaveChangesAsync();
        return new ItemBlueprintRepository(context);
    }

    private static PagedQuery Query(Dictionary<string, string>? filters) =>
        new() { PageNumber = 1, PageSize = 50, Filters = filters };

    [Fact]
    public async Task CategoryNameIncludesSubCategories_CaseInsensitive()
    {
        var repo = await SeedAsync();

        var result = await repo.SearchAsync(Query(new() { ["Category"] = "weapons" }));

        Assert.Equal(new[] { 10, 11 }, result.Items.Select(i => i.Id).OrderBy(i => i));
        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task CategoryIdLeafAndUnknownName()
    {
        var repo = await SeedAsync();

        Assert.Equal(new[] { 11 }, (await repo.SearchAsync(Query(new() { ["CategoryId"] = "2" }))).Items.Select(i => i.Id));
        Assert.Empty((await repo.SearchAsync(Query(new() { ["Category"] = "Nope" }))).Items);
    }

    [Fact]
    public async Task NoOrBlankFilterReturnsEverything()
    {
        var repo = await SeedAsync();

        Assert.Equal(4, (await repo.SearchAsync(Query(null))).TotalCount);
        Assert.Equal(4, (await repo.SearchAsync(Query(new() { ["Category"] = " " }))).TotalCount);
    }
}
