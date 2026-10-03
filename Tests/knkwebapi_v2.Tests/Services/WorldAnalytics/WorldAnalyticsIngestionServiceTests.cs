using System.Reflection;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services.WorldAnalytics;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace knkwebapi_v2.Tests.Services.WorldAnalytics;

/// <summary>
/// WorldAnalyticsIngestionService (IMPLEMENTATION_PLAN.md §3.4; link 7 acceptance criteria 1-2):
/// batch-id dedupe, local-day mapping, per-row validation, aggregation of repeated keys, region →
/// domain resolution, unique players as a maximum, and no user identity in any shape.
/// </summary>
public class WorldAnalyticsIngestionServiceTests : IDisposable
{
    private readonly WorldAnalyticsTestDb _db = new();

    public void Dispose() => _db.Dispose();

    [Fact]
    public async Task Batch_StoresCellsStepsAndDomains_OnTheWindowsLocalDay()
    {
        var batch = WorldAnalyticsTestDb.Batch();
        batch.MovementCells!.Add(WorldAnalyticsTestDb.Cell(1, -2, 3));
        batch.MenuSteps!.Add(WorldAnalyticsTestDb.Step("profile.main", "opened", 4));
        batch.MenuSteps.Add(WorldAnalyticsTestDb.Step("profile.main", "action:menu.open", 2, "succeeded"));
        batch.DomainInteractions!.Add(WorldAnalyticsTestDb.Domain("enter", 5, 2, domainId: 7));

        var result = await _db.Ingestion().IngestAsync(batch);

        Assert.False(result.Duplicate);
        Assert.Equal(4, result.Accepted);
        Assert.Empty(result.Rejected);
        Assert.Equal(WorldAnalyticsTestDb.Today, result.Day);
        var cell = Assert.Single(_db.NewContext().WorldMovementCellDailies);
        Assert.Equal((WorldAnalyticsTestDb.Today, "world", (short)16, 1, -2, 3), (cell.Day, cell.World, cell.CellSize, cell.CellX, cell.CellZ, cell.Samples));
        var steps = _db.NewContext().MenuFunnelDailies.OrderBy(s => s.Step).ToList();
        Assert.Equal(new[] { ("action:menu.open", TelemetryOutcome.Succeeded, 2), ("opened", TelemetryOutcome.Info, 4) },
            steps.Select(s => (s.Step, s.Outcome, s.Count)));
        var domain = Assert.Single(_db.NewContext().DomainInteractionDailies);
        Assert.Equal((7, "enter", 5, 2), (domain.DomainId, domain.Kind, domain.Count, domain.UniquePlayers));
        Assert.Single(_db.NewContext().WorldAnalyticsBatches);
    }

    [Fact]
    public async Task WindowJustAfterLocalMidnight_LandsOnTheNextDay()
    {
        // 22:30 UTC on 3 Oct = 00:30 on 4 Oct in Amsterdam (CEST, UTC+2).
        _db.Clock = new DateTime(2026, 10, 3, 23, 0, 0, DateTimeKind.Utc);
        var batch = WorldAnalyticsTestDb.Batch(new DateTime(2026, 10, 3, 22, 30, 0, DateTimeKind.Utc));
        batch.MovementCells!.Add(WorldAnalyticsTestDb.Cell(0, 0, 1));

        var result = await _db.Ingestion().IngestAsync(batch);

        Assert.Equal(new DateOnly(2026, 10, 4), result.Day);
        Assert.Equal(new DateOnly(2026, 10, 4), _db.NewContext().WorldMovementCellDailies.Single().Day);
    }

    [Fact]
    public async Task SameBatchIdTwice_AppliesOnce()
    {
        var id = Guid.NewGuid();
        var first = WorldAnalyticsTestDb.Batch(id: id);
        first.MovementCells!.Add(WorldAnalyticsTestDb.Cell(0, 0, 5));
        var replay = WorldAnalyticsTestDb.Batch(id: id);
        replay.MovementCells!.Add(WorldAnalyticsTestDb.Cell(0, 0, 5));

        await _db.Ingestion().IngestAsync(first);
        var again = await _db.Ingestion().IngestAsync(replay);

        Assert.True(again.Duplicate);
        Assert.Equal(5, _db.NewContext().WorldMovementCellDailies.Single().Samples);
    }

