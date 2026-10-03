using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Repositories.Interfaces;
using knkwebapi_v2.Services.WorldAnalytics;
using Xunit;

namespace knkwebapi_v2.Tests.Services.WorldAnalytics;

/// <summary>
/// WorldAnalyticsQueryService and WorldAnalyticsRetentionService (IMPLEMENTATION_PLAN.md §3.4;
/// link 7 acceptance criteria 1-2): date ranges, heatmap re-bucketing and truncation, funnel
/// ordering, domain summaries, retention.
/// </summary>
public class WorldAnalyticsQueryServiceTests : IDisposable
{
    private static readonly DateOnly Today = WorldAnalyticsTestDb.Today;

    private readonly WorldAnalyticsTestDb _db = new();

    public void Dispose() => _db.Dispose();

    private void Cells(DateOnly day, string world, short size, params (int X, int Z, int Samples)[] cells)
    {
        foreach (var (x, z, samples) in cells)
        {
            _db.Context.WorldMovementCellDailies.Add(new WorldMovementCellDaily
            {
                Day = day, World = world, CellSize = size, CellX = x, CellZ = z, Samples = samples
            });
        }
        _db.Context.SaveChanges();
    }

    [Fact]
    public void Range_DefaultsToTheLastSevenLocalDays_AndIsBounded()
    {
        var query = _db.Query();

        Assert.Equal((Today.AddDays(-6), Today), query.ResolveRange(null, null));
        Assert.Equal((new DateOnly(2026, 9, 1), new DateOnly(2026, 9, 7)), query.ResolveRange(null, new DateOnly(2026, 9, 7)));
        Assert.Throws<ArgumentException>(() => query.ResolveRange(Today, Today.AddDays(-1)));
        Assert.Throws<ArgumentException>(() => query.ResolveRange(Today.AddDays(-92), Today));
        Assert.Equal((Today.AddDays(-91), Today), query.ResolveRange(Today.AddDays(-91), Today));
    }

    [Fact]
    public async Task Heatmap_SumsDaysInRange_BusiestFirst()
    {
        Cells(Today, "world", 16, (0, 0, 2), (1, -1, 7));
        Cells(Today.AddDays(-1), "world", 16, (0, 0, 3));
        Cells(Today.AddDays(-30), "world", 16, (5, 5, 100));
        Cells(Today, "world_nether", 16, (0, 0, 50));

        var map = await _db.Query().GetHeatmapAsync("world", Today.AddDays(-6), Today, null);

        Assert.Equal(16, map.CellSize);
        Assert.Equal(new[] { (1, -1, 7L), (0, 0, 5L) }, map.Cells.Select(c => (c.X, c.Z, c.Samples)));
        Assert.Equal((7L, 12L, false), (map.MaxSamples, map.TotalSamples, map.Truncated));
    }

    [Fact]
    public async Task Heatmap_CoarserCells_MergeWithFloorDivision()
    {
        Cells(Today, "world", 16, (0, 0, 1), (3, 3, 1), (4, 0, 1), (-1, -1, 1), (-4, 0, 1), (-5, 0, 1));

        var map = await _db.Query().GetHeatmapAsync("world", Today, Today, 64);

        Assert.Equal(64, map.CellSize);
        Assert.Equal(new[] { (-2, 0, 1L), (-1, -1, 1L), (-1, 0, 1L), (0, 0, 2L), (1, 0, 1L) },
            map.Cells.Select(c => (c.X, c.Z, c.Samples)).OrderBy(c => c.X).ThenBy(c => c.Z));
    }

    [Fact]
    public void Rebucket_IgnoresSizesThatDoNotDivideTheRequestedSize()
    {
        var rows = new[] { new MovementCellSum(16, 1, 1, 4), new MovementCellSum(48, 0, 0, 9) };

        var map = WorldAnalyticsQueryService.Rebucket(rows, 32);

        Assert.Equal(new Dictionary<(int, int), long> { [(0, 0)] = 4 }, map);
    }

    [Fact]
    public async Task Heatmap_KeepsTheBusiestCells_WhenTooMany()
    {
        _db.Options.MaxHeatmapCells = 2;
        Cells(Today, "world", 16, (0, 0, 1), (1, 0, 5), (2, 0, 3));

        var map = await _db.Query().GetHeatmapAsync("world", Today, Today, null);

        Assert.True(map.Truncated);
        Assert.Equal(new[] { 5L, 3L }, map.Cells.Select(c => c.Samples));
        Assert.Equal(9L, map.TotalSamples);
    }

