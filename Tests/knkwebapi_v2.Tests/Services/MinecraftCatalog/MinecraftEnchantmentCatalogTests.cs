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
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace knkwebapi_v2.Tests.Services.MinecraftCatalog;

/// <summary>
/// KNG-250 (docs/specs/minecraft-catalog/IMPLEMENTATION_PLAN.md §12): enchantments in the server scan, vanilla
/// and custom; key resolution of old rows, merges, Missing, definitions, the database picker and the audit.
/// </summary>
public class MinecraftEnchantmentCatalogTests
{
    private const string BookUrl = "https://assets/items/enchanted_book.png";

    private readonly string _dbName = Guid.NewGuid().ToString();

    private sealed class BookIcon : IMinecraftIconResolver
    {
        public Task<IReadOnlyDictionary<string, MinecraftIconResult>> ResolveAsync(
            string? minecraftVersion, IReadOnlyCollection<MinecraftIconRequest> entries, CancellationToken ct) =>
            Task.FromResult<IReadOnlyDictionary<string, MinecraftIconResult>>(entries
                .Where(e => e.Key == MinecraftCatalogScanService.EnchantedBookKey)
                .ToDictionary(e => e.Key, _ => new MinecraftIconResult(MinecraftIconOutcome.Resolved, BookUrl, "prismarine@26.1")));
    }

    private readonly IMapper _mapper = new MapperConfiguration(cfg =>
    {
        cfg.AddProfile<PagedQueryMappingProfile>();
        cfg.AddProfile<MinecraftEnchantmentRefProfile>();
    }).CreateMapper();

    private KnKDbContext NewContext() =>
        new(new DbContextOptionsBuilder<KnKDbContext>().UseInMemoryDatabase(_dbName).Options);

    private static MinecraftCatalogScanService Scans(KnKDbContext context) =>
        new(context, new BookIcon(), NullLogger<MinecraftCatalogScanService>.Instance);

    private async Task SeedAsync(params object[] rows)
    {
        await using var context = NewContext();
        context.AddRange(rows);
        await context.SaveChangesAsync();
    }

    private static readonly MinecraftCatalogEntryDto[] Materials =
    {
        new() { Key = "minecraft:enchanted_book", IsItem = true, MaxStackSize = 1, DisplayName = "Enchanted Book" },
    };

    private static MinecraftCatalogEnchantmentDto Vanilla(string id, int maxLevel, string? name = null) => new()
    {
        Key = "minecraft:" + id, MaxLevel = maxLevel, TranslationKey = "enchantment.minecraft." + id, DisplayName = name,
    };

    private static MinecraftCatalogEnchantmentDto Custom(string id, int maxLevel, string name, string type = "ATTACK") => new()
    {
        Key = "knk:" + id, IsCustom = true, MaxLevel = maxLevel, DisplayName = name, Category = type,
    };

    private static MinecraftCatalogSnapshotDto Snapshot(params MinecraftCatalogEnchantmentDto[] enchantments) => new()
    {
        SchemaVersion = 2,
        MinecraftVersion = "26.3",
        ServerVersion = "Paper test",
        Entries = Materials.ToList(),
        Enchantments = enchantments.ToList(),
        ContentHash = MinecraftCatalogSnapshotHash.Compute(Materials, enchantments),
    };

    private async Task<MinecraftCatalogScanDto> ScanAsync(MinecraftCatalogSnapshotDto snapshot)
    {
        int scanId;
        await using (var context = NewContext())
        {
            scanId = (await Scans(context).RequestScanAsync(1)).Id;
        }
        await using (var context = NewContext())
        {
            return await Scans(context).IngestAsync(scanId, snapshot);
        }
    }

    private static readonly MinecraftCatalogEnchantmentDto[] Server =
    {
        Vanilla("sharpness", 5, "Sharpness"),
        Vanilla("unbreaking", 3, "Unbreaking"),
        Custom("chaos", 1, "Chaos", "SUPPORT"),
        Custom("flash_chaos", 1, "Flash Chaos", "SUPPORT"),
    };

