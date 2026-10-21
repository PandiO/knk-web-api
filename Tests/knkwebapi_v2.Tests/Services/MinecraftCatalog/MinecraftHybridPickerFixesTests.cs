using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.MinecraftCatalog;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services.MinecraftCatalog;

/// <summary>
/// KNG-200 phase 1 (plan §1.1, §10): the hybrid picker before the first scan - saved rows match the
/// catalogue by normalised key, saved-only rows obey search and category, unsaved rows have no id,
/// sorting and page size are honoured, two states of one block don't break the block search, and a
/// key or state can't be created twice.
/// </summary>
public class MinecraftHybridPickerFixesTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly IMapper _mapper = new MapperConfiguration(cfg =>
    {
        cfg.AddProfile<PagedQueryMappingProfile>();
        cfg.AddProfile<MinecraftMaterialRefMappingProfile>();
        cfg.AddProfile<MinecraftBlockRefMappingProfile>();
    }).CreateMapper();

    private static readonly MinecraftMaterialCatalogEntry[] Entries =
    {
        new() { NamespaceKey = "minecraft:stone", DisplayName = "Stone", Category = "ITEM", IconUrl = "https://static/stone.png" },
        new() { NamespaceKey = "minecraft:oak_stairs", DisplayName = "Oak Stairs", Category = "BLOCK" },
        new() { NamespaceKey = "minecraft:apple", DisplayName = "Apple", Category = "ITEM" },
    };

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    private static IMinecraftMaterialCatalogService StaticCatalog()
    {
        var catalog = new Mock<IMinecraftMaterialCatalogService>();
        catalog.Setup(c => c.GetAll()).Returns(Entries);
        catalog.Setup(c => c.Search(It.IsAny<string?>(), It.IsAny<string?>()))
            .Returns((string? search, string? category) => Entries
                .Where(e => string.IsNullOrWhiteSpace(category) || string.Equals(e.Category, category, StringComparison.OrdinalIgnoreCase))
                .Where(e => string.IsNullOrWhiteSpace(search)
                    || e.NamespaceKey.Contains(search, StringComparison.OrdinalIgnoreCase)
                    || e.DisplayName.Contains(search, StringComparison.OrdinalIgnoreCase)));
        return catalog.Object;
    }

    private MinecraftCatalogPickerSource HybridSource(KnKDbContext context) =>
        new(context, new MinecraftCatalogOptions { PickerSource = MinecraftPickerSource.Hybrid });

    private MinecraftMaterialRefService Materials(KnKDbContext context) =>
        new(new MinecraftMaterialRefRepository(context), _mapper, StaticCatalog(), HybridSource(context));

    private MinecraftBlockRefService Blocks(KnKDbContext context) =>
        new(new MinecraftBlockRefRepository(context), _mapper, StaticCatalog(), HybridSource(context), new MinecraftMaterialRefRepository(context));

    private async Task SeedAsync(params object[] rows)
    {
        await using var context = NewContext();
        context.AddRange(rows);
        await context.SaveChangesAsync();
    }

    private static PagedQueryDto Query(string? search = null, string? category = null, string? sortBy = null, bool descending = false, int pageSize = 10)
    {
        var filters = new Dictionary<string, string> { ["SearchHybrid"] = "true" };
        if (category != null) filters["category"] = category;
        return new PagedQueryDto { PageNumber = 1, PageSize = pageSize, SearchTerm = search, SortBy = sortBy, SortDescending = descending, Filters = filters };
    }

    [Fact]
    public async Task Materials_SavedRowInAnotherNotation_IsOneRow_AndUnsavedRowsHaveNoId()
    {
        await SeedAsync(new MinecraftMaterialRef { Id = 7, NamespaceKey = "STONE", Category = "ITEM" });

        await using var context = NewContext();
        var result = await Materials(context).SearchHybridAsync(Query());

        Assert.Equal(3, result.TotalCount);
        var stone = Assert.Single(result.Items, r => r.Id == 7);
        Assert.True(stone.IsPersisted);
        Assert.Equal("Stone", stone.DisplayName);
        Assert.All(result.Items.Where(r => r.IsPersisted != true), r => Assert.Null(r.Id));
        Assert.Equal(10, result.PageSize);
    }

    [Fact]
    public async Task Materials_SavedRowsTheCatalogueLacks_ObeySearchAndCategory()
    {
        await SeedAsync(
            new MinecraftMaterialRef { Id = 1, NamespaceKey = "knk:ruby", Category = "ITEM", DisplayName = "Ruby" },
            new MinecraftMaterialRef { Id = 2, NamespaceKey = "knk:ruby_block", Category = "BLOCK", DisplayName = "Ruby Block" });

        await using var context = NewContext();
        var service = Materials(context);

        Assert.DoesNotContain((await service.SearchHybridAsync(Query("apple"))).Items, r => r.NamespaceKey.StartsWith("knk:"));
        Assert.Equal(new[] { "knk:ruby" }, (await service.SearchHybridAsync(Query("ruby", "ITEM"))).Items.Select(r => r.NamespaceKey));
    }

    [Fact]
    public async Task Materials_SortingFromTheTable_IsHonoured()
    {
        await using var context = NewContext();
        var result = await Materials(context).SearchHybridAsync(Query(sortBy: "namespaceKey", descending: true));

        Assert.Equal(new[] { "minecraft:stone", "minecraft:oak_stairs", "minecraft:apple" }, result.Items.Select(r => r.NamespaceKey));
    }

    [Fact]
    public async Task Materials_CreatingAnExistingKeyInAnyNotation_IsAConflictNamingTheRow()
    {
        await SeedAsync(new MinecraftMaterialRef { Id = 7, NamespaceKey = "minecraft:stone", Category = "ITEM" },
            new MinecraftMaterialRef { Id = 8, NamespaceKey = "minecraft:dirt", Category = "ITEM" });

        await using var context = NewContext();
        var service = Materials(context);
        var conflict = await Assert.ThrowsAsync<MinecraftMaterialRefConflictException>(() =>
            service.CreateAsync(new MinecraftMaterialRefCreateDto { NamespaceKey = " Stone ", Category = "ITEM" }));
        Assert.Equal(7, conflict.ExistingId);

        await Assert.ThrowsAsync<MinecraftMaterialRefConflictException>(() =>
            service.UpdateAsync(8, new MinecraftMaterialRefUpdateDto { NamespaceKey = "STONE", Category = "ITEM" }));

        var created = await service.CreateAsync(new MinecraftMaterialRefCreateDto { NamespaceKey = "Blue_Ice", Category = "ITEM" });
        Assert.Equal("minecraft:blue_ice", created.NamespaceKey);
    }

    [Fact]
    public async Task Blocks_TwoSavedStatesOfOneBlock_AreTwoRows_AndTheAnyStateRowMatchesTheCatalogue()
    {
        await SeedAsync(
            new MinecraftBlockRef { Id = 10, NamespaceKey = "minecraft:oak_stairs", BlockStateString = "minecraft:oak_stairs[facing=north]" },
            new MinecraftBlockRef { Id = 11, NamespaceKey = "minecraft:oak_stairs", BlockStateString = "minecraft:oak_stairs[facing=south]" },
            new MinecraftBlockRef { Id = 12, NamespaceKey = "OAK_STAIRS", BlockStateString = null });

        await using var context = NewContext();
        var result = await Blocks(context).SearchHybridAsync(Query("stairs"));

        Assert.Equal(new[] { 10, 11, 12 }, result.Items.Select(r => r.Id).OfType<int>().OrderBy(i => i));
        Assert.Equal(3, result.TotalCount);
        Assert.Null(result.Items.Single(r => r.Id == 12).BlockStateString);
    }

    [Fact]
    public async Task Blocks_GetOrCreate_FindsTheSameStateInAnyNotation_AndLinksAnUnverifiedMaterial()
    {
        await using var context = NewContext();
        var service = Blocks(context);

        var first = await service.GetOrCreateAsync(new MinecraftBlockRefCreateDto { NamespaceKey = "OAK_STAIRS", BlockStateString = "half=bottom, facing=north" });
        var again = await service.GetOrCreateAsync(new MinecraftBlockRefCreateDto { NamespaceKey = "minecraft:oak_stairs", BlockStateString = "minecraft:oak_stairs[facing=north,half=bottom]" });

        Assert.Equal(first.Id, again.Id);
        Assert.Equal("minecraft:oak_stairs[facing=north,half=bottom]", first.BlockStateString);
        var material = await context.MinecraftMaterialRefs.SingleAsync();
        Assert.Equal(("minecraft:oak_stairs", "Oak Stairs", MinecraftCatalogStatus.Unverified), (material.NamespaceKey, material.DisplayName, material.CatalogStatus));
        Assert.Equal(material.Id, first.MaterialRefId);

        await Assert.ThrowsAsync<MinecraftBlockRefConflictException>(() =>
            service.CreateAsync(new MinecraftBlockRefCreateDto { NamespaceKey = "oak_stairs", BlockStateString = "[facing=north,half=bottom]" }));
        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.GetOrCreateAsync(new MinecraftBlockRefCreateDto { NamespaceKey = "oak_stairs", BlockStateString = "[facing]" }));
    }
}