    [Fact]
    public async Task LaterBatches_AddCounts_AndKeepTheHighestUniquePlayers()
    {
        for (var i = 0; i < 2; i++)
        {
            var batch = WorldAnalyticsTestDb.Batch(WorldAnalyticsTestDb.Now.AddMinutes(-10 + 5 * i));
            batch.MovementCells!.Add(WorldAnalyticsTestDb.Cell(3, 3, 2));
            batch.MenuSteps!.Add(WorldAnalyticsTestDb.Step("kits.overview", "closed", 1));
            // The plugin reports the running distinct count of the day: 4, then 3 after a restart.
            batch.DomainInteractions!.Add(WorldAnalyticsTestDb.Domain("enter", 6, i == 0 ? 4 : 3, domainId: 8));
            await _db.Ingestion().IngestAsync(batch);
        }

        var ctx = _db.NewContext();
        Assert.Equal(4, ctx.WorldMovementCellDailies.Single().Samples);
        Assert.Equal(2, ctx.MenuFunnelDailies.Single().Count);
        var domain = ctx.DomainInteractionDailies.Single();
        Assert.Equal((12, 4), (domain.Count, domain.UniquePlayers));
    }

    [Fact]
    public async Task RepeatedKeysInOneBatch_AreAggregated()
    {
        var batch = WorldAnalyticsTestDb.Batch();
        batch.MovementCells!.Add(WorldAnalyticsTestDb.Cell(1, 1, 2));
        batch.MovementCells.Add(WorldAnalyticsTestDb.Cell(1, 1, 3));
        batch.DomainInteractions!.Add(WorldAnalyticsTestDb.Domain("discover", 1, 1, domainId: 7));
        batch.DomainInteractions.Add(WorldAnalyticsTestDb.Domain("discover", 2, 2, regionId: "ALDMOOR"));

        var result = await _db.Ingestion().IngestAsync(batch);

        Assert.Equal(4, result.Accepted);
        Assert.Equal(5, _db.NewContext().WorldMovementCellDailies.Single().Samples);
        var domain = _db.NewContext().DomainInteractionDailies.Single();
        Assert.Equal((7, 3, 2), (domain.DomainId, domain.Count, domain.UniquePlayers));
    }

    [Fact]
    public async Task RegionIds_ResolveToDomains_UnknownRegionsAreRejected()
    {
        var batch = WorldAnalyticsTestDb.Batch();
        batch.DomainInteractions!.Add(WorldAnalyticsTestDb.Domain("enter", 3, 2, regionId: "brightwater"));
        batch.DomainInteractions.Add(WorldAnalyticsTestDb.Domain("enter", 1, 1, regionId: "__global__"));
        batch.DomainInteractions.Add(WorldAnalyticsTestDb.Domain("leave", 1, 1));

        var result = await _db.Ingestion().IngestAsync(batch);

        Assert.Equal(1, result.Accepted);
        Assert.Equal(new[] { (1, "UnknownRegion"), (2, "MissingDomain") }, result.Rejected.Select(r => (r.Index, r.Code)));
        Assert.All(result.Rejected, r => Assert.Equal(WorldAnalyticsIngestionService.DomainSection, r.Section));
        Assert.Equal(8, _db.NewContext().DomainInteractionDailies.Single().DomainId);
    }

    [Fact]
    public async Task InvalidRows_AreRejectedWithCodes_TheRestApplied()
    {
        var batch = WorldAnalyticsTestDb.Batch();
        batch.MovementCells!.Add(WorldAnalyticsTestDb.Cell(0, 0, 1, world: ""));
        batch.MovementCells.Add(WorldAnalyticsTestDb.Cell(0, 0, 1, size: 0));
        batch.MovementCells.Add(WorldAnalyticsTestDb.Cell(0, 0, 0));
        batch.MovementCells.Add(WorldAnalyticsTestDb.Cell(0, 0, 1));
        batch.MenuSteps!.Add(WorldAnalyticsTestDb.Step("", "opened", 1));
        batch.MenuSteps.Add(WorldAnalyticsTestDb.Step("profile.main", "clicked", 1));
        batch.MenuSteps.Add(WorldAnalyticsTestDb.Step("profile.main", "action:Bad Id", 1, "succeeded"));
        batch.MenuSteps.Add(WorldAnalyticsTestDb.Step("profile.main", "action:menu.open", 1));
        batch.MenuSteps.Add(WorldAnalyticsTestDb.Step("profile.main", "opened", 1, "denied"));
        batch.MenuSteps.Add(WorldAnalyticsTestDb.Step("profile.main", "back", 0));
        batch.DomainInteractions!.Add(WorldAnalyticsTestDb.Domain("teleport", 1, 1, domainId: 7));
        batch.DomainInteractions.Add(WorldAnalyticsTestDb.Domain("enter", -1, 0, domainId: 7));

        var result = await _db.Ingestion().IngestAsync(batch);

        Assert.Equal(1, result.Accepted);
        Assert.Equal(new[]
        {
            ("movementCells", 0, "InvalidWorld"), ("movementCells", 1, "InvalidCellSize"), ("movementCells", 2, "InvalidCount"),
            ("menuSteps", 0, "InvalidMenuKey"), ("menuSteps", 1, "InvalidStep"), ("menuSteps", 2, "InvalidStep"),
            ("menuSteps", 3, "InvalidOutcome"), ("menuSteps", 4, "InvalidOutcome"), ("menuSteps", 5, "InvalidCount"),
            ("domainInteractions", 0, "InvalidKind"), ("domainInteractions", 1, "InvalidCount")
        }, result.Rejected.Select(r => (r.Section, r.Index, r.Code)));
        Assert.Single(_db.NewContext().WorldMovementCellDailies);
        Assert.Empty(_db.NewContext().MenuFunnelDailies);
        Assert.Equal(11, _db.NewContext().WorldAnalyticsBatches.Single().RejectedCount);
    }