    [Fact]
    public async Task FirstScan_InsertsVanillaAndCustom_AndGivesEachADefinition()
    {
        var result = await ScanAsync(Snapshot(Server));

        Assert.Equal((4, 4, 0, 0), (result.EnchantmentCount!.Value, result.EnchantmentsInserted, result.EnchantmentsUpdated, result.EnchantmentsDeleted));
        Assert.Equal((4, 0), (result.EnchantmentDefinitionsCreated, result.EnchantmentDefinitionsLinked));
        await using var db = NewContext();
        var refs = await db.MinecraftEnchantmentRefs.ToDictionaryAsync(r => r.NamespaceKey);
        Assert.Equal(new[] { "knk:chaos", "knk:flash_chaos", "minecraft:sharpness", "minecraft:unbreaking" }, refs.Keys.Order());
        var flash = refs["knk:flash_chaos"];
        Assert.Equal((true, "Flash Chaos", 1, "SUPPORT", MinecraftCatalogStatus.Active, "26.3", BookUrl),
            (flash.IsCustom, flash.DisplayName, flash.MaxLevel, flash.Category, flash.CatalogStatus, flash.FirstSeenVersion, flash.IconUrl));
        Assert.False(refs["minecraft:sharpness"].IsCustom);

        var definitions = await db.EnchantmentDefinitions.ToDictionaryAsync(d => d.Key);
        // Custom definitions are keyed by the ability key, vanilla ones by the namespaced key.
        Assert.Equal(new[] { "chaos", "flash_chaos", "minecraft:sharpness", "minecraft:unbreaking" }, definitions.Keys.Order());
        Assert.Equal((true, refs["knk:chaos"].Id), (definitions["chaos"].IsCustom, definitions["chaos"].MinecraftEnchantmentRefId!.Value));
        Assert.Equal((false, 5, "Sharpness"), (definitions["minecraft:sharpness"].IsCustom, definitions["minecraft:sharpness"].MaxLevel, definitions["minecraft:sharpness"].DisplayName));
    }

    [Fact]
    public async Task SecondScan_ChangesNothing()
    {
        await ScanAsync(Snapshot(Server));
        var again = await ScanAsync(Snapshot(Server));

        Assert.Equal((0, 0, 4), (again.EnchantmentsInserted, again.EnchantmentsUpdated, again.EnchantmentsUnchanged));
        Assert.Equal((0, 0), (again.EnchantmentDefinitionsCreated, again.EnchantmentDefinitionsLinked));
        await using var db = NewContext();
        Assert.Equal(4, await db.EnchantmentDefinitions.CountAsync());
    }

