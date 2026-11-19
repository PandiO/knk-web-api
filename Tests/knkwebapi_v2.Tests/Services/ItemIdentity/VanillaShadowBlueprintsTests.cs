using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.ItemIdentity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static knkwebapi_v2.Tests.Services.ItemIdentity.ItemIdentityTestDb;

namespace knkwebapi_v2.Tests.Services.ItemIdentity;

/// <summary>Item identity (#141, D11/G4): every obtainable material gets exactly one blueprint; shadows are plain and Uncurated.</summary>
public class VanillaShadowBlueprintsTests
{
    private readonly ItemIdentityTestDb _db = new();

    private async Task<knkwebapi_v2.Dtos.ItemShadowGenerationResultDto> GenerateAsync()
    {
        await using var context = _db.NewContext();
        return await VanillaShadowBlueprints.GenerateAsync(context, NullLogger.Instance, CancellationToken.None);
    }

    [Fact]
    public async Task Generate_CreatesOnePlainShadowPerObtainableMaterial_InTheUncuratedCategory()
    {
        await _db.SeedAsync(Item(1, "minecraft:oak_log", name: "Oak Log"), Item(2, "minecraft:diamond_sword", maxStack: 1));

        var result = await GenerateAsync();

        Assert.Equal((2, 0, 2, true), (result.ObtainableMaterials, result.AlreadyMatched, result.Created, result.CategoryCreated));
        await using var check = _db.NewContext();
        var category = await check.Categories.SingleAsync();
        Assert.Equal((VanillaShadowBlueprints.UncuratedCategoryName, (int?)null), (category.Name, category.ParentCategoryId));
        var log = await check.ItemBlueprints.SingleAsync(b => b.VanillaMaterialMatchRefId == 1);
        Assert.Equal(("Oak Log", "", "", 64, 1, category.Id), (log.Name, log.DefaultDisplayName, log.DefaultDisplayDescription, log.MaxStackSize, log.IconMaterialRefId, log.CategoryId));
        Assert.Equal(1, (await check.ItemBlueprints.SingleAsync(b => b.VanillaMaterialMatchRefId == 2)).MaxStackSize);
    }

    [Fact]
    public async Task Generate_SkipsMaterialsThatAreNotObtainableItems()
    {
        await _db.SeedAsync(
            Item(1, "minecraft:stone"),
            Item(2, "minecraft:wall_torch", isItem: false),
            Item(3, "minecraft:old_thing", status: MinecraftCatalogStatus.Missing),
            Item(4, "minecraft:unscanned", status: MinecraftCatalogStatus.Unverified),
            Item(5, "minecraft:zombie_spawn_egg"),
            Item(6, "minecraft:command_block"),
            Item(7, "minecraft:air"));

        var result = await GenerateAsync();

        Assert.Equal((1, 1), (result.ObtainableMaterials, result.Created));
        await using var check = _db.NewContext();
        Assert.Equal(1, (await check.ItemBlueprints.SingleAsync()).VanillaMaterialMatchRefId);
    }

    [Fact]
    public async Task Generate_LeavesCuratedMatches_AndIsIdempotent()
    {
        await _db.SeedAsync(
            new Category { Id = 9, Name = VanillaShadowBlueprints.UncuratedCategoryName },
            Item(1, "minecraft:oak_log"), Item(2, "minecraft:bread"),
            new ItemBlueprint { Id = 50, Name = "Phoenix Bread", DefaultDisplayName = "&6Bread", VanillaMaterialMatchRefId = 2 });

        var first = await GenerateAsync();
        var second = await GenerateAsync();

        Assert.Equal((2, 1, 1, false, 9), (first.ObtainableMaterials, first.AlreadyMatched, first.Created, first.CategoryCreated, first.UncuratedCategoryId));
        Assert.Equal((2, 0), (second.AlreadyMatched, second.Created));
        await using var check = _db.NewContext();
        Assert.Equal(2, await check.ItemBlueprints.CountAsync());
        Assert.Equal(50, (await check.ItemBlueprints.SingleAsync(b => b.VanillaMaterialMatchRefId == 2)).Id);
        Assert.Equal(1, await check.Categories.CountAsync());
    }

    [Fact]
    public async Task Generate_WithNothingToCreate_DoesNotCreateTheCategory()
    {
        await _db.SeedAsync(Item(1, "minecraft:wall_torch", isItem: false));

        var result = await GenerateAsync();

        Assert.Equal((0, false, 0), (result.Created, result.CategoryCreated, result.UncuratedCategoryId));
        await using var check = _db.NewContext();
        Assert.Empty(await check.Categories.ToListAsync());
    }

    [Theory]
    [InlineData("minecraft:oak_log", true, MinecraftCatalogStatus.Active, true)]
    [InlineData("OAK_LOG", true, MinecraftCatalogStatus.Active, true)]
    [InlineData("minecraft:oak_log", false, MinecraftCatalogStatus.Active, false)]
    [InlineData("minecraft:oak_log", null, MinecraftCatalogStatus.Unverified, false)]
    [InlineData("minecraft:barrier", true, MinecraftCatalogStatus.Active, false)]
    [InlineData("minecraft:pig_spawn_egg", true, MinecraftCatalogStatus.Active, false)]
    public void IsObtainable(string key, bool? isItem, MinecraftCatalogStatus status, bool expected) =>
        Assert.Equal(expected, VanillaShadowBlueprints.IsObtainable(key, isItem, status));
}
