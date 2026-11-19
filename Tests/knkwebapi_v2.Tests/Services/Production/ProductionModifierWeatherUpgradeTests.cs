using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services;
using knkwebapi_v2.Services.Interfaces;
using knkwebapi_v2.Services.Production;
using Microsoft.EntityFrameworkCore;
using Moq;
using Xunit;
using static knkwebapi_v2.Tests.Services.Production.ProductionEngineTestDb;

namespace knkwebapi_v2.Tests.Services.Production;

/// <summary>
/// The modifier pipeline (scope, target, window, additions before multiplications; PRODUCTION_KINDS § 4.2, F8), the
/// weather (one stored roll per zone and day, identical on replay; § 4.4), level upgrades (stock and coins; § 5) and the
/// catalogue (eight seeded kinds with ten levels each, G8, P-D10). Acceptance A08, A09.
/// </summary>
public class ProductionModifierWeatherUpgradeTests : IDisposable
{
    private readonly Mock<ICurrencyService> _currency = new();
    private readonly ProductionEngineTestDb _t;

    public ProductionModifierWeatherUpgradeTests()
    {
        _t = new ProductionEngineTestDb(currency: _currency.Object);
    }

    public void Dispose() => _t.Dispose();

    private ProductionModifierService Modifiers => new(_t.Db);

    private static ProductionModifierDto Mod(string name, string scope, int? refId, string effect, decimal value, string target = "Any") =>
        new() { Name = name, Scope = scope, ScopeRefId = refId, Target = target, Effect = effect, Value = value };

    private async Task<long> HourAsync(int output)
    {
        long before = _t.Amount(output, Cobblestone);
        _t.Advance(60);
        await _t.Engine.SweepAsync();
        return _t.Amount(output, Cobblestone) - before;
    }

    [Fact]
    public async Task Additions_ComeBeforeMultiplications_AndTheTraceListsThemInThatOrder()
    {
        var (_, line, output) = await _t.QuarryAsync(cap: null);
        await Modifiers.CreateAsync(Mod("Half", "Global", null, "Multiply", 0.5m));
        await Modifiers.CreateAsync(Mod("Plus 40", "Global", null, "Add", 40m));
        Assert.Equal(50, await HourAsync(output)); // (60 + 40) × 0.5 × level 1 (1.0); multiply-first would give 70
        var trace = ProductionCycleRecordMapping.ToDto(_t.Records(line).Last(), null);
        Assert.Equal(new[] { "Plus 40", "Level 1", "Half" }, trace.Modifiers.Select(m => m.Name));
    }

    [Theory]
    [InlineData("Town", true)]
    [InlineData("Territory", true)]
    [InlineData("Structure", true)]
    [InlineData("Output", true)]
    [InlineData("Kingdom", false)]
    [InlineData("MapArea", false)]
    public async Task Scopes_MatchTheStructuresTownTerritoryItselfOrItsLine(string scope, bool applies)
    {
        var (sid, line, output) = await _t.QuarryAsync(cap: null);
        int? refId = scope switch
        {
            "Town" or "Territory" => ProductionWorldTestDb.Phoenix,
            "Structure" => sid,
            "Output" => line,
            "Kingdom" => 0,
            _ => null,
        };
        if (scope == "Kingdom")
        {
            _t.Db.Kingdoms.Add(new Kingdom { Id = 70, Name = "Elsewhere" });
            _t.Db.SaveChanges();
            refId = 70; // Phoenix is in no kingdom
        }
        await Modifiers.CreateAsync(Mod("Double", scope, refId, "Multiply", 2m));
        Assert.Equal(applies ? 120 : 60, await HourAsync(output));
    }

    [Fact]
    public async Task AnotherTownsModifier_DoesNotApply_AndAKingdomModifierFollowsTheTown()
    {
        var (_, _, output) = await _t.QuarryAsync(cap: null);
        await Modifiers.CreateAsync(Mod("Rivertown boom", "Town", ProductionWorldTestDb.Rivertown, "Multiply", 3m));
        Assert.Equal(60, await HourAsync(output));
        _t.Db.Kingdoms.Add(new Kingdom { Id = 71, Name = "Realm" });
        _t.Db.SaveChanges();
        _t.Update(db => db.Towns.Single(t => t.Id == ProductionWorldTestDb.Phoenix), t => t.KingdomId = 71);
        await Modifiers.CreateAsync(Mod("Realm tax", "Kingdom", 71, "Add", -30m));
        Assert.Equal(30, await HourAsync(output));
    }