    [Fact]
    public async Task OldCustomRows_MergeIntoTheScannedKnkKey_AndExistingDefinitionsAreLinkedNotDuplicated()
    {
        // Pre-KNG-250 rows: a bare custom key saved as non-custom (old CreateAsync), its knk: twin, and a vanilla
        // row in another notation. The seeded custom definitions have no ref.
        await SeedAsync(
            new MinecraftEnchantmentRef { Id = 1, NamespaceKey = "chaos", IsCustom = false },
            new MinecraftEnchantmentRef { Id = 2, NamespaceKey = "knk:chaos", IsCustom = true },
            new MinecraftEnchantmentRef { Id = 3, NamespaceKey = "SHARPNESS", Category = "Weapon", MaxLevel = 5 },
            new MinecraftEnchantmentRef { Id = 4, NamespaceKey = "flashchaos", IsCustom = true },
            new EnchantmentDefinition { Id = 10, Key = "chaos", DisplayName = "Chaos", IsCustom = true, MaxLevel = 1 },
            new EnchantmentDefinition { Id = 11, Key = "flash_chaos", DisplayName = "Flash Chaos", IsCustom = true, MaxLevel = 1 },
            new EnchantmentDefinition { Id = 12, Key = "minecraft:sharpness", DisplayName = "Sharp", MaxLevel = 5, MinecraftEnchantmentRefId = 3 },
            new EnchantmentDefinition { Id = 13, Key = "custom-chaos-variant", DisplayName = "Chaos II", IsCustom = true, MaxLevel = 2, MinecraftEnchantmentRefId = 1 });

        var result = await ScanAsync(Snapshot(Server));

        Assert.Equal((1, 0, 0), (result.EnchantmentsMerged, result.EnchantmentsDeleted, result.EnchantmentsMarkedMissing));
        Assert.Equal((1, 2), (result.EnchantmentDefinitionsCreated, result.EnchantmentDefinitionsLinked)); // unbreaking created; chaos, flash_chaos linked
        await using var db = NewContext();
        var refs = await db.MinecraftEnchantmentRefs.ToDictionaryAsync(r => r.Id);
        Assert.Equal(new[] { 2, 3, 4 }, refs.Keys.Where(id => id <= 4).Order());
        Assert.Equal(("knk:chaos", true), (refs[2].NamespaceKey, refs[2].IsCustom));
        Assert.Equal(("minecraft:sharpness", "Weapon"), (refs[3].NamespaceKey, refs[3].Category)); // legacy category kept
        Assert.Equal("knk:flash_chaos", refs[4].NamespaceKey);

        var definitions = await db.EnchantmentDefinitions.ToDictionaryAsync(d => d.Id);
        Assert.Equal(2, definitions[13].MinecraftEnchantmentRefId); // repointed from the merged row
        Assert.Equal(2, definitions[10].MinecraftEnchantmentRefId);
        Assert.Equal(4, definitions[11].MinecraftEnchantmentRefId);
        Assert.Equal(("Sharp", 3), (definitions[12].DisplayName, definitions[12].MinecraftEnchantmentRefId!.Value)); // only linked, never renamed
        Assert.Equal(5, definitions.Count);
    }

    [Fact]
    public async Task UnseenRefs_AreDeleted_UnlessADefinitionUsesThem_ThenMissing_AndDefinitionsStay()
    {
        await SeedAsync(
            new MinecraftEnchantmentRef { Id = 1, NamespaceKey = "minecraft:sweeping", MaxLevel = 3 },
            new MinecraftEnchantmentRef { Id = 2, NamespaceKey = "minecraft:old_unused" },
            new MinecraftEnchantmentRef { Id = 3, NamespaceKey = "knk:lifesteal", IsCustom = true },
            new EnchantmentDefinition { Id = 10, Key = "minecraft:sweeping", DisplayName = "Sweeping", MaxLevel = 3, MinecraftEnchantmentRefId = 1 });

        var result = await ScanAsync(Snapshot(Server));

        Assert.Equal((2, 1), (result.EnchantmentsDeleted, result.EnchantmentsMarkedMissing));
        await using var db = NewContext();
        Assert.Equal(MinecraftCatalogStatus.Missing, (await db.MinecraftEnchantmentRefs.SingleAsync(r => r.Id == 1)).CatalogStatus);
        Assert.False(await db.MinecraftEnchantmentRefs.AnyAsync(r => r.Id == 2 || r.Id == 3));
        Assert.True(await db.EnchantmentDefinitions.AnyAsync(d => d.Id == 10));

        var audit = await new MinecraftCatalogMaintenanceService(db, NullLogger<MinecraftCatalogMaintenanceService>.Instance).AuditAsync();
        var missing = Assert.Single(audit.MissingEnchantmentsReferenced);
        Assert.Equal(("minecraft:sweeping", 10), (missing.NamespaceKey, Assert.Single(missing.Definitions).Id));
        Assert.Equal(5, audit.EnchantmentCount);
    }

