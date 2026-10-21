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
/// KNG-200 phase 4 (plan §9.3): once a scan has completed, the picker routes search the database only,
/// with the kind filter, Missing rows hidden, display names and real sorting. Before that, the hybrid
/// merge still answers.
/// </summary>
public class MinecraftPickerDatabaseModeTests
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

    private static IMinecraftMaterialCatalogService StaticCatalog()
    {
        var catalog = new Mock<IMinecraftMaterialCatalogService>();
        var entries = new[]
        {
            new MinecraftMaterialCatalogEntry { NamespaceKey = "minecraft:stone", DisplayName = "Stone", Category = "ITEM" },
            new MinecraftMaterialCatalogEntry { NamespaceKey = "minecraft:static_only", DisplayName = "Static Only", Category = "ITEM" },
        };
        catalog.Setup(c => c.GetAll()).Returns(entries);
        catalog.Setup(c => c.Search(It.IsAny<string?>(), It.IsAny<string?>())).Returns(entries);
        return catalog.Object;
    }

    private MinecraftMaterialRefService MaterialService(KnKDbContext context, MinecraftPickerSource source = MinecraftPickerSource.Auto) =>
        new(new MinecraftMaterialRefRepository(context), _mapper, StaticCatalog(),
            new MinecraftCatalogPickerSource(context, new MinecraftCatalogOptions { PickerSource = source }));

    private async Task SeedAsync(bool completedScan, params object[] rows)
    {
        await using var context = NewContext();
        context.AddRange(rows);
        if (completedScan) context.MinecraftCatalogScans.Add(new MinecraftCatalogScan { Status = MinecraftCatalogScanStatus.Completed });
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
        all["SearchHybrid"] = "true";
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
        var input = new Dictionary<string, string> { ["SearchHybrid"] = "true" };
        if (kind == null && category == null) input["kind"] = "item";
        if (kind != null) input["kind"] = kind;
        if (category != null) input["category"] = category;

        var result = MinecraftPickerFilters.ForDatabase(input);

        Assert.Equal("true", result["excludeMissing"]);
        Assert.Equal(expectedKind, result.GetValueOrDefault("kind"));
        Assert.Equal(expectedKeyContains, result.GetValueOrDefault("keyContains"));
        Assert.False(result.ContainsKey("SearchHybrid"));
        Assert.False(result.ContainsKey("category"));
    }

    [Fact]
    public async Task PickerSource_Auto_SwitchesAfterTheFirstCompletedScan_AndConfigOverrides()
    {
        await using var context = NewContext();
        async Task<bool> Use(MinecraftPickerSource source) =>
            await new MinecraftCatalogPickerSource(context, new MinecraftCatalogOptions { PickerSource = source }).UseDatabaseAsync();

        context.MinecraftCatalogScans.Add(new MinecraftCatalogScan { Status = MinecraftCatalogScanStatus.Failed });
        await context.SaveChangesAsync();
        Assert.False(await Use(MinecraftPickerSource.Auto));
        Assert.True(await Use(MinecraftPickerSource.Database));

        context.MinecraftCatalogScans.Add(new MinecraftCatalogScan { Status = MinecraftCatalogScanStatus.Completed });
        await context.SaveChangesAsync();
        Assert.True(await Use(MinecraftPickerSource.Auto));
        Assert.False(await Use(MinecraftPickerSource.Hybrid));
    }

    [Fact]
    public async Task DatabaseMode_ItemPicker_ShowsItemsOnly_HidesMissing_SortsByName_AndMarksRowsSaved()
    {
        await SeedAsync(true,
            Scanned(1, "minecraft:stone", "Stone", true, true),
            Scanned(2, "minecraft:wall_torch", "Wall Torch", false, true),
            Scanned(3, "minecraft:apple", "Apple", true, false),
            Scanned(4, "minecraft:old_relic", "Old Relic", true, false, MinecraftCatalogStatus.Missing),
            new MinecraftMaterialRef { Id = 5, NamespaceKey = "minecraft:seeded_sword", Category = "ITEM" }, // never scanned
            new MinecraftMaterialRef { Id = 6, NamespaceKey = "minecraft:seeded_block", Category = "BLOCK" });

        await using var context = NewContext();
        var result = await MaterialService(context).SearchHybridAsync(PickerQuery(null, ("kind", "item")));

        Assert.Equal(new[] { "Apple", "Seeded Sword", "Stone" }, result.Items.Select(r => r.DisplayName));
        Assert.Equal(3, result.TotalCount);
        Assert.All(result.Items, r => Assert.True(r.IsPersisted));
        Assert.DoesNotContain(result.Items, r => r.NamespaceKey == "minecraft:static_only");
    }

    [Fact]
    public async Task DatabaseMode_SearchMatchesNames_AndSortsAsAsked_WithTheOverriddenIcon()
    {
        await SeedAsync(true,
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
        var result = await MaterialService(context).SearchHybridAsync(query);

        Assert.Equal(new[] { "Oak Planks", "Oak Log" }, result.Items.Select(r => r.DisplayName));
        Assert.Equal("https://mine/oak_log.png", result.Items[1].IconUrl);
    }

    [Fact]
    public async Task DatabaseMode_GetHybrid_ReturnsDatabaseRowsOnly()
    {
        await SeedAsync(true, Scanned(1, "minecraft:dirt_path", "Dirt Path", true, true), Scanned(2, "minecraft:dirt", "Dirt", true, true));

        await using var context = NewContext();
        var options = (await MaterialService(context).GetHybridAsync("dirt", null, 1)).ToList();

        var option = Assert.Single(options);
        Assert.Equal(("minecraft:dirt", "Dirt", true), (option.NamespaceKey, option.DisplayName, option.IsPersisted));
    }

    [Fact]
    public async Task BeforeTheFirstScan_TheHybridMergeStillAnswers()
    {
        await SeedAsync(false, new MinecraftMaterialRef { Id = 1, NamespaceKey = "minecraft:stone", Category = "ITEM" });

        await using var context = NewContext();
        var result = await MaterialService(context).SearchHybridAsync(PickerQuery());

        Assert.Contains(result.Items, r => r.NamespaceKey == "minecraft:static_only" && r.IsPersisted == false);
        Assert.Contains(result.Items, r => r.NamespaceKey == "minecraft:stone" && r.IsPersisted == true);
    }

    [Fact]
    public async Task DatabaseMode_BlockRefs_TwoStatesOfOneBlock_AndTheMaterialsIconAndName()
    {
        var stairs = Scanned(1, "minecraft:oak_stairs", "Oak Stairs", true, true);
        stairs.IconUrl = "https://scan/oak_stairs.png";
        await SeedAsync(true, stairs,
            new MinecraftBlockRef { Id = 10, NamespaceKey = "minecraft:oak_stairs", BlockStateString = "minecraft:oak_stairs[facing=north]", MaterialRefId = 1 },
            new MinecraftBlockRef { Id = 11, NamespaceKey = "minecraft:oak_stairs", BlockStateString = "minecraft:oak_stairs[facing=south]", MaterialRefId = 1 });

        await using var context = NewContext();
        var service = new MinecraftBlockRefService(new MinecraftBlockRefRepository(context), _mapper, StaticCatalog(),
            new MinecraftCatalogPickerSource(context, new MinecraftCatalogOptions()), new MinecraftMaterialRefRepository(context));
        var result = await service.SearchHybridAsync(PickerQuery());

        Assert.Equal(2, result.TotalCount);
        Assert.All(result.Items, r => Assert.Equal(("https://scan/oak_stairs.png", "Oak Stairs", (bool?)true), (r.IconUrl, r.DisplayName, r.IsPersisted)));
    }

    [Fact]
    public async Task GetOrCreate_NormalisesTheKey_AndCreatesAnUnverifiedRowWithAName()
    {
        await SeedAsync(true, Scanned(1, "minecraft:stone", "Stone", true, true));

        await using var context = NewContext();
        var service = MaterialService(context);
        var existing = await service.GetOrCreateAsync("STONE", "ITEM");
        var created = await service.GetOrCreateAsync(" Minecraft:Blue_Ice ", "ITEM");

        Assert.Equal(1, existing.Id);
        Assert.Equal("minecraft:blue_ice", created.NamespaceKey);
        var row = await context.MinecraftMaterialRefs.SingleAsync(m => m.Id == created.Id);
        Assert.Equal(("Blue Ice", MinecraftCatalogStatus.Unverified), (row.DisplayName, row.CatalogStatus));
        await Assert.ThrowsAsync<ArgumentException>(() => service.GetOrCreateAsync("blue ice", "ITEM"));
    }

    [Fact]
    public void Factory_NormalisesAndPrefersTheStaticName()
    {
        var withEntry = MinecraftMaterialRefFactory.NewUnverified("TORCH",
            new MinecraftMaterialCatalogEntry { NamespaceKey = "minecraft:torch", DisplayName = "Torch", Category = "ITEM", IconUrl = "https://x/torch.png" }, "Uncategorized");
        var bare = MinecraftMaterialRefFactory.NewUnverified("minecraft:soul_lantern", null, "Uncategorized");

        Assert.Equal(("minecraft:torch", "Torch", "ITEM", "https://x/torch.png"), (withEntry.NamespaceKey, withEntry.DisplayName, withEntry.Category, withEntry.IconUrl));
        Assert.Equal(("Soul Lantern", "Uncategorized", MinecraftCatalogStatus.Unverified), (bare.DisplayName, bare.Category, bare.CatalogStatus));
    }
}
