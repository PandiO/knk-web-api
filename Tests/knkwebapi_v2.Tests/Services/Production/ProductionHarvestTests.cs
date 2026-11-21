using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static knkwebapi_v2.Tests.Services.Production.ProductionYieldTestDb;

namespace knkwebapi_v2.Tests.Services.Production;

/// <summary>
/// Player harvesting P8a (PandiO/knk-workspace#685, PRODUCTION_KINDS § 6-7, decision record § 3.6, § 11 F9-F10, A16 pool
/// and ledger side): the pool arithmetic (floor 0.5, P-D6), the split of each harvest into the worker's share
/// (SYS_PRODUCTION → SYS_WORLD) and the town's share (SYS_PRODUCTION → Output), carried fractions, idempotent batched
/// reports, the full-Output rule (F10), the pool step in the engine, the survey capacity, the net-output check (F9),
/// the feature flag (A21) and the reconciler.
/// </summary>
public class ProductionHarvestTests : IDisposable
{
    private const int Alex = 900, Sam = 901;

    private readonly ProductionYieldTestDb _y = new();
    private readonly HarvestRuleService _rules;
    private readonly ResourcePoolService _pools;
    private readonly HarvestService _harvest;

    public ProductionHarvestTests()
    {
        var db = _y.T.Db;
        db.Users.Add(new User { Id = Alex, Username = "Alex" });
        db.Users.Add(new User { Id = Sam, Username = "Sam" });
        db.SaveChanges();
        db.ChangeTracker.Clear();
        var settings = new ProductionSettingsReader(db);
        _rules = new HarvestRuleService(db, settings, new ProductionYieldCalculator(db), _y.T.Clock);
        _pools = new ResourcePoolService(db, settings, _y.Engine, _y.T.Clock);
        _harvest = new HarvestService(db, _y.T.Stock, settings, _y.Engine, _y.T.Clock);
    }

    public void Dispose() => _y.Dispose();

    private KnKDbContextShortcuts D => new(_y.T.Db);

    private sealed record KnKDbContextShortcuts(knkwebapi_v2.Properties.KnKDbContext Db)
    {
        public ResourcePool? Pool(int areaId) => Db.Set<ResourcePool>().AsNoTracking().FirstOrDefault(p => p.ProductionAreaId == areaId);
        public List<PlayerHarvest> Harvests => Db.Set<PlayerHarvest>().AsNoTracking().OrderBy(h => h.Id).ToList();
    }

    /// <summary>A Configured logger with one 60/h oak line and a cuboid area: (structure, area, output).</summary>
    private async Task<(int Structure, int Area, int Output)> SetupAsync(string name = "Logger")
    {
        int s = await _y.LoggerAsync(name, "Configured");
        await _y.Lines.CreateAsync(new ProductionLineDto { ProductionStructureId = s, ItemBlueprintId = OakLog, ConfiguredRatePerHour = 60m });
        var area = await _y.Areas.CreateAsync(new ProductionAreaDto
        {
            ProductionStructureId = s, Name = "Grove " + name, Shape = "Cuboid", MinX = 0, MaxX = 9, MinY = 60, MaxY = 62, MinZ = 0, MaxZ = 9,
        });
        return (s, area.Id, _y.OutputOf(s));
    }

    private Task<HarvestRuleDto> RuleAsync(Action<HarvestRuleDto>? change = null)
    {
        var dto = new HarvestRuleDto
        {
            ProductionKindId = LoggerKind, Name = "Oak", Targets = new() { "*_log", "OAK_WOOD" }, YieldItemBlueprintId = OakLog,
            Placeholder = "Sapling", RegenSeconds = 25,
        };
        change?.Invoke(dto);
        return _rules.CreateAsync(dto);
    }

    private static HarvestReportEntryDto Entry(string key, int rule, int area, int breaks = 1, int user = Alex, int fortune = 0) => new()
    {
        IdempotencyKey = key, UserId = user, HarvestRuleId = rule, ProductionAreaId = area, Breaks = breaks, FortuneLevel = fortune,
    };

