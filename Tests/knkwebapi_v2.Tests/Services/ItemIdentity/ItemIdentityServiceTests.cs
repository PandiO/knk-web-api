using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.ItemIdentity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static knkwebapi_v2.Tests.Services.ItemIdentity.ItemIdentityTestDb;

namespace knkwebapi_v2.Tests.Services.ItemIdentity;

/// <summary>Item identity (#141): the plugin's map, moving a match, the conversion counts and the admin report.</summary>
public class ItemIdentityServiceTests
{
    private readonly ItemIdentityTestDb _db = new();
    private readonly DateTime _now = new(2026, 11, 19, 15, 30, 0, DateTimeKind.Utc);

    private ItemIdentityService Service(knkwebapi_v2.Properties.KnKDbContext context) =>
        new(context, NullLogger<ItemIdentityService>.Instance, () => _now);

    private async Task SeedCatalogAsync() => await _db.SeedAsync(
        new Category { Id = 9, Name = VanillaShadowBlueprints.UncuratedCategoryName },
        new Category { Id = 3, Name = "Food" },
        Item(1, "minecraft:oak_log"), Item(2, "BREAD"), Item(3, "minecraft:stone"), Item(4, "minecraft:wall_torch", isItem: false),
        new ItemBlueprint { Id = 10, Name = "Oak Log", CategoryId = 9, VanillaMaterialMatchRefId = 1 },
        new ItemBlueprint { Id = 11, Name = "Bread", CategoryId = 9, VanillaMaterialMatchRefId = 2 },
        new ItemBlueprint { Id = 20, Name = "Phoenix Bread", CategoryId = 3, DefaultDisplayName = "&6Phoenix Bread" });

    [Fact]
    public async Task MaterialMap_ListsEveryMatch_WithNormalisedKeys_AndTheShadowFlag()
    {
        await SeedCatalogAsync();
        await using var context = _db.NewContext();

        var map = await Service(context).GetMaterialMapAsync();

        Assert.Equal(_now, map.GeneratedAt);
        Assert.Equal(new[] { ("minecraft:bread", 11, true), ("minecraft:oak_log", 10, true) },
            map.Entries.Select(e => (e.MaterialKey, e.BlueprintId, e.Shadow)));
    }

    [Fact]
    public async Task SetMatch_MovesTheMatchFromTheShadowToACuratedBlueprint()
    {
        await SeedCatalogAsync();
        await using (var context = _db.NewContext())
        {
            var result = await Service(context).SetMatchAsync(2, 20);
            Assert.Equal((11, 20, "BREAD", (int?)null), (result.PreviousBlueprintId, result.BlueprintId, result.MaterialKey, result.ReleasedMaterialRefId));
        }

        await using var check = _db.NewContext();
        Assert.Null((await check.ItemBlueprints.SingleAsync(b => b.Id == 11)).VanillaMaterialMatchRefId);
        Assert.Equal(2, (await check.ItemBlueprints.SingleAsync(b => b.Id == 20)).VanillaMaterialMatchRefId);
        Assert.Equal(1, await check.ItemBlueprints.CountAsync(b => b.VanillaMaterialMatchRefId == 2));
        var map = await Service(check).GetMaterialMapAsync();
        Assert.Contains(map.Entries, e => e.MaterialKey == "minecraft:bread" && e.BlueprintId == 20 && !e.Shadow);
    }

    [Fact]
    public async Task SetMatch_ABlueprintHoldsOneMatch_TheOldOneIsReleased()
    {
        await SeedCatalogAsync();
        await using (var context = _db.NewContext())
        {
            var result = await Service(context).SetMatchAsync(3, 10);
            Assert.Equal(((int?)null, 10, (int?)1), (result.PreviousBlueprintId, result.BlueprintId, result.ReleasedMaterialRefId));
        }

        await using var check = _db.NewContext();
        Assert.Equal(3, (await check.ItemBlueprints.SingleAsync(b => b.Id == 10)).VanillaMaterialMatchRefId);
        Assert.False(await check.ItemBlueprints.AnyAsync(b => b.VanillaMaterialMatchRefId == 1));
    }

    [Fact]
    public async Task SetMatch_Null_ClearsTheMatch()
    {
        await SeedCatalogAsync();
        await using (var context = _db.NewContext())
        {
            await Service(context).SetMatchAsync(1, null);
        }

        await using var check = _db.NewContext();
        Assert.Null((await check.ItemBlueprints.SingleAsync(b => b.Id == 10)).VanillaMaterialMatchRefId);
    }

