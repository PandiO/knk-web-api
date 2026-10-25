using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Properties;
using knkwebapi_v2.Services.MinecraftCatalog;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace knkwebapi_v2.Tests.Services.MinecraftCatalog;

/// <summary>KNG-200 phase 3: scan requests and snapshot ingest (plan §5.2a, §6, decisions D3/D7).</summary>
public class MinecraftCatalogScanServiceTests
{
    private readonly string _dbName = Guid.NewGuid().ToString();
    private DateTime _now = new(2026, 10, 21, 12, 0, 0, DateTimeKind.Utc);

    private sealed class FakeIcons : IMinecraftIconResolver
    {
        public Dictionary<string, MinecraftIconResult> Results { get; } = new();
        public int Calls { get; private set; }

        public Task<IReadOnlyDictionary<string, MinecraftIconResult>> ResolveAsync(
            string? minecraftVersion, IReadOnlyCollection<MinecraftIconRequest> entries, CancellationToken ct)
        {
            Calls++;
            return Task.FromResult<IReadOnlyDictionary<string, MinecraftIconResult>>(
                entries.Where(e => Results.ContainsKey(e.Key)).ToDictionary(e => e.Key, e => Results[e.Key]));
        }
    }

    private readonly FakeIcons _icons = new();

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    private MinecraftCatalogScanService Service(KnKDbContext context) =>
        new(context, _icons, NullLogger<MinecraftCatalogScanService>.Instance, () => _now);

    private async Task SeedAsync(params object[] rows)
    {
        await using var context = NewContext();
        context.AddRange(rows);
        await context.SaveChangesAsync();
    }

    private async Task<int> RequestAsync()
    {
        await using var context = NewContext();
        return (await Service(context).RequestScanAsync(7)).Id;
    }

    private static MinecraftCatalogEntryDto Entry(string key, bool isItem = true, bool isBlock = false, string? name = null) => new()
    {
        Key = key, IsItem = isItem, IsBlock = isBlock, MaxStackSize = 64, DisplayName = name,
        DefaultBlockState = isBlock ? key : null,
    };

    private static MinecraftCatalogSnapshotDto Snapshot(params MinecraftCatalogEntryDto[] entries) => new()
    {
        SchemaVersion = 1,
        MinecraftVersion = "1.21.10",
        ServerVersion = "Paper test",
        Entries = entries.ToList(),
        ContentHash = MinecraftCatalogSnapshotHash.Compute(entries),
    };

    private async Task<MinecraftCatalogScanDto> IngestAsync(int scanId, MinecraftCatalogSnapshotDto snapshot)
    {
        await using var context = NewContext();
        return await Service(context).IngestAsync(scanId, snapshot);
    }

    [Fact]
    public async Task RequestScan_CreatesAPendingHeadlessWorldTask_InAWorkflowSessionOfTheRequester()
    {
        var scanId = await RequestAsync();

        await using var context = NewContext();
        var scan = await context.MinecraftCatalogScans.SingleAsync();
        var task = await context.WorldTasks.Include(t => t.WorkflowSession).SingleAsync();
        Assert.Equal(scanId, scan.Id);
        Assert.Equal(MinecraftCatalogScanStatus.Requested, scan.Status);
        Assert.Equal(task.Id, scan.WorldTaskId);
        Assert.Equal(WorldTaskTypes.MinecraftCatalogScan, task.TaskType);
        Assert.Equal("Pending", task.Status);
        Assert.Equal($"{{\"scanId\":{scanId}}}", task.InputJson);
        Assert.Equal(7, task.WorkflowSession.UserId);
        Assert.Equal(("MinecraftCatalogScan", scanId), (task.WorkflowSession.EntityTypeName, task.WorkflowSession.EntityId));
    }

    [Fact]
    public async Task RequestScan_RefusesASecondActiveScan_UntilTheFirstTimesOut()
    {
        var first = await RequestAsync();
        await Assert.ThrowsAsync<MinecraftCatalogConflictException>(RequestAsync);

        _now = _now.AddMinutes(16);
        var second = await RequestAsync();

        await using var context = NewContext();
        var stale = await context.MinecraftCatalogScans.SingleAsync(s => s.Id == first);
        Assert.Equal(MinecraftCatalogScanStatus.Failed, stale.Status);
        Assert.Contains("Timed out", stale.Error);
        Assert.Equal("Cancelled", (await context.WorldTasks.SingleAsync(t => t.Id == stale.WorldTaskId)).Status);
        Assert.NotEqual(first, second);
    }