    [Fact]
    public async Task Heatmap_ValidatesWorldAndCellSize()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _db.Query().GetHeatmapAsync(" ", Today, Today, null));
        await Assert.ThrowsAsync<ArgumentException>(() => _db.Query().GetHeatmapAsync("world", Today, Today, 0));
        var empty = await _db.Query().GetHeatmapAsync("world", Today, Today, null);
        Assert.Equal((16, 0, 0L), (empty.CellSize, empty.Cells.Count, empty.MaxSamples));
    }

    [Fact]
    public async Task Worlds_ListSamplesAndCellSizes()
    {
        Cells(Today, "world", 16, (0, 0, 2));
        Cells(Today, "world", 32, (0, 0, 1));
        Cells(Today, "world_nether", 16, (0, 0, 9));

        var worlds = await _db.Query().GetWorldsAsync(Today, Today);

        Assert.Equal(new[] { ("world_nether", 9L), ("world", 3L) }, worlds.Select(w => (w.World, w.Samples)));
        Assert.Equal(new[] { 16, 32 }, worlds[1].CellSizes);
    }

    [Fact]
    public async Task MenuFunnels_GroupPerMenu_OpenedBackClosedFirst()
    {
        void Step(string menu, string step, TelemetryOutcome outcome, int count, DateOnly? day = null) =>
            _db.Context.MenuFunnelDailies.Add(new MenuFunnelDaily { Day = day ?? Today, MenuKey = menu, Step = step, Outcome = outcome, Count = count });
        Step("profile.main", "opened", TelemetryOutcome.Info, 10);
        Step("profile.main", "opened", TelemetryOutcome.Info, 5, Today.AddDays(-1));
        Step("profile.main", "action:menu.open", TelemetryOutcome.Succeeded, 6);
        Step("profile.main", "action:menu.open", TelemetryOutcome.Denied, 1);
        Step("profile.main", "action:kits.claim", TelemetryOutcome.Failed, 8);
        Step("profile.main", "closed", TelemetryOutcome.Info, 9);
        Step("profile.main", "back", TelemetryOutcome.Info, 2);
        Step("kits.overview", "opened", TelemetryOutcome.Info, 3);
        _db.Context.SaveChanges();

        var report = await _db.Query().GetMenuFunnelsAsync(null, Today.AddDays(-6), Today);

        Assert.Equal(new[] { "profile.main", "kits.overview" }, report.Menus.Select(m => m.MenuKey));
        var profile = report.Menus[0];
        Assert.Equal((15L, 2L, 9L), (profile.Opened, profile.Back, profile.Closed));
        Assert.Equal(new[]
        {
            ("opened", "info", 15L), ("back", "info", 2L), ("closed", "info", 9L),
            ("action:kits.claim", "failed", 8L), ("action:menu.open", "succeeded", 6L), ("action:menu.open", "denied", 1L)
        }, profile.Steps.Select(s => (s.Step, s.Outcome, s.Count)));

        var one = await _db.Query().GetMenuFunnelsAsync("kits.overview", Today, Today);
        Assert.Equal("kits.overview", Assert.Single(one.Menus).MenuKey);
    }

    [Fact]
    public async Task Domains_SummariseKinds_VisitorDaysAndPeak_WithNames()
    {
        void Row(int domain, string kind, int count, int unique, DateOnly day) =>
            _db.Context.DomainInteractionDailies.Add(new DomainInteractionDaily { Day = day, DomainId = domain, Kind = kind, Count = count, UniquePlayers = unique });
        Row(7, "enter", 10, 4, Today);
        Row(7, "enter", 6, 3, Today.AddDays(-1));
        Row(7, "leave", 15, 4, Today);
        Row(7, "discover", 2, 2, Today);
        Row(99, "enter", 30, 9, Today);
        _db.Context.SaveChanges();

        var report = await _db.Query().GetDomainsAsync(null, Today.AddDays(-6), Today);

        Assert.Equal(new[] { 99, 7 }, report.Domains.Select(d => d.DomainId));
        var deleted = report.Domains[0];
        Assert.Null(deleted.Name);
        var aldmoor = report.Domains[1];
        Assert.Equal(("Aldmoor", "aldmoor", 16L, 15L, 2L, 7L, 4), (aldmoor.Name, aldmoor.RegionId, aldmoor.Enter, aldmoor.Leave,
            aldmoor.Discover, aldmoor.VisitorDays, aldmoor.PeakDailyVisitors));

        var discoveries = await _db.Query().GetDomainsAsync("Discover", Today, Today);
        Assert.Equal("discover", discoveries.Kind);
        Assert.Equal((0L, 2L), (discoveries.Domains.Single().Enter, discoveries.Domains.Single().Discover));
        await Assert.ThrowsAsync<ArgumentException>(() => _db.Query().GetDomainsAsync("teleport", Today, Today));
    }

    [Fact]
    public async Task Retention_RemovesOldDaysAndBatchIds_KeepsTheRest()
    {
        Cells(Today.AddDays(-181), "world", 16, (0, 0, 1));
        Cells(Today.AddDays(-180), "world", 16, (0, 0, 1));
        _db.Context.MenuFunnelDailies.Add(new MenuFunnelDaily { Day = Today.AddDays(-200), MenuKey = "m", Step = "opened", Outcome = TelemetryOutcome.Info, Count = 1 });
        _db.Context.DomainInteractionDailies.Add(new DomainInteractionDaily { Day = Today.AddDays(-181), DomainId = 7, Kind = "enter", Count = 1 });
        _db.Context.WorldAnalyticsBatches.Add(new WorldAnalyticsBatch { BatchId = Guid.NewGuid(), ReceivedAt = WorldAnalyticsTestDb.Now.AddDays(-31) });
        _db.Context.WorldAnalyticsBatches.Add(new WorldAnalyticsBatch { BatchId = Guid.NewGuid(), ReceivedAt = WorldAnalyticsTestDb.Now.AddDays(-29) });
        _db.Context.SaveChanges();

        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        Microsoft.Extensions.DependencyInjection.ServiceCollectionServiceExtensions.AddScoped<IWorldAnalyticsRepository>(services,
            _ => new knkwebapi_v2.Repositories.WorldAnalyticsRepository(_db.NewContext()));
        using var provider = Microsoft.Extensions.DependencyInjection.ServiceCollectionContainerBuilderExtensions.BuildServiceProvider(services);
        var job = new WorldAnalyticsRetentionService(provider,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<WorldAnalyticsRetentionService>.Instance);

        await job.RunOnceAsync(WorldAnalyticsTestDb.Now);

        var ctx = _db.NewContext();
        Assert.Equal(Today.AddDays(-180), ctx.WorldMovementCellDailies.Single().Day);
        Assert.Empty(ctx.MenuFunnelDailies);
        Assert.Empty(ctx.DomainInteractionDailies);
        Assert.Single(ctx.WorldAnalyticsBatches);
    }
}
