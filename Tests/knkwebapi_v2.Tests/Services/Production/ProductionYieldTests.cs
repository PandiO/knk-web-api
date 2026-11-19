using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Production;
using knkwebapi_v2.Services.Territory;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static knkwebapi_v2.Tests.Services.Production.ProductionEngineTestDb;
using static knkwebapi_v2.Tests.Services.Production.ProductionYieldTestDb;

namespace knkwebapi_v2.Tests.Services.Production;

/// <summary>
/// The engine of <see cref="ProductionEngineTestDb"/> with every P4 part wired the way DI does: the Derived and Hybrid
/// providers, the primary and variation steps, the yield, area, survey and override services. Adds the logger
/// blueprints and the Logger kind's yield table (oak and birch logs per tree, sticks as a byproduct).
/// </summary>
internal sealed class ProductionYieldTestDb : IDisposable
{
    public const int OakLog = 510, BirchLog = 511, Stick = 512, Apple = 513;
    public const int LoggerKind = 2;

    public ProductionEngineTestDb T { get; }
    public ProductionEngine Engine { get; }
    public ProductionYieldService Yield { get; }
    public ProductionAreaService Areas { get; }
    public AreaSurveyService Surveys { get; }
    public ProductionYieldOverrideService Overrides { get; }
    public ProductionStructureService Structures { get; }
    public ProductionLineService Lines { get; }
    public ProductionKindService Kinds { get; }

    public ProductionYieldTestDb(bool productionEnabled = true)
    {
        T = new ProductionEngineTestDb(productionEnabled);
        var db = T.Db;
        foreach (var (id, name) in new[] { (OakLog, "Oak log"), (BirchLog, "Birch log"), (Stick, "Stick"), (Apple, "Apple") })
            db.ItemBlueprints.Add(new ItemBlueprint { Id = id, Name = name, DefaultDisplayName = name });
        db.SaveChanges();
        db.ChangeTracker.Clear();

        var settings = new ProductionSettingsReader(db);
        var weather = new ProductionWeatherService(db, T.Clock);
        var calculator = new ProductionYieldCalculator(db);
        Engine = new ProductionEngine(db, T.Stock, settings, weather,
            new IProductionModifierSource[]
            {
                new LevelModifierSource(), new WeatherModifierSource(db, weather), new StoredModifierSource(db),
                new PrimaryOutputModifierSource(db), new VariationModifierSource(),
            },
            new IProductionBaseRateProvider[]
            {
                new ConfiguredBaseRateProvider(), new DerivedBaseRateProvider(calculator), new HybridBaseRateProvider(calculator),
            }, null, T.Notifications, T.Clock);
        Yield = new ProductionYieldService(db, calculator, Engine, settings, T.Clock);
        Areas = new ProductionAreaService(db, settings, Engine, Yield, T.Clock);
        Surveys = new AreaSurveyService(db);
        Overrides = new ProductionYieldOverrideService(db, Engine, Yield);
        Structures = new ProductionStructureService(db, ProductionWorldTestDb.Writer(db), new knkwebapi_v2.Services.Stock.StorageService(db),
            ProductionWorldTestDb.Mapper(), Engine, Yield);
        Lines = new ProductionLineService(db, settings, Engine, T.Clock);
        Kinds = new ProductionKindService(db, Engine, Yield);
    }

    public void Dispose() => T.Dispose();

    /// <summary>A PUT as the web app sends it, through the P4 structure service.</summary>
    public async Task UpdateAsync(int id, Action<ProductionStructureDto> change)
    {
        var dto = (await Structures.GetByIdAsync(id))!;
        change(dto);
        await Structures.UpdateAsync(id, dto);
    }

    /// <summary>The Logger kind's yield table: oak logs 2 per large tree (prefix), 1 per other oak; birch 1 per tree;
    /// sticks at 0.5 per oak tree with chance 0.2 (a byproduct); apples only from level 3.</summary>
    public Task SeedLoggerTableAsync() => Kinds.UpdateAsync(LoggerKind, new ProductionKindDto
    {
        YieldTable = new()
        {
            new ProductionYieldRowDto { UnitKey = "TREE:OAK:LARGE", ItemBlueprintId = OakLog, RatePerUnitPerHour = 2m },
            new ProductionYieldRowDto { UnitKey = "TREE:OAK:*", ItemBlueprintId = OakLog, RatePerUnitPerHour = 1m },
            new ProductionYieldRowDto { UnitKey = "tree:birch:*", ItemBlueprintId = BirchLog, RatePerUnitPerHour = 1m },
            new ProductionYieldRowDto { UnitKey = "TREE:OAK:*", ItemBlueprintId = Stick, RatePerUnitPerHour = 0.5m, ByproductChance = 0.2m },
            new ProductionYieldRowDto { UnitKey = "TREE:OAK:*", ItemBlueprintId = Apple, RatePerUnitPerHour = 1m, MinLevel = 3 },
        },
    });