    [Fact]
    public async Task Get_FailsTheScan_WhenThePluginFailedItsTask()
    {
        var scanId = await RequestAsync();
        await using (var context = NewContext())
        {
            var task = await context.WorldTasks.SingleAsync();
            task.Status = "Failed";
            task.ErrorMessage = "API answered 400";
            await context.SaveChangesAsync();
        }

        await using var read = NewContext();
        var scan = await Service(read).GetAsync(scanId);

        Assert.Equal("Failed", scan!.Status);
        Assert.Contains("API answered 400", scan.Error);
    }

    [Fact]
    public async Task Get_DoesNotOverwriteAScanThatCompletedMeanwhile()
    {
        // The plugin timed out and failed its task while the API was still applying the snapshot.
        var scanId = await RequestAsync();
        await using var reader = NewContext();
        await reader.MinecraftCatalogScans.SingleAsync(); // the reader holds the scan as Requested
        await using (var ingest = NewContext())
        {
            var task = await ingest.WorldTasks.SingleAsync();
            task.Status = "Failed";
            var scan = await ingest.MinecraftCatalogScans.SingleAsync();
            scan.Status = MinecraftCatalogScanStatus.Completed;
            await ingest.SaveChangesAsync();
        }

        var result = await Service(reader).GetAsync(scanId);

        Assert.Equal("Completed", result!.Status);
        await using var check = NewContext();
        Assert.Equal(MinecraftCatalogScanStatus.Completed, (await check.MinecraftCatalogScans.SingleAsync()).Status);
    }

    [Fact]
    public async Task Cancel_FailsTheScan_AndCancelsItsPendingTask()
    {
        var scanId = await RequestAsync();
        await using (var context = NewContext())
        {
            var scan = await Service(context).CancelAsync(scanId);
            Assert.Equal("Failed", scan.Status);
            await Assert.ThrowsAsync<MinecraftCatalogConflictException>(() => Service(context).CancelAsync(scanId));
        }

        await using var check = NewContext();
        Assert.Equal("Cancelled", (await check.WorldTasks.SingleAsync()).Status);
    }

    [Fact]
    public async Task Ingest_UpsertsByNormalisedKey_KeepsIds_AndCountsEveryOutcome()
    {
        await SeedAsync(
            new MinecraftMaterialRef { Id = 1, NamespaceKey = "minecraft:stone", Category = "ITEM", DisplayName = "Stone", IsItem = true, IsBlock = true,
                MaxStackSize = 64, DefaultBlockState = "minecraft:stone", CatalogStatus = MinecraftCatalogStatus.Active, LastSeenVersion = "1.21.10" },
            new MinecraftMaterialRef { Id = 2, NamespaceKey = "APPLE", Category = "ITEM" },
            new ItemBlueprint { Id = 30, Name = "Apple", IconMaterialRefId = 2 });
        var scanId = await RequestAsync();

        var result = await IngestAsync(scanId, Snapshot(
            Entry("minecraft:stone", isBlock: true, name: "Stone"),
            Entry("minecraft:apple", name: "Apple"),
            Entry("minecraft:wall_torch", isItem: false, isBlock: true)));

        Assert.Equal("Completed", result.Status);
        Assert.Equal((1, 1, 1), (result.Inserted, result.Updated, result.Unchanged));
        Assert.Equal(3, result.EntryCount);

        await using var check = NewContext();
        var apple = await check.MinecraftMaterialRefs.SingleAsync(m => m.Id == 2);
        Assert.Equal(("minecraft:apple", true, false, MinecraftCatalogStatus.Active), (apple.NamespaceKey, apple.IsItem, apple.IsBlock, apple.CatalogStatus));
        Assert.Equal(2, (await check.ItemBlueprints.SingleAsync()).IconMaterialRefId);
        var torch = await check.MinecraftMaterialRefs.SingleAsync(m => m.NamespaceKey == "minecraft:wall_torch");
        Assert.Equal(("Wall Torch", "BLOCK", "1.21.10", "1.21.10"), (torch.DisplayName, torch.Category, torch.FirstSeenVersion, torch.LastSeenVersion));
        Assert.Equal(scanId, torch.LastScanId);
    }