    private async Task<HarvestEntryResultDto> ReportOneAsync(HarvestReportEntryDto e) =>
        (await _harvest.ReportAsync(new HarvestReportDto { Entries = new() { e } })).Entries.Single();

    private long World(int blueprint) => _y.T.Amount(_y.T.Db.Storages.AsNoTracking().Single(s => s.SystemKey == Storage.SysWorld).Id, blueprint);

    private bool LedgerClean() => new StockReconciler(_y.T.Db, _y.T.Clock).RunAsync().Result.Clean;

    // ---------------------------------------------------------------- pool arithmetic (§ 6.3)

    [Theory]
    [InlineData(0.5, 1.0, 1.0)]
    [InlineData(0.5, 0.0, 0.5)] // never stops: the floor of 0.5 (P-D6)
    [InlineData(0.5, 0.5, 0.75)]
    [InlineData(0.0, 0.25, 0.25)]
    public void PoolFactor_IsFloorPlusTheRestTimesTheFill(decimal floor, decimal fill, decimal expected) =>
        Assert.Equal(expected, ResourcePoolMath.Factor(floor, fill));

    [Fact]
    public void Pool_RegeneratesLazily_UpToItsCapacity()
    {
        var at = new DateTime(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);
        var pool = new ResourcePool { Capacity = 100, Current = 40, RegenPerHour = 10, LastRegenAt = at };
        Assert.Equal(40m, ResourcePoolMath.CurrentAt(pool, at));
        Assert.Equal(55m, ResourcePoolMath.CurrentAt(pool, at.AddMinutes(90)));
        Assert.Equal(100m, ResourcePoolMath.CurrentAt(pool, at.AddHours(10)));
        Assert.Equal(6m, ResourcePoolMath.HoursToFull(40, 100, 10));
        Assert.Null(ResourcePoolMath.HoursToFull(100, 100, 10));
        // Several pools combine by their units: (0 + 100) of 200 = half full.
        var empty = new ResourcePool { Capacity = 100, Current = 0, LastRegenAt = at };
        var full = new ResourcePool { Capacity = 100, Current = 100, LastRegenAt = at };
        Assert.Equal(0.75m, ResourcePoolMath.StructureFactor(new[] { empty, full }, 0.5m, at));
        Assert.Equal(1m, ResourcePoolMath.StructureFactor(Array.Empty<ResourcePool>(), 0.5m, at));
    }

    [Fact]
    public void Yield_CountsFortuneUpToFour_AndRegenSlowsAtALowPool()
    {
        var rule = new HarvestRule { YieldPerBreak = 2m, FortuneBonusPerLevel = 0.25m, RegenSeconds = 60 };
        Assert.Equal(6m, ResourcePoolMath.Yield(rule, 3, 0));
        Assert.Equal(12m, ResourcePoolMath.Yield(rule, 3, 4));
        Assert.Equal(12m, ResourcePoolMath.Yield(rule, 3, 10)); // capped at Fortune IV (legacy V1)
        Assert.Equal(60, ResourcePoolMath.RegenSeconds(rule, 0m));
        rule.RegenScalesWithPool = true;
        Assert.Equal(120, ResourcePoolMath.RegenSeconds(rule, 0m));
        Assert.Equal(90, ResourcePoolMath.RegenSeconds(rule, 0.5m));
    }

    // ---------------------------------------------------------------- the split (§ 6.2)

