using knkwebapi_v2.Dtos;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Models;
using knkwebapi_v2.Services.Shops;
using knkwebapi_v2.Services.Stock;
using knkwebapi_v2.Tests.Services.Production;
using Microsoft.EntityFrameworkCore;
using Xunit;
using static knkwebapi_v2.Tests.Services.Shops.ShopTestDb;

namespace knkwebapi_v2.Tests.Services.Shops;

/// <summary>Shops P10 (PandiO/knk-workspace#248): processing lines on the production engine, restocking by the town planner
/// with the shop's own worker, and the hourly trade stats (smoke guide § 11.1).</summary>
public class ShopSupplyTests : IDisposable
{
    private readonly ShopTestDb _s = new();
    private ProductionEngineTestDb E => _s.E;

    public void Dispose() => _s.Dispose();

    private async Task<int> BreadLineAsync(decimal rate = 10m, decimal flourPerHour = 20m)
    {
        var line = await E.Lines.CreateAsync(new ProductionLineDto { ProductionStructureId = _s.Bakery, ItemBlueprintId = Bread, ConfiguredRatePerHour = rate });
        await E.Lines.UpdateAsync(line.Id, new ProductionLineDto { Inputs = new() { new ProductionLineInputDto { ItemBlueprintId = Flour, AmountPerHour = flourPerHour } } });
        return line.Id;
    }

    // ================= Processing =================

    [Fact]
    public async Task AProcessingShop_TurnsFlourIntoBread_FromInputIntoMerchandise()
    {
        await _s.SetUpAsync();
        int line = await BreadLineAsync();
        await _s.Put(_s.Input(_s.Bakery), Flour, 100);
        E.Advance(60);
        await E.Engine.SweepAsync();
        Assert.Equal(10, E.Amount(_s.Merchandise(_s.Bakery), Bread));
        Assert.Equal(80, E.Amount(_s.Input(_s.Bakery), Flour));
        Assert.Equal(ProductionLineStatus.Running, E.Line(line).Status);

        var view = (await E.Engine.OverviewAsync(_s.Bakery))!;
        Assert.Equal(_s.Merchandise(_s.Bakery), view.OutputStorageId);
        Assert.Equal(new StockReconciler(E.Db, E.Clock).RunAsync().Result.Clean, true);
    }

    [Fact]
    public async Task TheShopLevel_SetsProcessingSpeedYieldAndInputCost()
    {
        await _s.SetUpAsync();
        await _s.UpdateShopAsync(_s.Bakery, d => d.Level = 2); // yield × 1.5, inputs × 0.5
        await BreadLineAsync();
        await _s.Put(_s.Input(_s.Bakery), Flour, 100);
        E.Advance(60);
        await E.Engine.SweepAsync();
        Assert.Equal(15, E.Amount(_s.Merchandise(_s.Bakery), Bread));
        Assert.Equal(90, E.Amount(_s.Input(_s.Bakery), Flour));
    }

    [Fact]
    public async Task ATradingShop_DoesntProcess_AndNothingRunsWhileProductionIsOff()
    {
        await _s.SetUpAsync();
        await _s.UpdateShopAsync(_s.Bakery, d => d.Kind = "Trading");
        int line = await BreadLineAsync();
        await _s.Put(_s.Input(_s.Bakery), Flour, 100);
        E.Advance(60);
        await E.Engine.SweepAsync();
        Assert.Equal(0, E.Amount(_s.Merchandise(_s.Bakery), Bread));
        Assert.Equal(ProductionLineStatus.Paused, E.Line(line).Status);
        Assert.Contains("trading shop", E.Line(line).StatusReason);

        using var off = new ShopTestDb(productionEnabled: false);
        await off.SetUpAsync();
        var l = await off.E.Lines.CreateAsync(new ProductionLineDto { ProductionStructureId = off.Bakery, ItemBlueprintId = Bread, ConfiguredRatePerHour = 10m });
        off.E.Advance(60);
        Assert.Equal(0, (await off.E.Engine.SweepAsync()).LinesSettled);
        Assert.Equal(0, off.E.Amount(off.Merchandise(off.Bakery), Bread));
    }

    [Fact]
    public async Task WhileTheWorkerIsAway_ProcessingSlows_AndSellingGoesOn()
    {
        await _s.SetUpAsync();
        await _s.UpdateShopAsync(_s.Bakery, d => d.WorkersTotal = 2);
        await BreadLineAsync();
        await _s.Put(_s.Input(_s.Bakery), Flour, 100);
        await E.Engine.SetWorkersPresentAsync(_s.Bakery, 1);
        E.Advance(60);
        await E.Engine.SweepAsync();
        Assert.Equal(5, E.Amount(_s.Merchandise(_s.Bakery), Bread));
        var bought = await _s.Trading.PurchaseAsync(_s.Bakery, Buy(_s.Ann, Bread, 2, 20, "away"));
        Assert.Equal(40, bought.TotalPrice);
    }

    // ================= Restocking =================