    [Fact]
    public async Task Ingest_MergesNotationDuplicates_DeletesUnreferencedUnknownRows_AndKeepsReferencedOnesAsMissing()
    {
        await SeedAsync(
            new MinecraftMaterialRef { Id = 1, NamespaceKey = "minecraft:oak_planks", Category = "ITEM" },
            new MinecraftMaterialRef { Id = 2, NamespaceKey = "oak_planks", Category = "ITEM" },
            new MinecraftMaterialRef { Id = 3, NamespaceKey = "minecraft:removed_thing", Category = "ITEM" },
            new MinecraftMaterialRef { Id = 4, NamespaceKey = "minecraft:old_relic", Category = "ITEM" },
            new MinecraftMaterialRef { Id = 5, NamespaceKey = "not a key", Category = "ITEM" },
            new ItemBlueprint { Id = 30, Name = "Plank", IconMaterialRefId = 2 },
            new ItemBlueprint { Id = 31, Name = "Relic", IconMaterialRefId = 4 },
            new MinecraftBlockRef { Id = 10, NamespaceKey = "oak_planks", BlockStateString = "oak_planks[axis=y]", MaterialRefId = 2 });
        var scanId = await RequestAsync();

        var result = await IngestAsync(scanId, Snapshot(Entry("minecraft:oak_planks", isBlock: true)));

        Assert.Equal((1, 2, 1), (result.Merged, result.Deleted, result.MarkedMissing));
        await using var check = NewContext();
        var ids = await check.MinecraftMaterialRefs.OrderBy(m => m.Id).Select(m => m.Id).ToListAsync();
        Assert.Equal(new[] { 1, 4 }, ids);
        Assert.Equal(1, (await check.ItemBlueprints.SingleAsync(b => b.Id == 30)).IconMaterialRefId);
        Assert.Equal(MinecraftCatalogStatus.Missing, (await check.MinecraftMaterialRefs.SingleAsync(m => m.Id == 4)).CatalogStatus);
        var block = await check.MinecraftBlockRefs.SingleAsync();
        Assert.Equal((1, "minecraft:oak_planks[axis=y]"), (block.MaterialRefId, block.BlockStateString));
    }