    [Theory]
    [InlineData("Blueprint", true)]
    [InlineData("Category", true)]
    [InlineData("Kind", true)]
    [InlineData("OtherBlueprint", false)]
    [InlineData("OtherCategory", false)]
    [InlineData("OtherKind", false)]
    public async Task Targets_MatchTheBlueprintCategoryOrKind(string target, bool applies)
    {
        var (_, _, output) = await _t.QuarryAsync(cap: null);
        var dto = Mod("Boost", "Global", null, "Multiply", 2m, target.Replace("Other", ""));
        switch (target)
        {
            case "Blueprint": dto.TargetItemBlueprintId = Cobblestone; break;
            case "OtherBlueprint": dto.TargetItemBlueprintId = Coal; break;
            case "Category": dto.TargetCategory = "Stone"; break; // the Quarry kind's category
            case "OtherCategory": dto.TargetCategory = "Crops"; break;
            case "Kind": dto.TargetProductionKindId = QuarryKind; break;
            case "OtherKind": dto.TargetProductionKindId = MineKind; break;
        }
        await Modifiers.CreateAsync(dto);
        Assert.Equal(applies ? 120 : 60, await HourAsync(output));
    }

    [Fact]
    public async Task TheActiveWindow_StartsInclusive_AndEndsExclusive()
    {
        var (_, _, output) = await _t.QuarryAsync(cap: null);
        var start = _t.Clock.Now.UtcDateTime;
        var dto = Mod("Festival", "Global", null, "Multiply", 2m);
        dto.ActiveFrom = start.AddMinutes(60);
        dto.ActiveUntil = start.AddMinutes(120);
        await Modifiers.CreateAsync(dto);
        Assert.Equal(120, await HourAsync(output)); // the settle at +60 is inside [start+60, start+120)
        Assert.Equal(60, await HourAsync(output)); // the settle at +120 is outside
    }