    [Fact]
    public async Task AHarvest_IsSplitAtOnce_WorkersShareToTheWorld_TheRestToOutput_BothAsPlayerHarvest()
    {
        var (s, area, output) = await SetupAsync();
        var rule = await RuleAsync();

        var r = await ReportOneAsync(Entry("h1", rule.Id, area, breaks: 4));

        Assert.Equal(nameof(PlayerHarvestStatus.Applied), r.Status);
        Assert.Equal((1L, 3L, 3L, 0L), (r.WorkerUnits, r.TownUnits, r.TownAccepted, r.TownLost));
        Assert.Equal((s, OakLog, 25), (r.ProductionStructureId!.Value, r.ItemBlueprintId!.Value, r.RegenSeconds));
        Assert.Equal(3, _y.T.Amount(output, OakLog));
        Assert.Equal(1, World(OakLog));
        var moves = _y.T.Db.StockTransactions.AsNoTracking().Where(t => t.Reason == StockReason.PlayerHarvest).ToList();
        Assert.Equal(2, moves.Count);
        Assert.All(moves, t => Assert.Equal(nameof(PlayerHarvest), t.SourceType));
        Assert.True(LedgerClean());
        var row = D.Harvests.Single();
        Assert.Equal((Alex, 4, 0.25m), (row.UserId, row.Breaks, row.WorkerShare));
    }

    [Fact]
    public async Task Fractions_AccruePerPlayerAndRule_LikeLineAccrual()
    {
        var (_, area, output) = await SetupAsync();
        var rule = await RuleAsync();

        long worker = 0, town = 0;
        for (int i = 1; i <= 4; i++)
        {
            var r = await ReportOneAsync(Entry("f" + i, rule.Id, area));
            worker += r.WorkerUnits;
            town += r.TownUnits;
        }
        var sam = await ReportOneAsync(Entry("sam1", rule.Id, area, user: Sam));

        Assert.Equal((1L, 3L), (worker, town)); // 4 × 0.25 and 4 × 0.75, paid as they reach a whole unit
        Assert.Equal((0L, 0L), (sam.WorkerUnits, sam.TownUnits)); // Sam's fractions are his own
        Assert.Equal(3, _y.T.Amount(output, OakLog));
        var acc = _y.T.Db.Set<HarvestAccrual>().AsNoTracking().Single(a => a.UserId == Alex);
        Assert.Equal((0m, 0m), (acc.WorkerAccrued, acc.TownAccrued));
    }

    [Fact]
    public async Task AReport_IsIdempotentPerEntry_AndAKeyWithAnotherHarvestIsRejected()
    {
        var (_, area, output) = await SetupAsync();
        var rule = await RuleAsync();
        await ReportOneAsync(Entry("k1", rule.Id, area, breaks: 4));

        var batch = await _harvest.ReportAsync(new HarvestReportDto
        {
            Entries = new() { Entry("k1", rule.Id, area, breaks: 4), Entry("k2", rule.Id, area, breaks: 4), Entry("k3", rule.Id + 99, area) },
        });

        Assert.Equal(new[] { "Replayed", "Applied", "Rejected" }, batch.Entries.Select(e => e.Status));
        Assert.Equal(1, batch.Entries[0].WorkerUnits);
        Assert.Contains("not found", batch.Entries[2].Error);
        Assert.Equal(6, _y.T.Amount(output, OakLog));
        var reused = await ReportOneAsync(Entry("k1", rule.Id, area, breaks: 5));
        Assert.Equal("Rejected", reused.Status);
        Assert.Contains("different harvest", reused.Error);
        await Assert.ThrowsAsync<ArgumentException>(() => _harvest.ReportAsync(new HarvestReportDto
        {
            Entries = new() { Entry("same", rule.Id, area), Entry("same", rule.Id, area) },
        }));
        Assert.Equal(2, D.Harvests.Count);
    }

    [Fact]
    public async Task AFullOutput_LosesTheTownsShare_ButThePlayerIsStillPaid_F10()
    {
        var (_, area, output) = await SetupAsync();
        var rule = await RuleAsync();
        _y.T.Update(db => db.Storages.Single(x => x.Id == output), st => st.MaxUnits = 2);

        var r = await ReportOneAsync(Entry("full", rule.Id, area, breaks: 8));

        Assert.Equal((2L, 6L, 2L, 4L), (r.WorkerUnits, r.TownUnits, r.TownAccepted, r.TownLost));
        Assert.Contains("lost to the town", r.ShortfallReason);
        Assert.Equal(2, _y.T.Amount(output, OakLog));
        Assert.Equal(2, World(OakLog));
        Assert.True(LedgerClean());
    }

