using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.MinecraftCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace knkwebapi_v2.Tests.MySql;

/// <summary>
/// KNG-200 phase 3 on a real MySQL: a scan request creates its session and WorldTask with real FKs, and
/// a snapshot is applied in one transaction (FOR UPDATE on the scan row), including a merge whose kept
/// row only changes case, which the case-insensitive unique key index must accept.
/// </summary>
[Trait("Category", "requires-mysql")]
public class MinecraftCatalogScanMySqlTests : IClassFixture<MySqlTestDatabase>
{
    private readonly MySqlTestDatabase _db;

    public MinecraftCatalogScanMySqlTests(MySqlTestDatabase db)
    {
        _db = db;
    }

    private sealed class NoIcons : IMinecraftIconResolver
    {
        public Task<IReadOnlyDictionary<string, MinecraftIconResult>> ResolveAsync(
            string? minecraftVersion, IReadOnlyCollection<MinecraftIconRequest> entries, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, MinecraftIconResult>>(new Dictionary<string, MinecraftIconResult>());
    }

    private MinecraftCatalogScanService Service(knkwebapi_v2.Properties.KnKDbContext context) =>
        new(context, new NoIcons(), NullLogger<MinecraftCatalogScanService>.Instance);

    [MySqlFact]
    public async Task RequestAndIngest_OnMySql()
    {
        int userId;
        await using (var context = _db.NewContext())
        {
            var user = new User { Username = "s" + Guid.NewGuid().ToString("N")[..12] };
            context.Users.Add(user);
            context.MinecraftMaterialRefs.AddRange(
                new MinecraftMaterialRef { NamespaceKey = "Minecraft:Oak_Planks", Category = "ITEM" },
                new MinecraftMaterialRef { NamespaceKey = "oak_planks", Category = "ITEM" },
                new MinecraftMaterialRef { NamespaceKey = "minecraft:gone", Category = "ITEM" });
            await context.SaveChangesAsync();
            userId = user.Id;
        }

        int scanId;
        await using (var context = _db.NewContext())
        {
            scanId = (await Service(context).RequestScanAsync(userId)).Id;
        }

        var entries = new[]
        {
            new MinecraftCatalogEntryDto { Key = "minecraft:oak_planks", IsItem = true, IsBlock = true, MaxStackSize = 64, DisplayName = "Oak Planks", DefaultBlockState = "minecraft:oak_planks" },
            new MinecraftCatalogEntryDto { Key = "minecraft:apple", IsItem = true, MaxStackSize = 64 },
        };
        MinecraftCatalogScanDto result;
        await using (var context = _db.NewContext())
        {
            result = await Service(context).IngestAsync(scanId, new MinecraftCatalogSnapshotDto
            {
                SchemaVersion = 1, MinecraftVersion = "1.21.10", ServerVersion = "Paper", Entries = entries.ToList(),
                ContentHash = MinecraftCatalogSnapshotHash.Compute(entries),
            });
        }

        Assert.Equal("Completed", result.Status);
        Assert.Equal((1, 1, 1), (result.Inserted, result.Merged, result.Deleted));
        await using var check = _db.NewContext();
        var planks = await check.MinecraftMaterialRefs.Where(m => m.NamespaceKey == "minecraft:oak_planks").ToListAsync();
        var kept = Assert.Single(planks);
        Assert.Equal("minecraft:oak_planks", kept.NamespaceKey); // case fixed on the kept row
        Assert.Equal(MinecraftCatalogStatus.Active, kept.CatalogStatus);
        Assert.False(await check.MinecraftMaterialRefs.AnyAsync(m => m.NamespaceKey == "minecraft:gone"));
        var task = await check.WorldTasks.Include(t => t.WorkflowSession).SingleAsync(t => t.Id == result.WorldTaskId);
        Assert.Equal((WorldTaskTypes.MinecraftCatalogScan, userId), (task.TaskType, task.WorkflowSession.UserId));
    }
}