    public async Task<int> LoggerAsync(string name = "Logger", string? yieldSource = null)
    {
        var s = await Structures.CreateAsync(new ProductionStructureDto
        {
            Name = name, Description = "", WgRegionId = "tmp_" + name, TownId = ProductionWorldTestDb.Phoenix,
            ProductionKindId = LoggerKind, YieldSource = yieldSource,
        });
        return s.Id!.Value;
    }

    public Task<ProductionAreaDto> RadiusAsync(int structureId, int radius = 20, int x = 0, int z = 0, string name = "Forest") =>
        Areas.CreateAsync(new ProductionAreaDto
        {
            ProductionStructureId = structureId, Name = name, Shape = "Radius", Radius = radius, CenterX = x, CenterY = 64, CenterZ = z,
        });

    public Task<AreaSurveyDto> ReportAsync(int areaId, params (string Key, long Count, int SharedBy)[] units) =>
        Areas.ReportSurveyAsync(areaId, new AreaSurveyDto
        {
            ContentHash = "h" + units.Length,
            Units = units.Select(u => new AreaSurveyUnitDto { UnitKey = u.Key, Count = u.Count, SharedBy = u.SharedBy }).ToList(),
        });

    public ProductionLine? LineFor(int structureId, int blueprint) =>
        T.Db.ProductionLines.AsNoTracking().FirstOrDefault(l => l.ProductionStructureId == structureId && l.ItemBlueprintId == blueprint);

    public int OutputOf(int structureId) =>
        T.Db.Storages.AsNoTracking().Single(s => s.DomainId == structureId && s.Purpose == StoragePurpose.Output).Id;
}

/// <summary>
/// Yield areas and surveys P4 (PandiO/knk-workspace#138, PRODUCTION_KINDS § 3-4): area shapes, the level's size cap
/// (G6, A11), exclusive overlaps, the survey queue and reports (A10, G5), Derived and Hybrid materialization, the equal
/// split of shared units (G7, A11), the primary and variation steps, and the rate explanation (smoke § 5.1).
/// </summary>
public class ProductionYieldTests : IDisposable
{
    private readonly ProductionYieldTestDb _y = new();

    public void Dispose() => _y.Dispose();

    // ---------------------------------------------------------------- areas (§ 3.1, G6)

    [Fact]
    public async Task AnArea_OverTheLevelsCap_IsRefusedWithTheMaximum_AndALevelUpAllowsMore()
    {
        int logger = await _y.LoggerAsync();
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _y.RadiusAsync(logger, radius: 33));
        Assert.Contains("allows at most 32", ex.Message);
        var ok = await _y.RadiusAsync(logger, radius: 32);
        Assert.Equal(32, ok.Size);
        Assert.Equal("radius", ok.SizeUnit);
        Assert.Equal(32, ok.MaxSize);

        // A quarry caps volume: 64 × 64 × 64 = 262144 at level 1.
        var (quarry, _, _) = await _y.T.QuarryAsync();
        var cube = new ProductionAreaDto { ProductionStructureId = quarry, Name = "Pit", Shape = "Cuboid", MinX = 100, MinY = 0, MinZ = 100 };
        cube.MaxX = 163; cube.MaxY = 63; cube.MaxZ = 163;
        Assert.Equal(262144, (await _y.Areas.CreateAsync(cube)).Size);
        var bigger = await Assert.ThrowsAsync<ArgumentException>(() => _y.Areas.UpdateAsync(_y.T.Db.ProductionAreas.Single(a => a.Name == "Pit").Id,
            new ProductionAreaDto { MaxY = 64 }));
        Assert.Contains("262144", bigger.Message);

