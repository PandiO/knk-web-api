using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.ItemIdentity;
using knkwebapi_v2.Services.MinecraftCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// Item identity (PandiO/knk-workspace#141) on a real MySQL: the unique vanilla match index (many NULLs allowed, one
/// blueprint per material), shadow generation and a match move against it, and a catalogue merge of two matched
/// materials, which would otherwise break the index.
/// </summary>
[Trait("Category", "requires-mysql")]
public class ItemIdentityMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public ItemIdentityMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private async Task<int> MaterialAsync(string key)
    {
        await using var ctx = _db.NewContext();
        var material = new MinecraftMaterialRef
        {
            NamespaceKey = key, Category = "ITEM", IsItem = true, CatalogStatus = MinecraftCatalogStatus.Active, MaxStackSize = 64,
        };
        ctx.MinecraftMaterialRefs.Add(material);
        await ctx.SaveChangesAsync();
        return material.Id;
    }

    private static string Key() => "test:item_" + Guid.NewGuid().ToString("N")[..10];

    [MySqlFact]
    public async Task UniqueIndex_AllowsManyUnmatchedBlueprints_ButOnlyOneMatchPerMaterial()
    {
        var material = await MaterialAsync(Key());
        await using (var ctx = _db.NewContext())
        {
            ctx.ItemBlueprints.AddRange(new ItemBlueprint { Name = "a" }, new ItemBlueprint { Name = "b" },
                new ItemBlueprint { Name = "c", VanillaMaterialMatchRefId = material });
            await ctx.SaveChangesAsync();
        }

        await using var second = _db.NewContext();
        second.ItemBlueprints.Add(new ItemBlueprint { Name = "d", VanillaMaterialMatchRefId = material });
        await Assert.ThrowsAsync<DbUpdateException>(() => second.SaveChangesAsync());
    }

    [MySqlFact]
    public async Task Generate_ThenMoveTheMatch_KeepsOneBlueprintPerMaterial()
    {
        var material = await MaterialAsync(Key());
        await using (var ctx = _db.NewContext())
        {
            await VanillaShadowBlueprints.GenerateAsync(ctx, NullLogger.Instance, CancellationToken.None);
        }
        int curatedId;
        await using (var ctx = _db.NewContext())
        {
            var curated = new ItemBlueprint { Name = "Curated", DefaultDisplayName = "&6Curated" };
            ctx.ItemBlueprints.Add(curated);
            await ctx.SaveChangesAsync();
            curatedId = curated.Id;
            await new ItemIdentityService(ctx, NullLogger<ItemIdentityService>.Instance).SetMatchAsync(material, curatedId);
        }

        await using var check = _db.NewContext();
        var holder = await check.ItemBlueprints.SingleAsync(b => b.VanillaMaterialMatchRefId == material);
        Assert.Equal(curatedId, holder.Id);
    }

    [MySqlFact]
    public async Task Merge_OfTwoMatchedMaterials_KeepsTheTargetsMatch()
    {
        var target = await MaterialAsync(Key());
        var source = await MaterialAsync(Key());
        int keptId, clearedId;
        await using (var ctx = _db.NewContext())
        {
            var kept = new ItemBlueprint { Name = "kept", VanillaMaterialMatchRefId = target };
            var cleared = new ItemBlueprint { Name = "cleared", VanillaMaterialMatchRefId = source };
            ctx.ItemBlueprints.AddRange(kept, cleared);
            await ctx.SaveChangesAsync();
            (keptId, clearedId) = (kept.Id, cleared.Id);
        }

        await using (var ctx = _db.NewContext())
        {
            await new MinecraftCatalogMaintenanceService(ctx, NullLogger<MinecraftCatalogMaintenanceService>.Instance).MergeAsync(source, target);
        }

        await using var check = _db.NewContext();
        Assert.Equal(target, (await check.ItemBlueprints.SingleAsync(b => b.Id == keptId)).VanillaMaterialMatchRefId);
        Assert.Null((await check.ItemBlueprints.SingleAsync(b => b.Id == clearedId)).VanillaMaterialMatchRefId);
    }
}