    [Fact]
    public async Task ModifierValidation_NeedsAnExistingScopeRow_AndTheTargetsField()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => Modifiers.CreateAsync(Mod("x", "Town", null, "Add", 1m)));
        await Assert.ThrowsAsync<ArgumentException>(() => Modifiers.CreateAsync(Mod("x", "Town", 999, "Add", 1m)));
        await Assert.ThrowsAsync<ArgumentException>(() => Modifiers.CreateAsync(Mod("x", "Global", null, "Add", 1m, "Category")));
        await Assert.ThrowsAsync<ArgumentException>(() => Modifiers.CreateAsync(Mod("x", "Global", null, "Multiply", -1m)));
        await Assert.ThrowsAsync<ArgumentException>(() => Modifiers.CreateAsync(Mod("x", "Nowhere", null, "Add", 1m)));
        var bad = Mod("x", "Global", null, "Add", 1m);
        bad.ActiveFrom = DateTime.UtcNow;
        bad.ActiveUntil = bad.ActiveFrom.Value.AddMinutes(-1);
        await Assert.ThrowsAsync<ArgumentException>(() => Modifiers.CreateAsync(bad));
        var global = await Modifiers.CreateAsync(Mod("ok", "Global", 5, "Add", 1m));
        Assert.Null(global.ScopeRefId);
    }

    // ------------------------------------------------------------------ weather

    private async Task StormEveryDayAsync(int zoneId = ProductionCatalogSeed.DefaultClimateZoneId)
    {
        await new WeatherTypeService(_t.Db).UpdateAsync(ProductionCatalogSeed.StormId, new WeatherTypeDto
        {
            Effects = new() { new WeatherTypeEffectDto { Category = "Stone", Effect = "Multiply", Value = 0.5m } },
        });
        await new ClimateZoneService(_t.Db).UpdateAsync(zoneId, new ClimateZoneDto
        {
            WeatherOdds = new() { new ClimateZoneWeatherDto { WeatherTypeId = ProductionCatalogSeed.StormId, ChancePercent = 100m } },
        });
    }

    [Fact]
    public async Task TheWeather_IsRolledOncePerZoneAndDay_AndItsEffectIsAModifierInTheTrace()
    {
        var (sid, line, output) = await _t.QuarryAsync(cap: null);
        await StormEveryDayAsync();
        Assert.Equal(30, await HourAsync(output));
        var trace = ProductionCycleRecordMapping.ToDto(_t.Records(line).Last(), null);
        Assert.Contains(trace.Modifiers, m => m.Source == "Weather" && m.Name.StartsWith("Storm") && m.Value == 0.5m);
        // 20-minute days: the hour's sweeps rolled one row per day touched, never two for the same day.
        var rolls = _t.Db.WeatherRolls.AsNoTracking().ToList();
        Assert.Equal(rolls.Count, rolls.Select(r => (r.ClimateZoneId, r.Day)).Distinct().Count());
        Assert.All(rolls, r => Assert.Equal(ProductionCatalogSeed.StormId, r.WeatherTypeId));
        Assert.Equal("Storm", (await _t.Engine.OverviewAsync(sid))!.WeatherToday);
    }

    [Fact]
    public async Task ARoll_IsIdenticalOnReplay_AndTheStoredRowWinsWhenTheOddsChange()
    {
        var weather = new ProductionWeatherService(_t.Db, _t.Clock);
        var odds = new[]
        {
            new ClimateZoneWeather { WeatherTypeId = ProductionCatalogSeed.RainId, ChancePercent = 30m },
            new ClimateZoneWeather { WeatherTypeId = ProductionCatalogSeed.StormId, ChancePercent = 5m },
        };
        for (long day = 0; day < 50; day++)
            Assert.Equal(ProductionWeatherService.RollValueOf(1, day), ProductionWeatherService.RollValueOf(1, day));
        Assert.NotEqual(ProductionWeatherService.RollValueOf(1, 7), ProductionWeatherService.RollValueOf(2, 7));
        Assert.Equal(ProductionCatalogSeed.RainId, ProductionWeatherService.Pick(odds, 10m));
        Assert.Equal(ProductionCatalogSeed.StormId, ProductionWeatherService.Pick(odds, 32m));
        Assert.Null(ProductionWeatherService.Pick(odds, 35m));

        await StormEveryDayAsync();
        var first = await weather.GetOrRollAsync(1, 1000, 20);
        await new ClimateZoneService(_t.Db).UpdateAsync(1, new ClimateZoneDto { WeatherOdds = new() });
        var again = await weather.GetOrRollAsync(1, 1000, 20);
        Assert.Equal((first.Id, ProductionCatalogSeed.StormId), (again.Id, again.WeatherTypeId!.Value));
        Assert.Equal(1, _t.Db.WeatherRolls.Count(r => r.Day == 1000));
        Assert.Equal(DateTime.UnixEpoch.AddMinutes(20000), first.DayStartsAt);
    }

    [Fact]
    public async Task ATownInItsOwnZone_GetsThatZonesWeather_AndRollAll_RollsEachZoneOnce()
    {
        var zones = new ClimateZoneService(_t.Db);
        var coast = await zones.CreateAsync(new ClimateZoneDto
        {
            Name = "Coast", Towns = new() { new ClimateZoneTownDto { TownId = ProductionWorldTestDb.Phoenix } },
        });
        Assert.False(coast.IsDefault);
        await StormEveryDayAsync(coast.Id);
        var (_, _, output) = await _t.QuarryAsync(cap: null);
        Assert.Equal(30, await HourAsync(output));

        var weather = new ProductionWeatherService(_t.Db, _t.Clock);
        _t.Advance(TimeSpan.FromDays(1));
        Assert.Equal(2, await weather.RollAllAsync(_t.Clock.Now.UtcDateTime, 20));
        Assert.Equal(0, await weather.RollAllAsync(_t.Clock.Now.UtcDateTime, 20));
    }

    [Fact]
    public async Task ClimateZones_KeepExactlyOneDefault_AndOddsAtMost100()
    {
        var zones = new ClimateZoneService(_t.Db);
        var mountain = await zones.CreateAsync(new ClimateZoneDto { Name = "Mountain" });
        await zones.UpdateAsync(mountain.Id, new ClimateZoneDto { IsDefault = true });
        Assert.Equal(new[] { mountain.Id }, _t.Db.ClimateZones.AsNoTracking().Where(z => z.IsDefault).Select(z => z.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => zones.UpdateAsync(mountain.Id, new ClimateZoneDto { IsDefault = false }));
        await Assert.ThrowsAsync<InvalidOperationException>(() => zones.DeleteAsync(mountain.Id));
        await Assert.ThrowsAsync<ArgumentException>(() => zones.UpdateAsync(mountain.Id, new ClimateZoneDto
        {
            WeatherOdds = new()
            {
                new ClimateZoneWeatherDto { WeatherTypeId = ProductionCatalogSeed.RainId, ChancePercent = 70m },
                new ClimateZoneWeatherDto { WeatherTypeId = ProductionCatalogSeed.StormId, ChancePercent = 40m },
            },
        }));
        await zones.UpdateAsync(mountain.Id, new ClimateZoneDto
        {
            WeatherOdds = new() { new ClimateZoneWeatherDto { WeatherTypeId = ProductionCatalogSeed.RainId, ChancePercent = 30m } },
        });
        await Assert.ThrowsAsync<InvalidOperationException>(() => new WeatherTypeService(_t.Db).DeleteAsync(ProductionCatalogSeed.RainId));
        await zones.DeleteAsync(ProductionCatalogSeed.DefaultClimateZoneId); // no longer the default
    }

    // ------------------------------------------------------------------ upgrades

    private async Task<int> SetUpgradeCostAsync(int level, long coins, long cobblestone)
    {
        var row = _t.Db.ProductionKindLevels.AsNoTracking().Single(l => l.ProductionKindId == QuarryKind && l.Level == level);
        await new ProductionKindLevelService(_t.Db).UpdateAsync(row.Id, new ProductionKindLevelDto
        {
            UpgradeCoins = coins,
            Items = cobblestone > 0 ? new() { new ProductionKindLevelItemDto { Role = "UpgradeCost", ItemBlueprintId = Cobblestone, Amount = cobblestone } } : new(),
        });
        return row.Id;
    }

    [Fact]
    public async Task AnUpgrade_PaysItsResourcesAndCoins_AndRaisesTheLevel()
    {
        var (sid, _, output) = await _t.QuarryAsync(cap: null);
        await SetUpgradeCostAsync(2, 250, 40);
        await _t.AdjustAsync(output, Cobblestone, 50);
        _currency.Setup(c => c.SpendAsync(20, Currency.Coins, 250, It.IsAny<CurrencyContext>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new PostingResult { PublicId = "pub-1", ReasonCode = CurrencyReasons.ProductionUpgrade });

        var result = await _t.Engine.UpgradeAsync(sid, new ProductionUpgradeRequestDto { PayerUserId = 20 }, actorUserId: 7);

        Assert.Equal((1, 2, 250L, "pub-1"), (result.FromLevel, result.ToLevel, result.CoinsPaid, result.CurrencyTransactionId));
        Assert.Equal(2, _t.Db.ProductionStructures.AsNoTracking().Single(s => s.Id == sid).Level);
        Assert.Equal(10, _t.Amount(output, Cobblestone));
        var tx = _t.Db.StockTransactions.AsNoTracking().Single(t => t.Id == result.StockTransactionId);
        Assert.Equal((StockReason.Upgrade, 7), (tx.Reason, tx.ActorUserId!.Value));
        _currency.Verify(c => c.SpendAsync(20, Currency.Coins, 250,
            It.Is<CurrencyContext>(x => x.ReasonCode == CurrencyReasons.ProductionUpgrade && x.IdempotencyKey == $"upgrade:{sid}:2"
                && x.InitiatorUserId == 7), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AnUpgradeThatCantBePaid_ChangesNothing()
    {
        var (sid, _, output) = await _t.QuarryAsync(cap: null);
        await SetUpgradeCostAsync(2, 250, 40);
        await _t.AdjustAsync(output, Cobblestone, 10);

        await Assert.ThrowsAsync<ArgumentException>(() => _t.Engine.UpgradeAsync(sid, new ProductionUpgradeRequestDto(), 7)); // no payer
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _t.Engine.UpgradeAsync(sid, new ProductionUpgradeRequestDto { PayerUserId = 20 }, 7));
        Assert.Contains("NotEnoughStock", ex.Message);
        Assert.Equal(1, _t.Db.ProductionStructures.AsNoTracking().Single(s => s.Id == sid).Level);
        Assert.Equal(10, _t.Amount(output, Cobblestone));
        _currency.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task UpgradesStopAtLevel10_AndNeedAKind()
    {
        var (sid, _, _) = await _t.QuarryAsync(cap: null);
        _t.Update(db => db.ProductionStructures.Single(s => s.Id == sid), s => s.Level = 10);
        await Assert.ThrowsAsync<InvalidOperationException>(() => _t.Engine.UpgradeAsync(sid, new ProductionUpgradeRequestDto(), 7));
        var (bare, _, _) = await _t.QuarryAsync(cap: null, kind: null, name: "Kindless");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _t.Engine.UpgradeAsync(bare, new ProductionUpgradeRequestDto(), 7));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _t.Engine.UpgradeAsync(9999, new ProductionUpgradeRequestDto(), 7));
    }

    // ------------------------------------------------------------------ catalogue

    [Fact]
    public void TheSeededCatalogue_HasEightKindsWithTenLevelsEach_AndNoHunter()
    {
        var kinds = _t.Db.ProductionKinds.AsNoTracking().Include(k => k.Levels).ToList();
        Assert.Equal(new[] { "Animal farm", "Farm", "Fishery", "Florist", "Flower farm", "Logger", "Mine", "Quarry" },
            kinds.Select(k => k.Name).OrderBy(n => n));
        Assert.All(kinds, k => Assert.Equal(Enumerable.Range(1, 10), k.Levels.Select(l => l.Level).OrderBy(l => l)));
        Assert.DoesNotContain(kinds, k => k.AreaModel == ProductionAreaModel.HuntingGround);
        Assert.Single(_t.Db.ClimateZones.AsNoTracking().Where(z => z.IsDefault));
        Assert.Empty(_t.Db.ClimateZoneWeathers); // clear weather until staff set odds
    }

    [Fact]
    public async Task ANewKind_GetsTenLevels_AndAKindInUseCantBeDeleted()
    {
        var kinds = new ProductionKindService(_t.Db);
        var created = await kinds.CreateAsync(new ProductionKindDto
        {
            Name = "Salt pan", AreaModel = "BorderedField", ResourceCategory = "Stone",
            DefaultStoragesJson = "[{\"purpose\":\"input\",\"name\":\"brine\"}]",
        });
        Assert.Equal(10, created.Levels!.Count);
        Assert.True(created.ExclusiveAreas);
        Assert.Contains("\"purpose\":\"Input\"", created.DefaultStoragesJson);
        await Assert.ThrowsAsync<ArgumentException>(() => kinds.CreateAsync(new ProductionKindDto { Name = "Salt pan", AreaModel = "Pasture", ResourceCategory = "Animals" }));
        await Assert.ThrowsAsync<ArgumentException>(() => kinds.UpdateAsync(created.Id, new ProductionKindDto { DefaultStoragesJson = "[{\"purpose\":\"Output\",\"name\":\"x\"}]" }));
        await Assert.ThrowsAsync<ArgumentException>(() => kinds.UpdateAsync(created.Id, new ProductionKindDto { AreaModel = "Volcano" }));

        await _t.QuarryAsync(kind: created.Id, name: "Pan");
        await Assert.ThrowsAsync<InvalidOperationException>(() => kinds.DeleteAsync(created.Id));
        await kinds.DeleteAsync(MineKind);
        Assert.Empty(_t.Db.ProductionKindLevels.Where(l => l.ProductionKindId == MineKind));
    }

    [Fact]
    public async Task LevelValidation_RefusesBadMultipliersAndCosts()
    {
        var levels = new ProductionKindLevelService(_t.Db);
        int id = _t.Db.ProductionKindLevels.AsNoTracking().First().Id;
        await Assert.ThrowsAsync<ArgumentException>(() => levels.UpdateAsync(id, new ProductionKindLevelDto { CycleSpeedMultiplier = 0m }));
        await Assert.ThrowsAsync<ArgumentException>(() => levels.UpdateAsync(id, new ProductionKindLevelDto { YieldMultiplier = -1m }));
        await Assert.ThrowsAsync<ArgumentException>(() => levels.UpdateAsync(id, new ProductionKindLevelDto
        {
            Items = new() { new ProductionKindLevelItemDto { Role = "UpgradeCost", ItemBlueprintId = Coal, Amount = 0 } },
        }));
        await levels.UpdateAsync(id, new ProductionKindLevelDto { OutputMaxUnits = 500, MaxAreaSize = 0 });
        var row = (await levels.GetByIdAsync(id))!;
        Assert.Equal((500, (int?)null), (row.OutputMaxUnits, row.MaxAreaSize));
    }

    [Fact]
    public async Task Lines_AreConfiguredOnlyInP3_OnePerBlueprint_AndKeepTheirStructure()
    {
        var (sid, line, _) = await _t.QuarryAsync(cap: null);
        await Assert.ThrowsAsync<ArgumentException>(() => _t.Lines.CreateAsync(new ProductionLineDto
        {
            ProductionStructureId = sid, ItemBlueprintId = Cobblestone, ConfiguredRatePerHour = 1m,
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => _t.Lines.CreateAsync(new ProductionLineDto
        {
            ProductionStructureId = sid, ItemBlueprintId = Coal, RateSource = "Derived",
        }));
        await Assert.ThrowsAsync<ArgumentException>(() => _t.Lines.UpdateAsync(line, new ProductionLineDto { ConfiguredRatePerHour = -1m }));
        var page = await _t.Lines.SearchAsync(new PagedQueryDto { Filters = new() { ["productionStructureId"] = sid.ToString() } });
        Assert.Equal("Quarry: Cobblestone", page.Items.Single().Name);
    }
}