    [Fact]
    public async Task VanillaDefinitionStillAtItsRefsLevel_FollowsTheServer_OneSetByStaffKeepsItsLevel()
    {
        // As the seeds leave them: ref and definition at the level the lootbox seed needs.
        await SeedAsync(
            new MinecraftEnchantmentRef { Id = 1, NamespaceKey = "minecraft:sharpness", MaxLevel = 1 },
            new MinecraftEnchantmentRef { Id = 2, NamespaceKey = "minecraft:unbreaking", MaxLevel = 3 },
            new EnchantmentDefinition { Id = 10, Key = "minecraft:sharpness", DisplayName = "Sharpness", MaxLevel = 1, MinecraftEnchantmentRefId = 1 },
            new EnchantmentDefinition { Id = 11, Key = "minecraft:unbreaking", DisplayName = "Unbreaking", MaxLevel = 2, MinecraftEnchantmentRefId = 2 });

        await ScanAsync(Snapshot(Server));

        await using var db = NewContext();
        var levels = await db.EnchantmentDefinitions.ToDictionaryAsync(d => d.Id, d => d.MaxLevel);
        Assert.Equal(5, levels[10]);
        Assert.Equal(2, levels[11]);
    }

    [Fact]
    public async Task SchemaOne_LeavesEnchantmentsAlone()
    {
        await SeedAsync(new MinecraftEnchantmentRef { Id = 1, NamespaceKey = "minecraft:old_unused" });

        var result = await ScanAsync(new MinecraftCatalogSnapshotDto
        {
            SchemaVersion = 1, MinecraftVersion = "26.3", Entries = Materials.ToList(),
            ContentHash = MinecraftCatalogSnapshotHash.Compute(Materials),
        });

        Assert.Equal("Completed", result.Status);
        Assert.Null(result.EnchantmentCount);
        await using var db = NewContext();
        Assert.Equal(MinecraftCatalogStatus.Unverified, (await db.MinecraftEnchantmentRefs.SingleAsync()).CatalogStatus);
    }

    public static IEnumerable<object[]> InvalidEnchantmentLists()
    {
        yield return new object[] { "no list", (Action<MinecraftCatalogSnapshotDto>)(s => s.Enchantments = null) };
        yield return new object[] { "custom in minecraft namespace", (Action<MinecraftCatalogSnapshotDto>)(s => s.Enchantments!.Add(new MinecraftCatalogEnchantmentDto { Key = "minecraft:chaos2", IsCustom = true })) };
        yield return new object[] { "duplicate", (Action<MinecraftCatalogSnapshotDto>)(s => s.Enchantments!.Add(Vanilla("sharpness", 5))) };
        yield return new object[] { "invalid key", (Action<MinecraftCatalogSnapshotDto>)(s => s.Enchantments!.Add(new MinecraftCatalogEnchantmentDto { Key = "bad key" })) };
    }

    [Theory]
    [MemberData(nameof(InvalidEnchantmentLists))]
    public async Task InvalidEnchantments_AreRefused_BeforeAnyWrite(string _, Action<MinecraftCatalogSnapshotDto> spoil)
    {
        var snapshot = Snapshot(Server);
        spoil(snapshot);
        snapshot.ContentHash = MinecraftCatalogSnapshotHash.Compute(Materials, snapshot.Enchantments ?? new());

        await Assert.ThrowsAsync<ArgumentException>(() => ScanAsync(snapshot));
        await using var db = NewContext();
        Assert.False(await db.MinecraftEnchantmentRefs.AnyAsync());
        Assert.False(await db.MinecraftMaterialRefs.AnyAsync());
    }

    [Theory]
    [InlineData("minecraft:sharpness", false, "minecraft:sharpness")]
    [InlineData("SHARPNESS", false, "minecraft:sharpness")]
    [InlineData("chaos", false, "knk:chaos")] // no vanilla chaos: the custom one
    [InlineData("KNK:Flash_Chaos", false, "knk:flash_chaos")]
    [InlineData("flashchaos", true, "knk:flash_chaos")]
    [InlineData("minecraft:chaos", true, "minecraft:chaos")] // an explicit minecraft key stays vanilla
    [InlineData("lifesteal", true, "knk:lifesteal")]
    [InlineData("lifesteal", false, "minecraft:lifesteal")]
    [InlineData("mypack:frost", false, "mypack:frost")]
    [InlineData("bad key", false, null)]
    public void Resolve_FindsTheScannedKeyOfAnOldRow(string stored, bool isCustom, string? expected)
    {
        var scanned = new ScannedEnchantmentKeys(Server.Select(e => (e.Key, e.IsCustom)));
        Assert.Equal(expected, MinecraftEnchantmentKey.Resolve(stored, isCustom, scanned));
    }