        await _y.UpdateAsync(logger, d => d.Level = 2);
        Assert.Equal(36, (await _y.RadiusAsync(logger, radius: 36, name: "Wider")).Size);
    }

    [Fact]
    public async Task Shapes_ComputeTheirBoundingBoxAndSize_AndNeedTheirFields()
    {
        int farmId = (await _y.Structures.CreateAsync(new ProductionStructureDto
        {
            Name = "Farm", Description = "", WgRegionId = "tmp_farm", TownId = ProductionWorldTestDb.Phoenix, ProductionKindId = 1,
        })).Id!.Value;
        // A polygon field: a 10 × 20 rectangle counts 200 blocks of footprint (BorderedField).
        var field = await _y.Areas.CreateAsync(new ProductionAreaDto
        {
            ProductionStructureId = farmId, Name = "North field", Shape = "Polygon", MinY = 60, MaxY = 70,
            Points = new() { new() { X = 0, Z = 0 }, new() { X = 10, Z = 0 }, new() { X = 10, Z = 20 }, new() { X = 0, Z = 20 } },
        });
        Assert.Equal(200, field.Size);
        Assert.Equal("blocks", field.SizeUnit);
        Assert.True(field.Exclusive);
        Assert.Equal((0, 10, 0, 20), (field.MinX, field.MaxX, field.MinZ, field.MaxZ));

        // A region needs its id and its bounding box; corners in any order.
        var region = await _y.Areas.CreateAsync(new ProductionAreaDto
        {
            ProductionStructureId = farmId, Name = "South field", Shape = "Region", WgRegionId = "farm_south",
            MinX = 50, MaxX = 41, MinY = 60, MaxY = 62, MinZ = 0, MaxZ = 9,
        });
        Assert.Equal((41, 50, 100L), (region.MinX!.Value, region.MaxX!.Value, region.Size));
        await Assert.ThrowsAsync<ArgumentException>(() => _y.Areas.CreateAsync(new ProductionAreaDto
        {
            ProductionStructureId = farmId, Name = "No id", Shape = "Region", MinX = 0, MaxX = 1, MinY = 0, MaxY = 1, MinZ = 0, MaxZ = 1,
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => _y.Areas.CreateAsync(new ProductionAreaDto
        {
            ProductionStructureId = farmId, Name = "Two points", Shape = "Polygon", MinY = 0, MaxY = 1,
            Points = new() { new() { X = 0, Z = 0 }, new() { X = 1, Z = 1 } },
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => _y.Areas.CreateAsync(new ProductionAreaDto
        {
            ProductionStructureId = farmId, Name = "Odd", Shape = "Sphere",
        }));
        // The same name twice on one structure.
        await Assert.ThrowsAsync<ArgumentException>(() => _y.Areas.CreateAsync(new ProductionAreaDto
        {
            ProductionStructureId = farmId, Name = "North field", Shape = "Cuboid", MinX = 500, MaxX = 501, MinY = 0, MaxY = 1, MinZ = 0, MaxZ = 1,
        }));
    }

    [Fact]
    public async Task ARadius_CentresOnTheStructuresLocation_AndAStructureWithoutKindHasNoArea()
    {
        int logger = await _y.LoggerAsync();
        _y.T.Db.Locations.Add(new Location { Id = 900, X = 100.7, Y = 70, Z = -40.2, World = "world" });
        _y.T.Db.SaveChanges();
        _y.T.Update(db => db.ProductionStructures.Single(s => s.Id == logger), s => s.LocationId = 900);
        var area = await _y.Areas.CreateAsync(new ProductionAreaDto { ProductionStructureId = logger, Name = "Wood", Shape = "Radius", Radius = 10 });
        Assert.Equal((100, -41), (area.CenterX!.Value, area.CenterZ!.Value));
        Assert.Equal((90, 110, ProductionArea.WorldMinY, ProductionArea.WorldMaxY), (area.MinX!.Value, area.MaxX!.Value, area.MinY!.Value, area.MaxY!.Value));

        var (noKind, _, _) = await _y.T.QuarryAsync(kind: null, name: "Kindless");
        var ex = await Assert.ThrowsAsync<ArgumentException>(() => _y.RadiusAsync(noKind));
        Assert.Contains("production kind", ex.Message);
    }

    [Fact]
    public async Task ExclusiveAreasOfTheSameModel_MayNotOverlap_SharedOnesMay()
    {
        var (quarry, _, _) = await _y.T.QuarryAsync();
        var (mine, _, _) = await _y.T.QuarryAsync(kind: MineKind, name: "Mine");
        await _y.Areas.CreateAsync(new ProductionAreaDto
        {
            ProductionStructureId = quarry, Name = "Pit", Shape = "Cuboid", MinX = 0, MaxX = 20, MinY = 0, MaxY = 20, MinZ = 0, MaxZ = 20,
        });
        // A mine is an ExclusiveVolume too: its shaft may not cut into the quarry's pit.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => _y.Areas.CreateAsync(new ProductionAreaDto
        {
            ProductionStructureId = mine, Name = "Shaft", Shape = "Cuboid", MinX = 20, MaxX = 30, MinY = 0, MaxY = 20, MinZ = 0, MaxZ = 20,
        }));
        Assert.Contains("Pit", ex.Message);
        // Next to it, or in another world, is fine; so is a shaft made non-exclusive by staff.
        await _y.Areas.CreateAsync(new ProductionAreaDto
        {
            ProductionStructureId = mine, Name = "Shaft", Shape = "Cuboid", MinX = 21, MaxX = 30, MinY = 0, MaxY = 20, MinZ = 0, MaxZ = 20,
        });
        await _y.Areas.CreateAsync(new ProductionAreaDto
        {
            ProductionStructureId = mine, Name = "Nether shaft", WorldName = "world_nether", Shape = "Cuboid",
            MinX = 0, MaxX = 20, MinY = 0, MaxY = 20, MinZ = 0, MaxZ = 20,
        });

        // Two loggers' radii overlap freely, and each lists the other as an overlap.
        int a = await _y.LoggerAsync("Logger A"), b = await _y.LoggerAsync("Logger B");
        var ra = await _y.RadiusAsync(a, x: 1000);
        var rb = await _y.RadiusAsync(b, x: 1010);
        Assert.False(ra.Exclusive);
        var overlaps = await _y.Areas.OverlapsAsync(ra.Id);
        Assert.Equal(rb.Id, Assert.Single(overlaps).ProductionAreaId);
    }

    // ---------------------------------------------------------------- the survey queue (A10, G5)

    [Fact]
    public async Task ANewArea_IsDueOnCreation_ThenOnceADay_AndOnRequestFirst()
    {
        int logger = await _y.LoggerAsync();
        var area = await _y.RadiusAsync(logger);
        var due = Assert.Single(await _y.Areas.SurveyDueAsync(50));
        Assert.Equal(("Creation", area.Id), (due.Trigger, due.Area.Id));
        Assert.Equal("""{"unitTypes":["TREE","SAPLING"]}""", due.SurveyProfileJson);
        Assert.Equal(1, due.SurveyProfileVersion);

        var report = await _y.ReportAsync(area.Id, ("TREE:OAK:LARGE", 3, 1));
        Assert.Equal("Creation", report.Trigger);
        Assert.Empty(await _y.Areas.SurveyDueAsync(50));

        _y.T.Advance(TimeSpan.FromHours(23));
        Assert.Empty(await _y.Areas.SurveyDueAsync(50));
        _y.T.Advance(TimeSpan.FromHours(1));
        Assert.Equal("Scheduled", Assert.Single(await _y.Areas.SurveyDueAsync(50)).Trigger);
        await _y.ReportAsync(area.Id, ("TREE:OAK:LARGE", 3, 1));

        // "Survey now" (the command and the web app button): due at once, before scheduled ones.
        var other = await _y.LoggerAsync("Other");
        await _y.RadiusAsync(other, x: 5000);
        await _y.Areas.RequestSurveyAsync(logger, null);
        var list = await _y.Areas.SurveyDueAsync(50);
        Assert.Equal("Admin", list.Single(d => d.Area.Id == area.Id).Trigger);
        Assert.Equal("Creation", list.Single(d => d.Area.Id != area.Id).Trigger);
        var admin = await _y.ReportAsync(area.Id, ("TREE:OAK:LARGE", 3, 1));
        Assert.Equal("Admin", admin.Trigger);
        // Every report is a new survey row (history), the newest is the area's latest.
        var history = await _y.Surveys.SearchAsync(new PagedQueryDto { PageNumber = 1, PageSize = 10, Filters = new() { ["productionAreaId"] = area.Id.ToString() } });
        Assert.Equal(3, history.TotalCount);
        Assert.Equal(admin.Id, (await _y.Areas.GetByIdAsync(area.Id))!.LastSurveyId);
    }

    [Fact]
    public async Task AConfiguredStructure_IsNeverSurveyed()
    {
        int logger = await _y.LoggerAsync(yieldSource: "Configured");
        var area = await _y.RadiusAsync(logger);
        Assert.Null(area.SurveyRequestedAt);
        Assert.Empty(await _y.Areas.SurveyDueAsync(50));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _y.Areas.RequestSurveyAsync(logger, null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _y.ReportAsync(area.Id, ("TREE:OAK:LARGE", 3, 1)));
        Assert.Empty(_y.T.Db.AreaSurveys);

        // Switched to Hybrid, its never-surveyed area is due.
        await _y.UpdateAsync(logger, d => d.YieldSource = "Hybrid");
        Assert.Equal("Creation", Assert.Single(await _y.Areas.SurveyDueAsync(50)).Trigger);
    }

    [Fact]
    public async Task WhileTheFeatureIsOff_NothingIsDue_AndReportsAreRefused_ButAreasCanBeEdited()
    {
        using var off = new ProductionYieldTestDb(productionEnabled: false);
        int logger = await off.LoggerAsync();
        var area = await off.RadiusAsync(logger);
        Assert.NotNull(area.SurveyRequestedAt);
        Assert.Empty(await off.Areas.SurveyDueAsync(50));
        await Assert.ThrowsAsync<InvalidOperationException>(() => off.ReportAsync(area.Id, ("TREE:OAK:LARGE", 3, 1)));
        Assert.Equal(25, (await off.Areas.UpdateAsync(area.Id, new ProductionAreaDto { Radius = 25 })).Size);
    }

    [Fact]
    public async Task ASurveyReport_ChecksItsUnits_AndALateOlderSurveyStaysInTheHistoryOnly()
    {
        int logger = await _y.LoggerAsync();
        var area = await _y.RadiusAsync(logger);
        await Assert.ThrowsAsync<ArgumentException>(() => _y.ReportAsync(area.Id, ("TREE OAK", 3, 1)));
        await Assert.ThrowsAsync<ArgumentException>(() => _y.ReportAsync(area.Id, ("TREE:OAK", -1, 1)));
        await Assert.ThrowsAsync<ArgumentException>(() => _y.ReportAsync(area.Id, ("TREE:OAK", 1, 0)));
        await Assert.ThrowsAsync<ArgumentException>(() => _y.ReportAsync(area.Id, ("TREE:OAK", 1, 1), ("tree:oak", 2, 1)));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _y.ReportAsync(9999, ("TREE:OAK", 1, 1)));

        var now = await _y.ReportAsync(area.Id, ("tree:oak:large", 5, 1));
        Assert.Equal("TREE:OAK:LARGE", Assert.Single(now.Units!).UnitKey);
        var late = await _y.Areas.ReportSurveyAsync(area.Id, new AreaSurveyDto
        {
            SurveyedAt = _y.T.Clock.Now.UtcDateTime.AddHours(-2), Units = new() { new() { UnitKey = "TREE:OAK:LARGE", Count = 1 } },
        });
        Assert.Equal(now.Id, (await _y.Areas.GetByIdAsync(area.Id))!.LastSurveyId);
        Assert.NotEqual(now.Id, late.Id);
    }

    [Fact]
    public async Task TheTrend_ListsEachUnitKeyPerSurvey_OldestFirst()
    {
        int logger = await _y.LoggerAsync();
        var area = await _y.RadiusAsync(logger);
        await _y.ReportAsync(area.Id, ("TREE:OAK:LARGE", 12, 1), ("TREE:BIRCH:SMALL", 4, 1));
        _y.T.Advance(TimeSpan.FromDays(1));
        await _y.ReportAsync(area.Id, ("TREE:OAK:LARGE", 7, 1));
        var trend = await _y.Surveys.TrendAsync(area.Id, 30);
        Assert.Equal(2, trend.Surveys.Count);
        Assert.Equal(new long[] { 12, 7 }, trend.Series["TREE:OAK:LARGE"]);
        Assert.Equal(new long[] { 4, 0 }, trend.Series["TREE:BIRCH:SMALL"]);
        var latest = (await _y.Surveys.GetByIdAsync(trend.Surveys[1].Id))!;
        Assert.Equal(7, latest.TotalUnits);
    }

    // ---------------------------------------------------------------- yields (§ 4.1-4.2)

    [Fact]
    public async Task Derived_MaterializesALinePerYieldedOutput_AndProducesWhatTheForestHolds()
    {
        await _y.SeedLoggerTableAsync();
        int logger = await _y.LoggerAsync(yieldSource: "Derived");
        var area = await _y.RadiusAsync(logger);
        var report = await _y.ReportAsync(area.Id, ("TREE:OAK:LARGE", 10, 1), ("TREE:OAK:SMALL", 4, 1), ("TREE:BIRCH:MEDIUM", 6, 1),
            ("TREE:SPRUCE:LARGE", 3, 1));
        Assert.Equal(3, report.Materialization!.LinesCreated.Count); // oak, birch, sticks; apples need level 3
        var oak = _y.LineFor(logger, OakLog)!;
        Assert.Equal(ProductionRateSource.Derived, oak.RateSource);
        Assert.Null(_y.LineFor(logger, Apple));

        var yield = (await _y.Yield.ExplainAsync(logger))!;
        // Exact beats prefix: 10 large oaks × 2 + 4 small × 1 = 24 per hour.
        Assert.Equal(24m, yield.Outputs.Single(o => o.ItemBlueprintId == OakLog).BaseRatePerHour);
        Assert.Equal(6m, yield.Outputs.Single(o => o.ItemBlueprintId == BirchLog).BaseRatePerHour);
        // Sticks: 14 oaks × 0.5 × 0.2 = 1.4 per hour (a byproduct share, not a roll).
        Assert.Equal(1.4m, yield.Outputs.Single(o => o.ItemBlueprintId == Stick).BaseRatePerHour);
        Assert.Equal(new[] { "TREE:SPRUCE:LARGE" }, yield.UnmatchedUnitKeys);
        var contribution = yield.Outputs.Single(o => o.ItemBlueprintId == OakLog).Contributions.Single(c => c.UnitKey == "TREE:OAK:LARGE");
        Assert.Equal(("TREE:OAK:LARGE", 20m), (contribution.YieldRowUnitKey, contribution.RatePerHour));

        _y.T.Advance(TimeSpan.FromHours(1));
        await _y.Engine.SweepAsync(all: true);
        int output = _y.OutputOf(logger);
        Assert.Equal(24, _y.T.Amount(output, OakLog));
        Assert.Equal(6, _y.T.Amount(output, BirchLog));
        Assert.Equal(1, _y.T.Amount(output, Stick));

        // The forest is cut down: the next survey lowers the rate; a vanished output keeps its line at 0.
        var cut = await _y.ReportAsync(area.Id, ("TREE:OAK:LARGE", 2, 1));
        Assert.Contains(_y.LineFor(logger, BirchLog)!.Id, cut.Materialization!.LinesWithoutYield);
        _y.T.Advance(TimeSpan.FromHours(1));
        await _y.Engine.SweepAsync(all: true);
        Assert.Equal(24 + 4, _y.T.Amount(output, OakLog));
        Assert.Equal(6, _y.T.Amount(output, BirchLog));
    }

    [Fact]
    public async Task ATreeInTwoLoggersAreas_YieldsHalfToEach()
    {
        await _y.SeedLoggerTableAsync();
        int a = await _y.LoggerAsync("Logger A", "Derived"), b = await _y.LoggerAsync("Logger B", "Derived");
        var ra = await _y.RadiusAsync(a, x: 0);
        var rb = await _y.RadiusAsync(b, x: 10);
        // The game server sees each structure's overlapping shared areas and counts per unit how many contain it.
        var due = await _y.Areas.SurveyDueAsync(50);
        Assert.Equal(rb.Id, Assert.Single(due.Single(d => d.Area.Id == ra.Id).OverlappingSharedAreas).ProductionAreaId);
        await _y.ReportAsync(ra.Id, ("TREE:OAK:LARGE", 6, 1), ("TREE:OAK:LARGE", 4, 2));
        await _y.ReportAsync(rb.Id, ("TREE:OAK:LARGE", 4, 2));
        var ya = (await _y.Yield.ExplainAsync(a))!.Outputs.Single(o => o.ItemBlueprintId == OakLog);
        var yb = (await _y.Yield.ExplainAsync(b))!.Outputs.Single(o => o.ItemBlueprintId == OakLog);
        Assert.Equal(6 * 2m + 4 * 2m / 2, ya.BaseRatePerHour);
        Assert.Equal(4 * 2m / 2, yb.BaseRatePerHour);
        Assert.Equal(0.5m, yb.Contributions.Single().Share);
        // The shared trees yield 8 per hour in total, however many loggers crowd them.
        Assert.Equal(8m, ya.Contributions.Single(c => c.SharedBy == 2).RatePerHour + yb.BaseRatePerHour);
    }

    [Fact]
    public async Task Hybrid_PinsExclusionsAndScales_ApplyAfterTheSurvey()
    {
        await _y.SeedLoggerTableAsync();
        int logger = await _y.LoggerAsync(); // Hybrid by default (P-D1)
        Assert.Equal("Hybrid", (await _y.Structures.GetByIdAsync(logger))!.YieldSource);
        var area = await _y.RadiusAsync(logger);

        // A pin makes its line at once, whatever the survey says (smoke § 5.1 step 8).
        var pin = await _y.Overrides.CreateAsync(new ProductionYieldOverrideDto
        {
            ProductionStructureId = logger, ItemBlueprintId = Apple, Kind = "Pin", Value = 5m,
        });
        Assert.Equal(ProductionRateSource.Hybrid, _y.LineFor(logger, Apple)!.RateSource);
        await _y.Overrides.CreateAsync(new ProductionYieldOverrideDto { ProductionStructureId = logger, ItemBlueprintId = BirchLog, Kind = "Exclude" });
        await _y.Overrides.CreateAsync(new ProductionYieldOverrideDto { ProductionStructureId = logger, ItemBlueprintId = OakLog, Kind = "Scale", Value = 0.5m });
        await Assert.ThrowsAsync<ArgumentException>(() => _y.Overrides.CreateAsync(new ProductionYieldOverrideDto
        {
            ProductionStructureId = logger, ItemBlueprintId = OakLog, Kind = "Pin", Value = 1m,
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => _y.Overrides.CreateAsync(new ProductionYieldOverrideDto
        {
            ProductionStructureId = logger, ItemBlueprintId = Stick, Kind = "Pin", Value = 0m,
        }));

        await _y.ReportAsync(area.Id, ("TREE:OAK:LARGE", 10, 1), ("TREE:BIRCH:MEDIUM", 6, 1));
        Assert.Null(_y.LineFor(logger, BirchLog)); // excluded: no line
        var yield = (await _y.Yield.ExplainAsync(logger))!;
        Assert.Equal((20m, "Scale", 10m), Pick(yield, OakLog));
        Assert.Equal((6m, "Exclude", 0m), Pick(yield, BirchLog));
        Assert.Equal((0m, "Pin", 5m), Pick(yield, Apple));

        _y.T.Advance(TimeSpan.FromHours(1));
        await _y.Engine.SweepAsync(all: true);
        int output = _y.OutputOf(logger);
        Assert.Equal((10L, 5L, 0L), (_y.T.Amount(output, OakLog), _y.T.Amount(output, Apple), _y.T.Amount(output, BirchLog)));

        // Removing the pin leaves the apple line without yield.
        await _y.Overrides.DeleteAsync(pin.Id);
        Assert.Equal(0m, (await _y.Yield.ExplainAsync(logger))!.Outputs.Where(o => o.ItemBlueprintId == Apple).Sum(o => o.BaseRatePerHour));
        Assert.NotNull(_y.LineFor(logger, Apple));

        static (decimal Derived, string? Override, decimal Base) Pick(ProductionYieldDto y, int bp)
        {
            var o = y.Outputs.Single(x => x.ItemBlueprintId == bp);
            return (o.DerivedRatePerHour, o.Override, o.BaseRatePerHour);
        }
    }

    [Fact]
    public async Task DerivedIgnoresOverrides_AndSwitchingToConfigured_TurnsSurveyLinesIntoConfiguredOnes()
    {
        await _y.SeedLoggerTableAsync();
        int logger = await _y.LoggerAsync(yieldSource: "Derived");
        var area = await _y.RadiusAsync(logger);
        await _y.Overrides.CreateAsync(new ProductionYieldOverrideDto { ProductionStructureId = logger, ItemBlueprintId = OakLog, Kind = "Exclude" });
        await _y.ReportAsync(area.Id, ("TREE:OAK:LARGE", 10, 1));
        var yield = (await _y.Yield.ExplainAsync(logger))!;
        Assert.Equal(20m, yield.Outputs.Single(o => o.ItemBlueprintId == OakLog).BaseRatePerHour);
        Assert.Contains(yield.Notes, n => n.Contains("Hybrid overrides are ignored"));

        // A survey line follows its structure: Derived lines only on Derived structures.
        await Assert.ThrowsAsync<ArgumentException>(() => _y.Lines.CreateAsync(new ProductionLineDto
        {
            ProductionStructureId = logger, ItemBlueprintId = Stick, RateSource = "Hybrid",
        }));

        var dto = (await _y.Structures.GetByIdAsync(logger))!;
        dto.YieldSource = "Configured";
        await _y.Structures.UpdateAsync(logger, dto);
        Assert.Equal(ProductionRateSource.Configured, _y.LineFor(logger, OakLog)!.RateSource);
        Assert.False((await _y.Yield.ExplainAsync(logger))!.SurveysRun);

        // A kind that doesn't allow a source refuses it.
        _y.T.Update(db => db.ProductionKinds.Single(k => k.Id == ProductionYieldTestDb.LoggerKind), k => k.AllowsDerived = false);
        dto.YieldSource = "Derived";
        await Assert.ThrowsAsync<ArgumentException>(() => _y.Structures.UpdateAsync(logger, dto));
    }

    [Fact]
    public async Task AConfiguredLine_OnAHybridStructure_KeepsItsRate()
    {
        await _y.SeedLoggerTableAsync();
        int logger = await _y.LoggerAsync();
        var manual = await _y.Lines.CreateAsync(new ProductionLineDto { ProductionStructureId = logger, ItemBlueprintId = OakLog, ConfiguredRatePerHour = 3m });
        var area = await _y.RadiusAsync(logger);
        var report = await _y.ReportAsync(area.Id, ("TREE:OAK:LARGE", 10, 1));
        Assert.Equal(new[] { manual.Id }, report.Materialization!.SkippedConfigured);
        _y.T.Advance(TimeSpan.FromHours(1));
        await _y.Engine.SweepAsync(all: true);
        Assert.Equal(3, _y.T.Amount(_y.OutputOf(logger), OakLog));
    }

    [Fact]
    public async Task MinLevelRows_StartAtTheirLevel_AndPrimaryOutputs_GetTheKindsMultiplierInTheTrace()
    {
        await _y.SeedLoggerTableAsync();
        await _y.Kinds.UpdateAsync(ProductionYieldTestDb.LoggerKind, new ProductionKindDto
        {
            PrimaryMultiplier = 2m, PrimaryOutputs = new() { new ProductionKindPrimaryOutputDto { ItemBlueprintId = OakLog } },
        });
        int logger = await _y.LoggerAsync(yieldSource: "Derived");
        var area = await _y.RadiusAsync(logger);
        await _y.ReportAsync(area.Id, ("TREE:OAK:LARGE", 10, 1));
        Assert.Null(_y.LineFor(logger, Apple));
        await _y.UpdateAsync(logger, d => d.Level = 3); // a new level re-materializes: apples unlock
        Assert.NotNull(_y.LineFor(logger, Apple));

        _y.T.Advance(TimeSpan.FromHours(1));
        await _y.Engine.SweepAsync(all: true);
        var oak = _y.LineFor(logger, OakLog)!;
        var record = _y.T.Records(oak.Id).Last();
        Assert.Contains("\"Primary\"", record.ModifiersJson);
        // 20 per hour × level 3 (1.3) × primary 2 = 52.
        Assert.Equal(52m, record.EffectiveRatePerHour);
        var apple = _y.T.Records(_y.LineFor(logger, Apple)!.Id).Last();
        Assert.DoesNotContain("\"Primary\"", apple.ModifiersJson);
    }

    [Fact]
    public async Task TheKindsYieldTableAndProfile_AreChecked_AndAProfileChangeRaisesItsVersion()
    {
        await _y.SeedLoggerTableAsync();
        var kind = (await _y.Kinds.GetByIdAsync(ProductionYieldTestDb.LoggerKind))!;
        Assert.Equal(5, kind.YieldTable!.Count);
        Assert.Equal("TREE:BIRCH:*", kind.YieldTable.Single(r => r.ItemBlueprintId == BirchLog).UnitKey);
        await Assert.ThrowsAsync<ArgumentException>(() => _y.Kinds.UpdateAsync(2, new ProductionKindDto
        {
            YieldTable = new() { new() { UnitKey = "TREE:*:LARGE", ItemBlueprintId = OakLog, RatePerUnitPerHour = 1 } },
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => _y.Kinds.UpdateAsync(2, new ProductionKindDto
        {
            YieldTable = new() { new() { UnitKey = "TREE", ItemBlueprintId = OakLog, RatePerUnitPerHour = 1, ByproductChance = 2 } },
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => _y.Kinds.UpdateAsync(2, new ProductionKindDto { SurveyProfileJson = "[1]" }));
        await _y.Kinds.UpdateAsync(2, new ProductionKindDto { SurveyProfileJson = """{ "unitTypes": ["TREE"] }""" });
        var updated = (await _y.Kinds.GetByIdAsync(2))!;
        Assert.Equal((2, """{"unitTypes":["TREE"]}"""), (updated.SurveyProfileVersion, updated.SurveyProfileJson));
        await _y.Kinds.UpdateAsync(2, new ProductionKindDto { SurveyProfileJson = """{"unitTypes":["TREE"]}""" });
        Assert.Equal(2, (await _y.Kinds.GetByIdAsync(2))!.SurveyProfileVersion);
    }

    [Fact]
    public void TheVariation_IsWithinTenPercent_AndTheSameForTheSameLineAndHour()
    {
        var at = new DateTime(2026, 11, 19, 10, 15, 0, DateTimeKind.Utc);
        var factors = Enumerable.Range(1, 200).Select(id => VariationModifierSource.Factor(id, at)).ToList();
        Assert.All(factors, f => Assert.InRange(f, 0.9m, 1.1m));
        Assert.True(factors.Distinct().Count() > 100);
        Assert.Equal(VariationModifierSource.Factor(7, at), VariationModifierSource.Factor(7, at.AddMinutes(30)));
    }

    [Fact]
    public async Task DeletingAnArea_RemovesItsSurveys_AndItsYield()
    {
        await _y.SeedLoggerTableAsync();
        int logger = await _y.LoggerAsync();
        var area = await _y.RadiusAsync(logger);
        await _y.ReportAsync(area.Id, ("TREE:OAK:LARGE", 10, 1));
        await _y.Areas.DeleteAsync(area.Id);
        Assert.Empty(_y.T.Db.AreaSurveys);
        Assert.Empty(_y.T.Db.AreaSurveyUnits);
        Assert.Equal(0m, (await _y.Yield.ExplainAsync(logger))!.Outputs.Sum(o => o.BaseRatePerHour));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _y.Areas.DeleteAsync(area.Id));
    }
}