    [Fact]
    public async Task EntriesTheApiMayNotBook_AreRejected_WithAReason()
    {
        var (s, area, _) = await SetupAsync();
        var rule = await RuleAsync();
        var off = await RuleAsync(d => { d.Name = "Off"; d.Enabled = false; });
        var quarryRule = await _rules.CreateAsync(new HarvestRuleDto
        {
            ProductionKindId = ProductionEngineTestDb.QuarryKind, Name = "Stone", Targets = new() { "STONE" }, YieldItemBlueprintId = ProductionEngineTestDb.Cobblestone,
        });

        Assert.Contains("switched off", (await ReportOneAsync(Entry("a", off.Id, area))).Error);
        Assert.Contains("another production kind", (await ReportOneAsync(Entry("b", quarryRule.Id, area))).Error);
        Assert.Contains("User 4242 not found", (await ReportOneAsync(Entry("c", rule.Id, area, user: 4242))).Error);
        Assert.Contains("ProductionArea 777", (await ReportOneAsync(Entry("d", rule.Id, 777))).Error);
        await _y.UpdateAsync(s, d => d.ProductionEnabled = false);
        Assert.Contains("switched off", (await ReportOneAsync(Entry("e", rule.Id, area))).Error);
        Assert.Empty(D.Harvests);
        Assert.Null(D.Pool(area));
    }

    // ---------------------------------------------------------------- the pool (§ 6.3)

    [Fact]
    public async Task TheFirstHarvest_MakesTheAreasPool_Full_ThenDrawsThePoolCost_NotBelowZero()
    {
        var (_, area, _) = await SetupAsync();
        var rule = await RuleAsync(d => d.PoolCost = 300m);

        var first = await ReportOneAsync(Entry("p1", rule.Id, area, breaks: 2));
        Assert.Equal((1000m, 400m, 1000m), (first.PoolBefore, first.PoolAfter, first.PoolCapacity)); // the default capacity
        var pool = D.Pool(area)!;
        Assert.Equal(1000m / 12m, pool.RegenPerHour, 4); // full again in the default 12 hours

        var second = await ReportOneAsync(Entry("p2", rule.Id, area, breaks: 2));
        Assert.Equal((400m, 0m), (second.PoolBefore, second.PoolAfter));

        _y.T.Advance(TimeSpan.FromHours(6)); // half of the capacity back
        var third = await ReportOneAsync(Entry("p3", rule.Id, area, breaks: 1));
        Assert.Equal(500m, third.PoolBefore, 2);
    }

    [Fact]
    public async Task ThePoolStep_SlowsTheStructure_DownToTheFloor_AndTheTraceShowsIt()
    {
        var (s, area, output) = await SetupAsync();
        var rule = await RuleAsync(d => d.PoolCost = 1000m);
        var line = _y.LineFor(s, OakLog)!;

        await ReportOneAsync(Entry("drain", rule.Id, area)); // empties the 1000-unit pool
        _y.T.Advance(TimeSpan.FromHours(1));
        await _y.Engine.SettleLineAsync(line.Id);

        var record = _y.T.Records(line.Id).Last();
        decimal fill = Math.Round(1000m / 12m / 1000m, 6);
        Assert.Equal(ResourcePoolMath.Factor(0.5m, fill), record.PoolFactor);
        Assert.Equal(Math.Round(60m * record.PoolFactor, 6), record.EffectiveRatePerHour);
        var overview = (await _y.Engine.OverviewAsync(s))!;
        Assert.Contains(overview.Lines.Single().NotProducingReasons, r => r.Contains("resource pool"));
        Assert.True(overview.Lines.Single().PoolFactor < 1m);
        Assert.True(_y.T.Amount(output, OakLog) > 0);

        // A kind's floor of 1 switches the step off.
        await _y.Kinds.UpdateAsync(LoggerKind, new ProductionKindDto { PoolFloor = 1m });
        _y.T.Advance(TimeSpan.FromHours(1));
        await _y.Engine.SettleLineAsync(line.Id);
        Assert.Equal(1m, _y.T.Records(line.Id).Last().PoolFactor);
    }