    [Fact]
    public async Task SetMatch_UnknownMaterialOrBlueprint_IsNotFound()
    {
        await SeedCatalogAsync();
        await using var context = _db.NewContext();

        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service(context).SetMatchAsync(99, 20));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service(context).SetMatchAsync(1, 99));
    }

    [Fact]
    public async Task RecordCounts_AddsToTodaysRows_PerSourceOutcomeAndMaterial()
    {
        await using (var context = _db.NewContext())
        {
            Assert.Equal(2, await Service(context).RecordConversionCountsAsync(new ItemConversionCountsDto
            {
                Counts =
                {
                    new ItemConversionCountDto { Source = "pickup", Outcome = "vanilla_match", MaterialKey = "OAK_LOG", Count = 5 },
                    new ItemConversionCountDto { Source = "pickup", Outcome = "vanilla_match", MaterialKey = "minecraft:oak_log", Count = 2 },
                    new ItemConversionCountDto { Source = "craft", Outcome = "unresolved_custom_name", MaterialKey = "minecraft:bread", Count = 1 },
                    new ItemConversionCountDto { Source = "craft", Outcome = "vanilla_match", MaterialKey = "minecraft:stone", Count = 0 },
                },
            }));
        }
        await using (var context = _db.NewContext())
        {
            await Service(context).RecordConversionCountsAsync(new ItemConversionCountsDto
            {
                Counts = { new ItemConversionCountDto { Source = "pickup", Outcome = "vanilla_match", MaterialKey = "minecraft:oak_log", Count = 3 } },
            });
        }

        await using var check = _db.NewContext();
        var rows = await check.ItemConversionStats.OrderBy(s => s.Source).ToListAsync();
        Assert.Equal(new[] { ("craft", "unresolved_custom_name", "minecraft:bread", 1L), ("pickup", "vanilla_match", "minecraft:oak_log", 10L) },
            rows.Select(r => (r.Source, r.Outcome, r.MaterialKey, r.Count)));
        Assert.All(rows, r => Assert.Equal(_now.Date, r.Day));
    }

    [Theory]
    [InlineData("Pickup", "vanilla_match", "minecraft:stone", 1)]
    [InlineData("pickup", "vanilla match", "minecraft:stone", 1)]
    [InlineData("pickup", "vanilla_match", "not a key!", 1)]
    [InlineData("pickup", "vanilla_match", "minecraft:stone", -1)]
    public async Task RecordCounts_RefusesBadLines(string source, string outcome, string key, long count)
    {
        await using var context = _db.NewContext();
        await Assert.ThrowsAsync<ArgumentException>(() => Service(context).RecordConversionCountsAsync(new ItemConversionCountsDto
        {
            Counts = { new ItemConversionCountDto { Source = source, Outcome = outcome, MaterialKey = key, Count = count } },
        }));
        Assert.Empty(await context.ItemConversionStats.ToListAsync());
    }

    [Fact]
    public async Task RecordCounts_RefusesADayInTheFuture()
    {
        await using var context = _db.NewContext();
        await Assert.ThrowsAsync<ArgumentException>(() => Service(context).RecordConversionCountsAsync(new ItemConversionCountsDto
        {
            Day = _now.AddDays(1),
            Counts = { new ItemConversionCountDto { Source = "pickup", Outcome = "vanilla_match", MaterialKey = "minecraft:stone", Count = 1 } },
        }));
    }

    [Fact]
    public async Task Report_CountsCoverage_ListsUnmatchedMaterials_AndSumsTheDaysAsked()
    {
        await SeedCatalogAsync();
        await _db.SeedAsync(
            new ItemConversionStat { Day = _now.Date, Source = "pickup", Outcome = "vanilla_match", MaterialKey = "minecraft:oak_log", Count = 7 },
            new ItemConversionStat { Day = _now.Date.AddDays(-1), Source = "pickup", Outcome = "vanilla_match", MaterialKey = "minecraft:bread", Count = 3 },
            new ItemConversionStat { Day = _now.Date, Source = "craft", Outcome = "unresolved_custom_name", MaterialKey = "minecraft:bread", Count = 4 },
            new ItemConversionStat { Day = _now.Date.AddDays(-1), Source = "pickup", Outcome = "unresolved_custom_name", MaterialKey = "minecraft:bread", Count = 1 },
            new ItemConversionStat { Day = _now.Date.AddDays(-10), Source = "pickup", Outcome = "vanilla_match", MaterialKey = "minecraft:stone", Count = 100 });
        await using var context = _db.NewContext();

        var report = await Service(context).GetReportAsync(7);

        Assert.Equal((3, 2, 2, 0, 9), (report.ObtainableMaterials, report.MatchedMaterials, report.ShadowMatches, report.CuratedMatches, report.UncuratedCategoryId));
        Assert.Equal("minecraft:stone", Assert.Single(report.UnmatchedMaterials).NamespaceKey);
        Assert.Equal(new[] { ("craft", "unresolved_custom_name", 4L), ("pickup", "unresolved_custom_name", 1L), ("pickup", "vanilla_match", 10L) },
            report.Conversions.Select(c => (c.Source, c.Outcome, c.Count)));
        var top = Assert.Single(report.TopUnresolved);
        Assert.Equal(("minecraft:bread", 5L), (top.MaterialKey, top.Count));
    }
}