    [Theory]
    [InlineData("opened", null, true, TelemetryOutcome.Info)]
    [InlineData("closed", "info", true, TelemetryOutcome.Info)]
    [InlineData("back", "succeeded", false, TelemetryOutcome.Succeeded)]
    [InlineData("action:x", "DENIED", true, TelemetryOutcome.Denied)]
    [InlineData("action:x", "failed", true, TelemetryOutcome.Failed)]
    [InlineData("action:x", "info", false, TelemetryOutcome.Info)]
    [InlineData("action:x", null, false, TelemetryOutcome.Info)]
    [InlineData("action:x", "2", false, TelemetryOutcome.Failed)]
    public void Outcomes_FitTheStep(string step, string? value, bool valid, TelemetryOutcome expected)
    {
        Assert.Equal(valid, WorldAnalyticsIngestionService.TryParseOutcome(value, step, out var outcome));
        if (valid) Assert.Equal(expected, outcome);
    }

    [Fact]
    public async Task StructuralErrors_Throw()
    {
        var ingestion = _db.Ingestion();
        await Assert.ThrowsAsync<ArgumentException>(() => ingestion.IngestAsync(new WorldAnalyticsBatchDto { WindowStart = WorldAnalyticsTestDb.Now }));
        await Assert.ThrowsAsync<ArgumentException>(() => ingestion.IngestAsync(new WorldAnalyticsBatchDto { BatchId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<ArgumentException>(() => ingestion.IngestAsync(WorldAnalyticsTestDb.Batch(WorldAnalyticsTestDb.Now.AddHours(1))));
        await Assert.ThrowsAsync<ArgumentException>(() => ingestion.IngestAsync(WorldAnalyticsTestDb.Batch(WorldAnalyticsTestDb.Now.AddDays(-8))));

        _db.Options.MaxBatchRows = 2;
        var big = WorldAnalyticsTestDb.Batch();
        big.MovementCells!.AddRange(new[] { WorldAnalyticsTestDb.Cell(0, 0, 1), WorldAnalyticsTestDb.Cell(1, 0, 1), WorldAnalyticsTestDb.Cell(2, 0, 1) });
        await Assert.ThrowsAsync<ArgumentException>(() => _db.Ingestion().IngestAsync(big));
        Assert.Empty(_db.NewContext().WorldAnalyticsBatches);
    }

    [Fact]
    public void NoAnalyticsShape_CarriesAPlayerIdentity()
    {
        // Link 7 risk "no user ids or names in any analytics table or payload".
        var types = new[]
        {
            typeof(WorldAnalyticsBatchDto), typeof(WorldMovementCellDto), typeof(MenuFunnelStepDto), typeof(DomainInteractionDto),
            typeof(Models.WorldMovementCellDaily), typeof(Models.MenuFunnelDaily), typeof(Models.DomainInteractionDaily),
            typeof(Models.WorldAnalyticsBatch), typeof(HeatmapDto), typeof(HeatmapCellDto), typeof(MenuFunnelReportDto),
            typeof(MenuFunnelDto), typeof(DomainInteractionReportDto), typeof(DomainInteractionSummaryDto)
        };
        var forbidden = new[] { "user", "uuid", "player", "username", "ip" };
        foreach (var type in types)
        {
            foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                var name = property.Name.ToLowerInvariant();
                Assert.False(forbidden.Any(f => name == f || name.StartsWith(f + "id") || name.EndsWith("userid") || name == f + "name"),
                    $"{type.Name}.{property.Name} looks like a player identity");
            }
        }
    }
}