    [Fact]
    public async Task TheHarvest_SettlesTheStructureFirst_SoTheTimeBeforeCountsAtTheOldFactor()
    {
        var (s, area, _) = await SetupAsync();
        var rule = await RuleAsync(d => d.PoolCost = 1000m);
        var line = _y.LineFor(s, OakLog)!;
        _y.T.Advance(TimeSpan.FromHours(1));

        await ReportOneAsync(Entry("late", rule.Id, area));

        var record = _y.T.Records(line.Id).Last();
        Assert.Equal((1m, 60m), (record.PoolFactor, record.EffectiveRatePerHour)); // the hour before the harvest at full speed
    }

    [Fact]
    public async Task ANewSurvey_SetsThePoolsCapacity_KeepingItsFill()
    {
        int s = await _y.LoggerAsync("Wood");
        var area = await _y.RadiusAsync(s);
        await _y.ReportAsync(area.Id, ("TREE:OAK:SMALL", 200, 1));
        var rule = await RuleAsync(d => d.PoolCost = 50m);

        var r = await ReportOneAsync(Entry("s1", rule.Id, area.Id, breaks: 2));
        Assert.Equal((200m, 100m), (r.PoolCapacity, r.PoolAfter)); // capacity from the latest survey's total units

        await _y.ReportAsync(area.Id, ("TREE:OAK:SMALL", 300, 1), ("TREE:BIRCH:SMALL", 100, 1));
        var pool = D.Pool(area.Id)!;
        Assert.Equal((400m, 200m), (pool.Capacity, pool.Current)); // still half full
        // A typed-in capacity is kept when the staff switch surveys off.
        await _pools.UpdateAsync(pool.Id, new ResourcePoolDto { CapacityFromSurvey = false, Capacity = 1000m });
        await _y.ReportAsync(area.Id, ("TREE:OAK:SMALL", 10, 1));
        Assert.Equal(1000m, D.Pool(area.Id)!.Capacity);
    }

    [Fact]
    public async Task StaffMakeAndTuneAPool_OnePerArea()
    {
        var (s, area, _) = await SetupAsync();
        var made = await _pools.CreateAsync(new ResourcePoolDto { ProductionAreaId = area, Capacity = 500m });
        Assert.Equal((500m, 500m, false), (made.Capacity!.Value, made.Current!.Value, made.CapacityFromSurvey!.Value));
        Assert.Equal(1m, made.PoolFactor);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _pools.CreateAsync(new ResourcePoolDto { ProductionAreaId = area }));

