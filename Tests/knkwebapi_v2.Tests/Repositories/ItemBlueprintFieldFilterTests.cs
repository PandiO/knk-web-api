using Microsoft.EntityFrameworkCore;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using Xunit;

namespace knkwebapi_v2.Tests.Repositories;

/// <summary>KNG-10: /knk itemblueprints (ib) search filters by Id, Name or DefaultDisplayName.</summary>
public class ItemBlueprintFieldFilterTests
{
    private static async Task<ItemBlueprintRepository> SeedAsync()
    {
        var context = new KnKDbContext(new DbContextOptionsBuilder<KnKDbContext>()
            .UseInMemoryDatabase($"FieldFilter_{Guid.NewGuid()}").Options);
        context.Categories.Add(new Category { Id = 1, Name = "Weapons" });
        context.ItemBlueprints.AddRange(
            new ItemBlueprint { Id = 10, Name = "Iron Sword", DefaultDisplayName = "&7Iron Sword", CategoryId = 1 },
            new ItemBlueprint { Id = 11, Name = "Excalibur", DefaultDisplayName = "&6Excalibur", CategoryId = 1 },
            new ItemBlueprint { Id = 12, Name = "Bread", DefaultDisplayName = "Fresh Bread" },
            new ItemBlueprint { Id = 13, Name = "Wooden Sword", DefaultDisplayName = "Training Blade" });
        await context.SaveChangesAsync();
        return new ItemBlueprintRepository(context);
    }

    private static async Task<int[]> Ids(ItemBlueprintRepository repo, Dictionary<string, string> filters)
    {
        var result = await repo.SearchAsync(new PagedQuery { PageNumber = 1, PageSize = 50, Filters = filters });
        Assert.Equal(result.Items.Count(), result.TotalCount);
        return result.Items.Select(i => i.Id).OrderBy(i => i).ToArray();
    }

    [Fact]
    public async Task NameIsCaseInsensitiveContains()
    {
        var repo = await SeedAsync();

        Assert.Equal(new[] { 10, 13 }, await Ids(repo, new() { ["Name"] = "sword" }));
        Assert.Equal(new[] { 11 }, await Ids(repo, new() { ["Name"] = "EXCAL" }));
        Assert.Empty(await Ids(repo, new() { ["Name"] = "Nope" }));
    }

    [Fact]
    public async Task DefaultDisplayNameMatchesTheDisplayNameNotTheName()
    {
        var repo = await SeedAsync();

        Assert.Equal(new[] { 13 }, await Ids(repo, new() { ["DefaultDisplayName"] = "blade" }));
        Assert.Equal(new[] { 11 }, await Ids(repo, new() { ["DefaultDisplayName"] = "Excalibur" }));
        Assert.Empty(await Ids(repo, new() { ["DefaultDisplayName"] = "Wooden" }));
    }

    [Fact]
    public async Task IdIsExact()
    {
        var repo = await SeedAsync();

        Assert.Equal(new[] { 12 }, await Ids(repo, new() { ["Id"] = "12" }));
        Assert.Empty(await Ids(repo, new() { ["Id"] = "99" }));
    }

    [Fact]
    public async Task KeysAreCaseInsensitiveAndCombineWithCategory()
    {
        var repo = await SeedAsync();

        Assert.Equal(new[] { 10, 13 }, await Ids(repo, new() { ["name"] = "sword" }));
        Assert.Equal(new[] { 10 }, await Ids(repo, new() { ["Name"] = "sword", ["Category"] = "Weapons" }));
    }

    [Fact]
    public async Task BlankValuesAreIgnored()
    {
        var repo = await SeedAsync();

        Assert.Equal(new[] { 10, 11, 12, 13 }, await Ids(repo, new() { ["Name"] = " ", ["Id"] = "abc" }));
    }
}
