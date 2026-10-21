using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.MinecraftCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace knkwebapi_v2.Tests.Services.MinecraftCatalog;

/// <summary>KNG-200 phase 2: backfill, audit and merge (docs/specs/minecraft-catalog/IMPLEMENTATION_PLAN.md §8).</summary>
public class MinecraftCatalogMaintenanceServiceTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    private static MinecraftCatalogMaintenanceService Service(KnKDbContext context, params MinecraftMaterialCatalogEntry[] staticEntries)
    {
        var catalog = new Mock<IMinecraftMaterialCatalogService>();
        catalog.Setup(c => c.GetAll()).Returns(staticEntries);
        return new MinecraftCatalogMaintenanceService(context, catalog.Object, NullLogger<MinecraftCatalogMaintenanceService>.Instance);
    }

    private static MinecraftMaterialRef Material(int id, string key, string category = "ITEM") =>
        new() { Id = id, NamespaceKey = key, Category = category };

    private async Task SeedAsync(params object[] rows)
    {
        await using var context = NewContext();
        context.AddRange(rows);
        await context.SaveChangesAsync();
    }

    [Fact]
    public async Task Backfill_NormalisesKeys_WhenTheNormalFormIsFree()
    {
        await SeedAsync(Material(1, "STONE"), Material(2, "minecraft:dirt"), Material(3, "oak_planks"), Material(4, "minecraft:oak_planks"));

        await using (var context = NewContext())
        {
            var result = await Service(context).BackfillAsync();

            Assert.Equal(1, result.MaterialKeysNormalized);
            Assert.Contains(result.Skipped, s => s.Contains("MinecraftMaterialRef 3") && s.Contains("merge needed"));
        }

        await using var check = NewContext();
        var keys = await check.MinecraftMaterialRefs.OrderBy(m => m.Id).Select(m => m.NamespaceKey).ToListAsync();
        Assert.Equal(new[] { "minecraft:stone", "minecraft:dirt", "oak_planks", "minecraft:oak_planks" }, keys);
    }

    [Fact]
    public async Task Backfill_FillsDisplayNames_FromTheStaticCatalogue_ElseFromTheKey_AndLeavesFlagsUnknown()
    {
        await SeedAsync(Material(1, "minecraft:wall_torch"), Material(2, "minecraft:pale_oak_log"),
            new MinecraftMaterialRef { Id = 3, NamespaceKey = "minecraft:stone", Category = "ITEM", DisplayName = "Kept" });

        await using (var context = NewContext())
        {
            await Service(context, new MinecraftMaterialCatalogEntry { NamespaceKey = "minecraft:wall_torch", DisplayName = "Torch", Category = "ITEM" })
                .BackfillAsync();
        }

        await using var check = NewContext();
        var rows = await check.MinecraftMaterialRefs.OrderBy(m => m.Id).ToListAsync();
        Assert.Equal(new[] { "Torch", "Pale Oak Log", "Kept" }, rows.Select(m => m.DisplayName));
        Assert.All(rows, m => Assert.Null(m.IsItem));
        Assert.All(rows, m => Assert.Equal(MinecraftCatalogStatus.Unverified, m.CatalogStatus));
    }

    [Fact]
    public async Task Backfill_LinksBlockRefs_CreatingAnUnverifiedMaterialWhenMissing_AndCanonicalisesTheirState()
    {
        await SeedAsync(
            Material(1, "minecraft:oak_stairs", "BLOCK"),
            new MinecraftBlockRef { Id = 10, NamespaceKey = "Minecraft:Oak_Stairs", BlockStateString = "[half=bottom,facing=north]" },
            new MinecraftBlockRef { Id = 11, NamespaceKey = "minecraft:birch_door", BlockStateString = null });

        await using (var context = NewContext())
        {
            var result = await Service(context).BackfillAsync();

            Assert.Equal(2, result.BlockRefsLinked);
            Assert.Equal(1, result.MaterialsCreated);
            Assert.Equal(2, result.BlockRefsCanonicalized);
        }

        await using var check = NewContext();
        var stairs = await check.MinecraftBlockRefs.SingleAsync(b => b.Id == 10);
        Assert.Equal(1, stairs.MaterialRefId);
        Assert.Equal("minecraft:oak_stairs", stairs.NamespaceKey);
        Assert.Equal("minecraft:oak_stairs[facing=north,half=bottom]", stairs.BlockStateString);
        Assert.Equal(MinecraftBlockState.StateKey(MinecraftBlockState.TryCanonicalize("minecraft:oak_stairs", "[facing=north,half=bottom]")!), stairs.StateKey);

        var door = await check.MinecraftBlockRefs.Include(b => b.MaterialRef).SingleAsync(b => b.Id == 11);
        Assert.Equal("minecraft:birch_door", door.MaterialRef!.NamespaceKey);
        Assert.Equal(MinecraftCatalogStatus.Unverified, door.MaterialRef.CatalogStatus);
        Assert.Equal("Birch Door", door.MaterialRef.DisplayName);
    }

    [Fact]
    public async Task Backfill_IsIdempotent()
    {
        await SeedAsync(Material(1, "STONE"),
            new MinecraftBlockRef { Id = 10, NamespaceKey = "minecraft:oak_stairs", BlockStateString = "facing=north" });

        await using (var first = NewContext()) await Service(first).BackfillAsync();
        await using var second = NewContext();
        var again = await Service(second).BackfillAsync();

        Assert.Equal(0, again.MaterialKeysNormalized + again.MaterialDisplayNamesFilled + again.MaterialsCreated
            + again.BlockRefsLinked + again.BlockRefsCanonicalized);
        Assert.Empty(again.Skipped);
    }

    [Fact]
    public async Task Backfill_LeavesInvalidRowsAlone_AndReportsThem()
    {
        await SeedAsync(Material(1, "oak planks"),
            new MinecraftBlockRef { Id = 10, NamespaceKey = "minecraft:oak_stairs", BlockStateString = "[facing]" });

        await using (var context = NewContext())
        {
            var result = await Service(context).BackfillAsync();
            Assert.Equal(2, result.Skipped.Count);
        }

        await using var check = NewContext();
        Assert.Equal("oak planks", (await check.MinecraftMaterialRefs.SingleAsync()).NamespaceKey);
        Assert.Null((await check.MinecraftBlockRefs.SingleAsync()).MaterialRefId);
    }

    [Fact]
    public async Task Audit_ReportsCollisions_WithTheirReferences_DuplicateStates_AndUnlinkedRows()
    {
        await SeedAsync(
            Material(1, "minecraft:oak_planks"), Material(2, "oak_planks"), Material(3, "oak planks"),
            new MinecraftMaterialRef { Id = 4, NamespaceKey = "minecraft:old_thing", Category = "ITEM", CatalogStatus = MinecraftCatalogStatus.Missing },
            new Category { Id = 20, Name = "Wood", IconMaterialRefId = 2 },
            new ItemBlueprint { Id = 30, Name = "Plank", IconMaterialRefId = 2 },
            new ItemBlueprint { Id = 31, Name = "Relic", IconMaterialRefId = 4 },
            new MinecraftBlockRef { Id = 10, NamespaceKey = "minecraft:oak_stairs", BlockStateString = "[facing=north]" },
            new MinecraftBlockRef { Id = 11, NamespaceKey = "minecraft:oak_stairs", BlockStateString = "minecraft:oak_stairs[facing=north]" });

        await using var context = NewContext();
        var report = await Service(context).AuditAsync();

        Assert.False(report.ReadyForConstraints);
        var collision = Assert.Single(report.MaterialKeyCollisions);
        Assert.Equal(new[] { 1, 2 }, collision.Select(r => r.Id));
        var referenced = collision.Single(r => r.Id == 2);
        Assert.Equal(2, referenced.TotalReferences);
        Assert.Equal(1, referenced.References["Category.IconMaterialRefId"]);
        Assert.Equal(1, referenced.References["ItemBlueprint.IconMaterialRefId"]);
        Assert.Equal(3, Assert.Single(report.InvalidMaterialKeys).Id);
        Assert.Equal(4, Assert.Single(report.MissingButReferenced).Id);
        Assert.Equal(new[] { 10, 11 }, Assert.Single(report.DuplicateBlockStates).Select(b => b.Id));
        Assert.Equal(2, report.UnlinkedBlockRefs.Count);
        Assert.Equal(3, report.UnverifiedCount);
    }

    [Fact]
    public async Task Audit_IsReadyForConstraints_AfterBackfillOnCleanData()
    {
        await SeedAsync(Material(1, "STONE"),
            new MinecraftBlockRef { Id = 10, NamespaceKey = "minecraft:oak_stairs", BlockStateString = "facing=north" });
        await using (var backfill = NewContext()) await Service(backfill).BackfillAsync();

        await using var context = NewContext();
        var report = await Service(context).AuditAsync();

        Assert.True(report.ReadyForConstraints);
        Assert.Empty(report.NotNormalizedMaterialKeys);
        Assert.Empty(report.NotCanonicalBlockRefs);
    }

    [Fact]
    public async Task Merge_RepointsEveryReference_MovesBlockStates_AndDeletesTheSource()
    {
        await SeedAsync(
            Material(1, "minecraft:oak_planks"), Material(2, "oak_planks"),
            new Category { Id = 20, Name = "Wood", IconMaterialRefId = 2 },
            new ItemBlueprint { Id = 30, Name = "Plank", IconMaterialRefId = 2 },
            new ItemBlueprint { Id = 31, Name = "Other", IconMaterialRefId = 1 },
            new MenuTemplate { Id = 40, BackgroundMaterialRefId = 2 },
            new MinecraftBlockRef { Id = 10, NamespaceKey = "oak_planks", BlockStateString = "oak_planks[axis=y]", MaterialRefId = 2 });

        await using (var context = NewContext())
        {
            var result = await Service(context).MergeAsync(2, 1);

            Assert.Equal(1, result.Repointed["Category.IconMaterialRefId"]);
            Assert.Equal(1, result.Repointed["ItemBlueprint.IconMaterialRefId"]);
            Assert.Equal(1, result.Repointed["MenuTemplate.BackgroundMaterialRefId"]);
            Assert.Equal(1, result.Repointed["MinecraftBlockRef.MaterialRefId"]);
        }

        await using var check = NewContext();
        Assert.False(await check.MinecraftMaterialRefs.AnyAsync(m => m.Id == 2));
        Assert.Equal(1, (await check.Categories.SingleAsync()).IconMaterialRefId);
        Assert.All(await check.ItemBlueprints.ToListAsync(), b => Assert.Equal(1, b.IconMaterialRefId));
        Assert.Equal(1, (await check.MenuTemplates.SingleAsync()).BackgroundMaterialRefId);
        var block = await check.MinecraftBlockRefs.SingleAsync();
        Assert.Equal(1, block.MaterialRefId);
        Assert.Equal("minecraft:oak_planks", block.NamespaceKey);
        Assert.Equal("minecraft:oak_planks[axis=y]", block.BlockStateString);
        Assert.Equal(MinecraftBlockState.StateKey(MinecraftBlockState.TryCanonicalize("minecraft:oak_planks", "[axis=y]")!), block.StateKey);
    }

    [Fact]
    public async Task Merge_RefusesUnknownIds_AndMergingIntoItself()
    {
        await SeedAsync(Material(1, "minecraft:stone"));
        await using var context = NewContext();
        var service = Service(context);

        await Assert.ThrowsAsync<ArgumentException>(() => service.MergeAsync(1, 1));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.MergeAsync(1, 99));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.MergeAsync(99, 1));
        Assert.True(await context.MinecraftMaterialRefs.AnyAsync(m => m.Id == 1));
    }
}