    [Fact]
    public async Task ARestock_IsReservedAtTheWarehouse_AndCollectedByTheShopsOwnWorker()
    {
        await _s.SetUpAsync(logistics: true);
        var t = _s.L.T;
        await t.Lane(_s.Bakery, t.North, 432m);
        await _s.Entry(_s.Bakery, Bread);
        await t.Put(t.NorthStorage, 100, Bread);
        int merch = _s.Merchandise(_s.Bakery);

        var plan = await _s.L.Run();
        var e = knkwebapi_v2.Tests.Services.Logistics.LogisticsTestDb.Entry(plan, merch, Bread);
        Assert.Equal(("Reserved", 40L), (e.Outcome, e.Reserved)); // the level's default max 40
        Assert.Equal("ShopLevel", e.SettingSource);
        var order = Assert.Single(_s.L.Distribution());
        Assert.Equal((_s.Bakery, merch), (order.CarrierStructureId!.Value, order.Stops.Single().StorageId));

        await t.Sweep();
        Assert.Equal(0, t.Structure(_s.Bakery).WorkersPresent); // its only worker is out fetching
        var sale = await _s.Trading.QuoteAsync(_s.Bakery, Buy(_s.Ann, Bread, 1, null, "q"));
        Assert.Equal("SoldOut", sale.Refusal); // nothing yet: selling still works, there is just no stock
        t.Advance(100);
        await t.Sweep();
        Assert.Equal(40, E.Amount(merch, Bread));
        Assert.Equal(1, t.Structure(_s.Bakery).WorkersPresent);
        Assert.True(t.LedgerClean());
    }

    [Fact]
    public async Task AnAdminSetting_OverridesTheLevelDefault()
    {
        await _s.SetUpAsync(logistics: true);
        var t = _s.L.T;
        await t.Lane(_s.Bakery, t.North, 432m);
        await _s.Entry(_s.Bakery, Bread);
        await t.Put(t.NorthStorage, 100, Bread);
        int merch = _s.Merchandise(_s.Bakery);
        await _s.L.MinMax(merch, Bread, 5, 25);
        var plan = await _s.L.Run();
        Assert.Equal(25, knkwebapi_v2.Tests.Services.Logistics.LogisticsTestDb.Entry(plan, merch, Bread).Reserved);
    }

    // ================= Trade stats =================

    [Fact]
    public async Task TheHourlyStats_MatchTheLedger_AndARebuildIsIdempotent()
    {
        await _s.SetUpAsync();
        await _s.FundTownAsync(500);
        await _s.Entry(_s.Bakery, Bread);
        int merch = _s.Merchandise(_s.Bakery);
        var hour = ShopTradeStatService.HourOf(E.Clock.GetUtcNow().UtcDateTime);
        E.Clock.Now = new DateTimeOffset(hour.AddMinutes(5));
        await _s.Put(merch, Bread, 10);
        await _s.Trading.PurchaseAsync(_s.Bakery, Buy(_s.Ann, Bread, 3, 20, "s1"));
        await _s.Trading.PurchaseAsync(_s.Bakery, Buy(_s.Bob, Bread, 2, 20, "s2"));
        await _s.Trading.SellAsync(_s.Bakery, new ShopSellRequestDto
        {
            UserId = _s.Bob, Confirmed = true, ExpectedTotal = 10, IdempotencyKey = "b1", Lines = new() { new ShopSellLineDto { ItemBlueprintId = Bread } },
        });
        E.Clock.Now = new DateTimeOffset(hour.AddHours(2).AddMinutes(1));

        var first = await _s.Stats.RollupAsync();
        var row = E.Db.Set<ShopTradeStat>().AsNoTracking().Single(r => r.ShopId == _s.Bakery);
        Assert.Equal((hour, 5L, 100L, 1L, 10L, 2, 20m, 6L),
            (row.HourStart, row.UnitsSold, row.Revenue, row.UnitsBoughtBack, row.BuyBackPaid, row.DistinctCustomers, row.AvgSellPrice!.Value, row.StockAtEnd));
        Assert.Equal(0, row.UnitsSupplied); // an admin adjustment is not supply
        Assert.Null(row.TownPlayerCount);

        var again = await _s.Stats.RebuildAsync(new ShopTradeStatRebuildDto { ShopId = _s.Bakery, From = hour.AddHours(-1), To = hour.AddHours(2) });
        Assert.Equal((3, 1), (again.Hours, again.RowsWritten));
        var rows = E.Db.Set<ShopTradeStat>().AsNoTracking().Where(r => r.ShopId == _s.Bakery).ToList();
        Assert.Single(rows); // hours with no trade have no row
        Assert.Equal((5L, 100L, 6L), (rows[0].UnitsSold, rows[0].Revenue, rows[0].StockAtEnd));
        Assert.True(first.RowsWritten >= 1);
    }

    [Fact]
    public async Task TheStats_CountProcessingOutputAsSupply_AndTheJobWaitsForProduction()
    {
        await _s.SetUpAsync();
        var hour = ShopTradeStatService.HourOf(E.Clock.GetUtcNow().UtcDateTime);
        E.Clock.Now = new DateTimeOffset(hour.AddMinutes(1));
        await BreadLineAsync();
        await _s.Put(_s.Input(_s.Bakery), Flour, 100);
        E.Advance(30);
        await E.Engine.SweepAsync();
        E.Clock.Now = new DateTimeOffset(hour.AddHours(1).AddMinutes(1));
        await _s.Stats.RollupAsync();
        var row = E.Db.Set<ShopTradeStat>().AsNoTracking().Single(r => r.ShopId == _s.Bakery && r.ItemBlueprintId == Bread);
        Assert.Equal((5L, 5L, 0L), (row.UnitsSupplied, row.StockAtEnd, row.UnitsSold));

        using var off = new ShopTestDb(productionEnabled: false);
        await off.SetUpAsync();
        Assert.Equal(0, (await off.Stats.RollupAsync()).Hours);
    }
}