    [Fact]
    public void Resolve_PrefersVanilla_ForABareNonCustomKeyBothKnow()
    {
        var scanned = new ScannedEnchantmentKeys(new[] { ("minecraft:poison", false), ("knk:poison", true) });
        Assert.Equal("minecraft:poison", MinecraftEnchantmentKey.Resolve("poison", false, scanned));
        Assert.Equal("knk:poison", MinecraftEnchantmentKey.Resolve("poison", true, scanned));
    }

    private static PagedQueryDto PickerQuery(string? origin = null, string? search = null)
    {
        var filters = new Dictionary<string, string> { [MinecraftPickerFilters.Picker] = "true" };
        if (origin != null) filters[MinecraftPickerFilters.Origin] = origin;
        return new PagedQueryDto { PageNumber = 1, PageSize = 20, SearchTerm = search, Filters = filters };
    }

    [Theory]
    [InlineData(null, new[] { "Chaos", "Sharpness" })]
    [InlineData("custom", new[] { "Chaos" })]
    [InlineData("vanilla", new[] { "Sharpness" })]
    public async Task PickerSearch_ShowsVanillaAndCustom_HidesMissing_SortedByName(string? origin, string[] expected)
    {
        await SeedAsync(
            new MinecraftEnchantmentRef { NamespaceKey = "minecraft:sharpness", DisplayName = "Sharpness", CatalogStatus = MinecraftCatalogStatus.Active },
            new MinecraftEnchantmentRef { NamespaceKey = "knk:chaos", DisplayName = "Chaos", IsCustom = true, CatalogStatus = MinecraftCatalogStatus.Active },
            new MinecraftEnchantmentRef { NamespaceKey = "minecraft:sweeping", DisplayName = "Sweeping", CatalogStatus = MinecraftCatalogStatus.Missing });

        await using var db = NewContext();
        var service = new MinecraftEnchantmentRefService(new MinecraftEnchantmentRefRepository(db), _mapper, NullLogger<MinecraftEnchantmentRefService>.Instance);
        var result = await service.SearchPickerAsync(PickerQuery(origin));

        Assert.Equal(expected, result.Items.Select(i => i.DisplayName));
        Assert.Equal(expected.Length, result.TotalCount);

        var admin = await service.SearchAsync(new PagedQueryDto { PageNumber = 1, PageSize = 20 });
        Assert.Equal(3, admin.TotalCount); // the admin table shows Missing rows too
        Assert.Contains(admin.Items, i => i.CatalogStatus == "Missing" && !i.IsCustom);
    }

    [Fact]
    public async Task GetOrCreate_MakesAnUnverifiedRefFromTheNormalisedKey()
    {
        await using var db = NewContext();
        var service = new MinecraftEnchantmentRefService(new MinecraftEnchantmentRefRepository(db), _mapper, NullLogger<MinecraftEnchantmentRefService>.Instance);

        var vanilla = await service.GetOrCreateAsync("MENDING");
        var custom = await service.GetOrCreateAsync("knk:lifesteal");
        var again = await service.GetOrCreateAsync("minecraft:mending");

        Assert.Equal(("minecraft:mending", false, "Unverified"), (vanilla.NamespaceKey, vanilla.IsCustom, vanilla.CatalogStatus));
        Assert.Equal(("knk:lifesteal", true), (custom.NamespaceKey, custom.IsCustom));
        Assert.Equal(vanilla.Id, again.Id);
    }
}
