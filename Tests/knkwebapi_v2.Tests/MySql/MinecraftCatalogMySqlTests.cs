using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.MinecraftCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// KNG-200 phase 2 on a real MySQL: the migration keeps existing rows (as Unverified) and rolls back,
/// the unique key index is case-insensitive like the backfill assumes, and backfill → audit → merge
/// work against the real schema.
/// </summary>
[Trait("Category", "requires-mysql")]
public class MinecraftCatalogMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private const string MigrationBefore = "20261012091824_LocationDeleteKeepsDomain";
    private const string PhaseTwoMigration = "20261021083312_MinecraftCatalogEntryFields";

    private readonly MySqlTestDatabase _db;

    public MinecraftCatalogMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private static MinecraftCatalogMaintenanceService Service(knkwebapi_v2.Properties.KnKDbContext context) =>
        new(context, NullLogger<MinecraftCatalogMaintenanceService>.Instance);

    /// <summary>
    /// The upgrade path of an environment that gets every KNG-200 migration at once: the phase 2 migration
    /// keeps the rows (Unverified), the constraints migration refuses until the backfill has linked and keyed
    /// the block refs, and after backfill and a merge it applies.
    /// </summary>
    [MySqlFact]
    public async Task UpgradePath_PhaseTwoKeepsRows_ConstraintsWaitForTheBackfill_ThenApply()
    {
        await using (var context = _db.NewContext())
        {
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync(MigrationBefore);

            // Rows as they exist before KNG-200.
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO minecraftmaterialrefs (Id, NamespaceKey, Category) VALUES (1, 'minecraft:oak_planks', 'ITEM'), (2, 'oak_planks', 'ITEM'), (3, 'STONE', 'ITEM')");
            await context.Database.ExecuteSqlRawAsync(
                "INSERT INTO minecraftblockrefs (Id, NamespaceKey, BlockStateString) VALUES (10, 'oak_stairs', '[half=bottom,facing=north]')");

            await migrator.MigrateAsync(PhaseTwoMigration);

            // The block ref has no material and no StateKey yet: the constraints migration refuses, changing nothing.
            var refused = await Assert.ThrowsAnyAsync<Exception>(() => migrator.MigrateAsync());
            Assert.Contains("KNG-200", refused.Message);
        }

        await using (var context = _db.NewContext())
        {
            var rows = await context.MinecraftMaterialRefs.OrderBy(m => m.Id).ToListAsync();
            Assert.Equal(3, rows.Count);
            Assert.All(rows, m => Assert.Equal(MinecraftCatalogStatus.Unverified, m.CatalogStatus));
            Assert.All(rows, m => Assert.Null(m.IsItem));

            // The unique key index is case-insensitive (utf8mb4_general_ci): the backfill relies on that.
            context.MinecraftMaterialRefs.Add(new MinecraftMaterialRef { NamespaceKey = "Minecraft:Oak_Planks", Category = "ITEM" });
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }

        await using (var context = _db.NewContext())
        {
            var backfill = await Service(context).BackfillAsync();
            Assert.Equal(1, backfill.MaterialKeysNormalized); // STONE
            Assert.Equal(1, backfill.MaterialsCreated); // minecraft:oak_stairs for the block ref
            Assert.Equal(1, backfill.BlockRefsLinked);
            Assert.Contains(backfill.Skipped, s => s.Contains("MinecraftMaterialRef 2"));

            context.ItemBlueprints.Add(new ItemBlueprint { Name = "Plank", IconMaterialRefId = 2 });
            await context.SaveChangesAsync();
        }

        await using (var context = _db.NewContext())
        {
            var audit = await Service(context).AuditAsync();
            Assert.Equal(new[] { 1, 2 }, Assert.Single(audit.MaterialKeyCollisions).Select(r => r.Id));
            Assert.False(audit.ReadyForConstraints);

            var merge = await Service(context).MergeAsync(2, 1);
            Assert.Equal(1, merge.Repointed["ItemBlueprint.IconMaterialRefId"]);
            Assert.True((await Service(context).AuditAsync()).ReadyForConstraints);

            await context.GetService<IMigrator>().MigrateAsync();
        }

        await using (var context = _db.NewContext())
        {
            Assert.False(await context.MinecraftMaterialRefs.AnyAsync(m => m.Id == 2));
            Assert.Equal(1, (await context.ItemBlueprints.SingleAsync(b => b.Name == "Plank")).IconMaterialRefId);
            var block = await context.MinecraftBlockRefs.Include(b => b.MaterialRef).SingleAsync();
            Assert.Equal("minecraft:oak_stairs[facing=north,half=bottom]", block.BlockStateString);
            Assert.Equal("minecraft:oak_stairs", block.MaterialRef!.NamespaceKey);

            // StateKey is unique now.
            context.MinecraftBlockRefs.Add(new MinecraftBlockRef
            {
                NamespaceKey = block.NamespaceKey, BlockStateString = block.BlockStateString,
                StateKey = block.StateKey, MaterialRefId = block.MaterialRefId,
            });
            await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync());
        }
    }
}