    [Fact]
    public async Task Ingest_SetsResolvedAndMissingIcons_KeepsTheOldIconWhenUnavailable_AndNeverTouchesAnOverride()
    {
        await SeedAsync(
            new MinecraftMaterialRef { Id = 1, NamespaceKey = "minecraft:stone", Category = "ITEM", IconUrl = "https://old/stone.png" },
            new MinecraftMaterialRef { Id = 2, NamespaceKey = "minecraft:apple", Category = "ITEM", IconUrl = "https://old/apple.png" },
            new MinecraftMaterialRef { Id = 3, NamespaceKey = "minecraft:dirt", Category = "ITEM", IconUrl = "https://old/dirt.png", IconUrlOverride = "https://mine/dirt.png" },
            new MinecraftMaterialRef { Id = 4, NamespaceKey = "minecraft:sand", Category = "ITEM", IconUrl = "https://old/sand.png" });
        _icons.Results["minecraft:stone"] = new(MinecraftIconOutcome.Resolved, "https://new/stone.png", "prismarine@1.21.10");
        _icons.Results["minecraft:apple"] = new(MinecraftIconOutcome.Missing, null, "inventivetalent@1.21.10");
        _icons.Results["minecraft:dirt"] = new(MinecraftIconOutcome.Missing, null, null);
        _icons.Results["minecraft:sand"] = new(MinecraftIconOutcome.Unavailable, null, null);
        var scanId = await RequestAsync();

        var result = await IngestAsync(scanId, Snapshot(Entry("minecraft:stone"), Entry("minecraft:apple"), Entry("minecraft:dirt"), Entry("minecraft:sand")));

        Assert.Equal(1, result.IconsMissing); // apple; dirt has an override
        await using var check = NewContext();
        var rows = await check.MinecraftMaterialRefs.OrderBy(m => m.Id).ToListAsync();
        Assert.Equal(("https://new/stone.png", MinecraftIconStatus.Resolved, "prismarine@1.21.10"), (rows[0].IconUrl, rows[0].IconStatus, rows[0].IconSource));
        Assert.Equal((null, MinecraftIconStatus.Missing), (rows[1].IconUrl, rows[1].IconStatus));
        Assert.Equal("https://mine/dirt.png", rows[2].IconUrlOverride);
        Assert.Equal(("https://old/sand.png", (MinecraftIconStatus?)null), (rows[3].IconUrl, rows[3].IconStatus));
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("invalid")]
    [InlineData("hash")]
    public async Task Ingest_RefusesAnInvalidSnapshot_AndWritesNothing(string defect)
    {
        await SeedAsync(new MinecraftMaterialRef { Id = 1, NamespaceKey = "minecraft:old", Category = "ITEM" });
        var scanId = await RequestAsync();
        var snapshot = defect switch
        {
            "schema" => Snapshot(Entry("minecraft:stone")),
            "empty" => Snapshot(),
            "duplicate" => Snapshot(Entry("minecraft:stone"), Entry("STONE")),
            "invalid" => Snapshot(Entry("minecraft:sto ne")),
            _ => Snapshot(Entry("minecraft:stone")),
        };
        if (defect == "schema") snapshot.SchemaVersion = 3;
        if (defect == "hash") snapshot.ContentHash = new string('0', 64);

        await Assert.ThrowsAsync<ArgumentException>(() => IngestAsync(scanId, snapshot));

        await using var check = NewContext();
        Assert.Equal("minecraft:old", (await check.MinecraftMaterialRefs.SingleAsync()).NamespaceKey);
        Assert.Equal(MinecraftCatalogScanStatus.Requested, (await check.MinecraftCatalogScans.SingleAsync()).Status);
        Assert.Equal(0, _icons.Calls);
    }

    [Fact]
    public async Task Ingest_SameSnapshotAgain_ReturnsTheStoredResult_AnotherOneIsAConflict()
    {
        var scanId = await RequestAsync();
        var snapshot = Snapshot(Entry("minecraft:stone"));
        var first = await IngestAsync(scanId, snapshot);

        var retry = await IngestAsync(scanId, Snapshot(Entry("minecraft:stone")));

        Assert.Equal((first.Id, first.Inserted, "Completed"), (retry.Id, retry.Inserted, retry.Status));
        Assert.Equal(1, _icons.Calls);
        await Assert.ThrowsAsync<MinecraftCatalogConflictException>(() => IngestAsync(scanId, Snapshot(Entry("minecraft:dirt"))));
    }

    [Fact]
    public async Task Ingest_UnknownOrCancelledScan_IsRefused()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => IngestAsync(99, Snapshot(Entry("minecraft:stone"))));

        var scanId = await RequestAsync();
        await using (var context = NewContext()) await Service(context).CancelAsync(scanId);
        await Assert.ThrowsAsync<MinecraftCatalogConflictException>(() => IngestAsync(scanId, Snapshot(Entry("minecraft:stone"))));
    }

    [Fact]
    public async Task SetIconOverride_AcceptsHttpUrls_AndClearsWithNull()
    {
        await SeedAsync(new MinecraftMaterialRef { Id = 1, NamespaceKey = "minecraft:stone", Category = "ITEM" });
        await using var context = NewContext();
        var service = Service(context);

        await service.SetIconOverrideAsync(1, " https://example.org/stone.png ");
        Assert.Equal("https://example.org/stone.png", (await context.MinecraftMaterialRefs.SingleAsync()).IconUrlOverride);
        await Assert.ThrowsAsync<ArgumentException>(() => service.SetIconOverrideAsync(1, "javascript:alert(1)"));
        await service.SetIconOverrideAsync(1, null);
        Assert.Null((await context.MinecraftMaterialRefs.SingleAsync()).IconUrlOverride);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.SetIconOverrideAsync(99, null));
    }
}