        await _pools.UpdateAsync(made.Id, new ResourcePoolDto { Current = 125m, RegenPerHour = 0m });
        var read = (await _pools.GetByIdAsync(made.Id))!;
        Assert.Equal((0.25m, 0.625m, null), (read.Fill, read.PoolFactor, read.HoursToFull));
        Assert.Equal(s, read.ProductionStructureId);
        await Assert.ThrowsAsync<ArgumentException>(() => _pools.UpdateAsync(made.Id, new ResourcePoolDto { Capacity = 0m }));
        var page = await _pools.SearchAsync(new PagedQueryDto { Filters = new() { ["productionStructureId"] = s.ToString() } });
        Assert.Single(page.Items);
    }

    // ---------------------------------------------------------------- the net-output check (§ 6.3, F9)

    [Fact]
    public async Task TheBalance_ComparesTheTownsShare_WithTheProductionLostUntilThePoolRefills()
    {
        var (_, area, _) = await SetupAsync();
        var cheap = await RuleAsync();
        await _pools.CreateAsync(new ResourcePoolDto { ProductionAreaId = area, Capacity = 1000m, RegenPerHour = 100m });

        var ok = await _rules.BalanceAsync(cheap.Id);
        var a = ok.Areas.Single();
        // 60 × (1 − 0.5) × (1 / 1000) × (1 / 100) / 2 = 0.00015 < 0.75
        Assert.Equal((60m, 0.00015m, 0.75m, true), (a.BaseRatePerHour, a.LostPerBreak!.Value, a.TownSharePerBreak, a.NetPositive));
        Assert.True(ok.NetPositive);
        Assert.Empty(ok.Warnings);

        var greedy = await RuleAsync(d => { d.Name = "Greedy"; d.PoolCost = 100m; d.WorkerShare = 0.5m; });
        await _pools.UpdateAsync(_y.T.Db.Set<ResourcePool>().AsNoTracking().Single().Id, new ResourcePoolDto { RegenPerHour = 1m });
        var bad = await _rules.BalanceAsync(greedy.Id);
        // 60 × 0.5 × (100 / 1000) × (100 / 1) / 2 = 150 > 0.5
        Assert.Equal(150m, bad.Areas.Single().LostPerBreak);
        Assert.False(bad.NetPositive);
        Assert.Single(bad.Warnings);

        await _pools.UpdateAsync(_y.T.Db.Set<ResourcePool>().AsNoTracking().Single().Id, new ResourcePoolDto { RegenPerHour = 0m });
        var never = await _rules.BalanceAsync(cheap.Id);
        Assert.Null(never.Areas.Single().LostPerBreak);
        Assert.Contains("does not regenerate", never.Warnings.Single());
    }

    [Fact]
    public async Task TheBalance_UsesANewPoolsDefaults_ForAnAreaWithoutAPool()
    {
        var (_, _, _) = await SetupAsync();
        var rule = await RuleAsync();
        var a = (await _rules.BalanceAsync(rule.Id)).Areas.Single();
        Assert.False(a.PoolExists);
        Assert.Equal((1000m, Math.Round(1000m / 12m, 6)), (a.PoolCapacity, a.RegenPerHour));
        Assert.True(a.NetPositive);
    }

    // ---------------------------------------------------------------- rules

    [Fact]
    public async Task ARule_IsValidated_AndCannotBeDeletedOnceItHasHarvests()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => RuleAsync(d => d.Targets = null));
        await Assert.ThrowsAsync<ArgumentException>(() => RuleAsync(d => d.Targets = new() { "COAL ORE" }));
        await Assert.ThrowsAsync<ArgumentException>(() => RuleAsync(d => d.WorkerShare = 1.5m));
        await Assert.ThrowsAsync<ArgumentException>(() => RuleAsync(d => d.RequiredPermission = "Knk Harvest"));
        await Assert.ThrowsAsync<ArgumentException>(() => RuleAsync(d => d.MinTitleBracketId = 999));
        var rule = await RuleAsync(d => { d.Targets = null; d.TargetsJson = "[\"wheat\"]"; d.MinBlockAge = 7; d.RequiredPermission = "KNK.harvest.farm"; });
        Assert.Equal(new List<string> { "WHEAT" }, rule.Targets);
        Assert.Equal(("knk.harvest.farm", 7, 0.25m), (rule.RequiredPermission, rule.MinBlockAge!.Value, rule.WorkerShare!.Value));
        await Assert.ThrowsAsync<ArgumentException>(() => RuleAsync()); // the same name in the kind
        await Assert.ThrowsAsync<ArgumentException>(() => _rules.UpdateAsync(rule.Id, new HarvestRuleDto { ProductionKindId = ProductionEngineTestDb.QuarryKind }));

        await _rules.UpdateAsync(rule.Id, new HarvestRuleDto { MinBlockAge = 0, RequiredPermission = "" });
        var cleared = (await _rules.GetByIdAsync(rule.Id))!;
        Assert.Equal((null, null), (cleared.MinBlockAge, cleared.RequiredPermission));

        var (_, area, _) = await SetupAsync();
        await ReportOneAsync(Entry("del", rule.Id, area));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _rules.DeleteAsync(rule.Id));
        var unused = await RuleAsync(d => d.Name = "Unused");
        await _rules.DeleteAsync(unused.Id);
        Assert.Null(await _rules.GetByIdAsync(unused.Id));
    }

    // ---------------------------------------------------------------- the flag (A21) and the catalogue

    [Fact]
    public async Task WithProductionOff_ReportsAre409_AndTheCatalogueIsEmpty()
    {
        var (_, area, _) = await SetupAsync();
        var rule = await RuleAsync();
        _y.T.SetSettings(s => s.Enabled = false);

        await Assert.ThrowsAsync<ProductionOffException>(() => _harvest.ReportAsync(new HarvestReportDto { Entries = new() { Entry("x", rule.Id, area) } }));
        var catalog = await _harvest.CatalogAsync();
        Assert.False(catalog.FeatureEnabled);
        Assert.Empty(catalog.Rules);
        Assert.Empty(catalog.Areas);
        Assert.Null(D.Pool(area));
    }

    [Fact]
    public async Task TheCatalogue_ListsTheEnabledRules_AndTheAreasOfTheirKinds_WithThePoolFill()
    {
        var (s, area, _) = await SetupAsync();
        var rule = await RuleAsync(d => d.PoolCost = 250m);
        await RuleAsync(d => { d.Name = "Off"; d.Enabled = false; });
        await ReportOneAsync(Entry("c1", rule.Id, area));

        var catalog = await _harvest.CatalogAsync();

        Assert.True(catalog.FeatureEnabled);
        Assert.Equal(new[] { "Oak" }, catalog.Rules.Select(r => r.Name));
        Assert.Equal(new List<string> { "*_LOG", "OAK_WOOD" }, catalog.Rules[0].Targets);
        var a = catalog.Areas.Single();
        Assert.Equal((area, s, LoggerKind, "Cuboid", 0.75m), (a.ProductionAreaId, a.ProductionStructureId, a.ProductionKindId, a.Shape, a.PoolFill));
    }

    [Fact]
    public async Task TheHistory_IsSearchable_ByPlayerAndStructure()
    {
        var (s, area, _) = await SetupAsync();
        var rule = await RuleAsync();
        await ReportOneAsync(Entry("h-a", rule.Id, area, breaks: 4));
        await ReportOneAsync(Entry("h-s", rule.Id, area, breaks: 4, user: Sam));

        var page = await _harvest.SearchAsync(new PagedQueryDto { Filters = new() { ["userId"] = Sam.ToString() } });
        Assert.Equal("Sam: Oak", page.Items.Single().Name);
        var all = await _harvest.SearchAsync(new PagedQueryDto { Filters = new() { ["productionStructureId"] = s.ToString() } });
        Assert.Equal(2, all.TotalCount);
        var one = (await _harvest.GetAsync(page.Items.Single().Id))!;
        Assert.Equal(("Sam", "Oak", "Oak log"), (one.User!.Name, one.HarvestRule!.Name, one.ItemBlueprint!.Name));
    }

    [Fact]
    public void HarvestSettings_AreValidated_AndKeptWhenAClientOmitsThem()
    {
        var stored = new ProductionSettingsDto { Harvest = new ProductionHarvestSettingsDto { DefaultPoolCapacity = 50m } };
        var kept = ProductionSettings.Normalize(new ProductionSettingsDto(), stored);
        Assert.Equal(50m, kept.Harvest!.DefaultPoolCapacity);
        Assert.Throws<ArgumentException>(() => ProductionSettings.Normalize(new ProductionSettingsDto
        {
            Harvest = new ProductionHarvestSettingsDto { DefaultRegenHoursToFull = 0m },
        }));
    }
}
