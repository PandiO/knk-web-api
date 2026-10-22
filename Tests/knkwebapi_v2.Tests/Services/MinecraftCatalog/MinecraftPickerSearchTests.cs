using AutoMapper;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Mapping;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Repositories;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.MinecraftCatalog;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services.MinecraftCatalog;

/// <summary>
/// KNG-200: the database is the catalogue. Picker searches filter on kind, hide Missing rows and sort
/// by display name; a key or block state can't be created twice; block refs link their material.
/// </summary>
public class MinecraftPickerSearchTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private readonly IMapper _mapper = new MapperConfiguration(cfg =>
    {
        cfg.AddProfile<PagedQueryMappingProfile>();
        cfg.AddProfile<MinecraftMaterialRefMappingProfile>();
        cfg.AddProfile<MinecraftBlockRefMappingProfile>();
    }).CreateMapper();

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    private MinecraftMaterialRefService Materials(KnKDbContext context) => new(new MinecraftMaterialRefRepository(context), _mapper);

    private MinecraftBlockRefService Blocks(KnKDbContext context) =>
        new(new MinecraftBlockRefRepository(context), _mapper, new MinecraftMaterialRefRepository(context));

    private async Task SeedAsync(params object[] rows)
    {
        await using var context = NewContext();
        context.AddRange(rows);
        await context.SaveChangesAsync();
    }

    private static MinecraftMaterialRef Scanned(int id, string key, string name, bool isItem, bool isBlock,
        MinecraftCatalogStatus status = MinecraftCatalogStatus.Active) => new()
    {
        Id = id, NamespaceKey = key, Category = isBlock ? "BLOCK" : "ITEM", DisplayName = name,
        IsItem = isItem, IsBlock = isBlock, CatalogStatus = status,
    };

    private static PagedQueryDto PickerQuery(string? search = null, params (string Key, string Value)[] filters)
    {
        var all = filters.ToDictionary(f => f.Key, f => f.Value);
        all[MinecraftPickerFilters.Picker] = "true";
        return new PagedQueryDto { PageNumber = 1, PageSize = 20, SearchTerm = search, Filters = all };
    }

    [Theory]
    [InlineData(null, null, "item", null)]
    [InlineData("block", null, "block", null)]
    [InlineData("item", "BLOCK", "block", null)]
    [InlineData(null, "ICON", "item", null)]
    [InlineData(null, "item", "item", null)]
    [InlineData("item", "ANY", null, null)]
    [InlineData("item", "BANNER", "item", "_banner")]
    [InlineData("nonsense", null, null, null)]
    public void PickerFilters_MapKindAndLegacyCategory(string? kind, string? category, string? expectedKind, string? expectedKeyContains)
    {
        var input = new Dictionary<string, string> { [MinecraftPickerFilters.Picker] = "true" };
        if (kind == null && category == null) input["kind"] = "item";
        if (kind != null) input["kind"] = kind;
        if (category != null) input["category"] = category;

        var result = MinecraftPickerFilters.ForDatabase(input);

        Assert.Equal("true", result["excludeMissing"]);
        Assert.Equal(expectedKind, result.GetValueOrDefault("kind"));
        Assert.Equal(expectedKeyContains, result.GetValueOrDefault("keyContains"));
        Assert.False(result.ContainsKey(MinecraftPickerFilters.Picker));
        Assert.False(result.ContainsKey("category"));
    }

    [Theory]
    [InlineData("Picker", "true", true)]
    [InlineData("SearchHybrid", "TRUE", true)]
    [InlineData("picker", "false", false)]
    [InlineData("kind", "item", false)]
    public void IsPickerSearch_AcceptsTheFlagAndItsOldName(string key, string value, bool expected)
    {
        Assert.Equal(expected, MinecraftPickerFilters.IsPickerSearch(new Dictionary<string, string> { [key] = value }));
    }

    [Fact]
    public async Task ItemPicker_ShowsItemsOnly_HidesMissing_SortsByName_AndMarksRowsSaved()
    {
        await SeedAsync(
            Scanned(1, "minecraft:stone", "Stone", true, true),
            Scanned(2, "minecraft:wall_torch", "Wall Torch", false, true),
            Scanned(3, "minecraft:apple", "Apple", true, false),
            Scanned(4, "minecraft:old_relic", "Old Relic", true, false, MinecraftCatalogStatus.Missing),
            new MinecraftMaterialRef { Id = 5, NamespaceKey = "minecraft:seeded_sword", Category = "ITEM" }, // never scanned
            new MinecraftMaterialRef { Id = 6, NamespaceKey = "minecraft:seeded_block", Category = "BLOCK" });

        await using var context = NewContext();
        var result = await Materials(context).SearchPickerAsync(PickerQuery(null, ("kind", "item")));

        Assert.Equal(new[] { "Apple", "Seeded Sword", "Stone" }, result.Items.Select(r => r.DisplayName));
        Assert.Equal(3, result.TotalCount);
        Assert.All(result.Items, r => Assert.True(r.IsPersisted));
    }

    [Fact]
    public async Task AdminSearch_StillShowsMissingAndEveryKind()
    {
        await SeedAsync(
            Scanned(1, "minecraft:wall_torch", "Wall Torch", false, true),
            Scanned(2, "minecraft:old_relic", "Old Relic", true, false, MinecraftCatalogStatus.Missing));

        await using var context = NewContext();
        var result = await Materials(context).SearchAsync(new PagedQueryDto { PageNumber = 1, PageSize = 20 });

        Assert.Equal(2, result.TotalCount);
    }

    [Fact]
    public async Task PickerSearch_MatchesNames_SortsAsAsked_WithTheOverriddenIcon()
    {
        await SeedAsync(
            Scanned(1, "minecraft:oak_planks", "Oak Planks", true, true),
            Scanned(2, "minecraft:oak_log", "Oak Log", true, true),
            Scanned(3, "minecraft:birch_planks", "Birch Planks", true, true));
        await using (var setup = NewContext())
        {
            var log = await setup.MinecraftMaterialRefs.SingleAsync(m => m.Id == 2);
            log.IconUrl = "https://scan/oak_log.png";
            log.IconUrlOverride = "https://mine/oak_log.png";
            await setup.SaveChangesAsync();
        }

        await using var context = NewContext();
        var query = PickerQuery("oak", ("kind", "block"));
        query.SortBy = "displayName";
        query.SortDescending = true;
        var result = await Materials(context).SearchPickerAsync(query);

        Assert.Equal(new[] { "Oak Planks", "Oak Log" }, result.Items.Select(r => r.DisplayName));
        Assert.Equal("https://mine/oak_log.png", result.Items[1].IconUrl);
    }

    [Fact]
    public async Task BlockPicker_TwoStatesOfOneBlock_WithTheMaterialsIconAndName()
    {
        var stairs = Scanned(1, "minecraft:oak_stairs", "Oak Stairs", true, true);
        stairs.IconUrl = "https://scan/oak_stairs.png";
        await SeedAsync(stairs,
            new MinecraftBlockRef { Id = 10, NamespaceKey = "minecraft:oak_stairs", BlockStateString = "minecraft:oak_stairs[facing=north]", MaterialRefId = 1, StateKey = new string('a', 64) },
            new MinecraftBlockRef { Id = 11, NamespaceKey = "minecraft:oak_stairs", BlockStateString = "minecraft:oak_stairs[facing=south]", MaterialRefId = 1, StateKey = new string('b', 64) });

        await using var context = NewContext();
        var result = await Blocks(context).SearchPickerAsync(PickerQuery());

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, r => Assert.Equal(("https://scan/oak_stairs.png", "Oak Stairs", (bool?)true), (r.IconUrl, r.DisplayName, r.IsPersisted)));
    }

    [Fact]
    public async Task GetOrCreate_NormalisesTheKey_AndCreatesAnUnverifiedRowWithAName()
    {
        await SeedAsync(Scanned(1, "minecraft:stone", "Stone", true, true));

        await using var context = NewContext();
        var service = Materials(context);
        var existing = await service.GetOrCreateAsync("STONE", "ITEM");
        var created = await service.GetOrCreateAsync(" Minecraft:Blue_Ice ", "ITEM");

        Assert.Equal(1, existing.Id);
        Assert.Equal("minecraft:blue_ice", created.NamespaceKey);
        var row = await context.MinecraftMaterialRefs.SingleAsync(m => m.Id == created.Id);
        Assert.Equal(("Blue Ice", MinecraftCatalogStatus.Unverified, "ITEM"), (row.DisplayName, row.CatalogStatus, row.Category));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetOrCreateAsync("blue ice", "ITEM"));
    }

    [Fact]
    public async Task CreatingAnExistingKeyInAnyNotation_IsAConflictNamingTheRow()
    {
        await SeedAsync(Scanned(7, "minecraft:stone", "Stone", true, true), Scanned(8, "minecraft:dirt", "Dirt", true, true));

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
    public async Task BlockGetOrCreate_FindsTheSameStateInAnyNotation_AndLinksAnUnverifiedMaterial()
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

    [Fact]
    public void Factory_NormalisesTheKey_AndNamesTheRow()
    {
        var material = MinecraftMaterialRefFactory.NewUnverified("SOUL_LANTERN", "Uncategorized");

        Assert.Equal(("minecraft:soul_lantern", "Soul Lantern", "Uncategorized", MinecraftCatalogStatus.Unverified),
            (material.NamespaceKey, material.DisplayName, material.Category, material.CatalogStatus));
    }
}
