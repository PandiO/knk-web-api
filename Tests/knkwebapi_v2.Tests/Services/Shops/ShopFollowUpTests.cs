using Xunit;
using knkwebapi_v2.Dtos;
using knkwebapi_v2.Models;
using knkwebapi_v2.Enums;
using knkwebapi_v2.Services;
using knkwebapi_v2.Tests.Services.Production;
using Microsoft.EntityFrameworkCore;
using static knkwebapi_v2.Tests.Services.Production.ProductionEngineTestDb;

namespace knkwebapi_v2.Tests.Services.Shops;

/// <summary>
/// P10 follow-ups (PandiO/knk-workspace#938): the shop search row carries the kind and the shop type; the town account
/// ledger carries the player's username and joins a shop purchase or buy-back to its stock transaction.
/// </summary>
public class ShopFollowUpTests : IDisposable
{
    private const int Bread = ShopTestDb.Bread;
    private readonly ShopTestDb _s = new();

    public void Dispose() => _s.Dispose();

    [Fact]
    public async Task TheShopSearchRow_ShowsTheKindAndTheShopType()
    {
        await _s.SetUpAsync();

        var rows = (await _s.Shops.SearchAsync(new PagedQueryDto { PageSize = 50 })).Items.ToDictionary(r => r.id!.Value);

        Assert.Equal(("Processing", "Bakery"), (rows[_s.Bakery].kind, rows[_s.Bakery].shopTypeName));
        Assert.Equal(("Trading", "Weaponsmith"), (rows[_s.Weaponsmith].kind, rows[_s.Weaponsmith].shopTypeName));
        var bakeryType = _s.E.Db.Set<Shop>().AsNoTracking().Single(s => s.Id == _s.Bakery).ShopTypeId;
        Assert.Equal(bakeryType, rows[_s.Bakery].shopTypeId);

        var filtered = (await _s.Shops.SearchAsync(new PagedQueryDto { Filters = new() { ["kind"] = "Trading" } })).Items.Single();
        Assert.Equal((_s.Weaponsmith, "Trading"), (filtered.id!.Value, filtered.kind));
    }

    [Fact]
    public async Task AShopWithoutAType_HasNoTypeName_ButKeepsItsKind()
    {
        await _s.SetUpAsync();
        await _s.UpdateShopAsync(_s.Weaponsmith, d => d.ShopTypeId = 0);

        var row = (await _s.Shops.SearchAsync(new PagedQueryDto { PageSize = 50 })).Items.Single(r => r.id == _s.Weaponsmith);

        Assert.Equal(("Trading", (int?)null, (string?)null), (row.kind, row.shopTypeId, row.shopTypeName));
    }

    [Fact]
    public async Task TheTownLedger_ShowsTheUsername_AndLinksAPurchaseToItsStockTransaction()
    {
        await _s.SetUpAsync();
        await _s.Entry(_s.Bakery, Bread);
        await _s.Put(_s.Merchandise(_s.Bakery), Bread, 10);
        var bought = await _s.Trading.PurchaseAsync(_s.Bakery, ShopTestDb.Buy(_s.Ann, Bread, 2, 20, "join-buy"));

        var account = (await _s.Accounts.GetByTownAsync(ProductionWorldTestDb.Phoenix))!;
        var leg = (await _s.Accounts.LedgerAsync(account.Id, new PagedQueryDto())).Items.Single();

        Assert.Equal((CurrencyReasons.ShopPurchase, _s.Ann, "Ann"), (leg.ReasonCode, leg.UserId!.Value, leg.Username));
        Assert.Equal(bought.StockTransactionId, leg.StockTransactionId);
        Assert.Equal(bought.CurrencyTransactionId, leg.TransactionPublicId);
    }

    [Fact]
    public async Task TheTownLedger_LinksEveryLineOfASale_ToItsOwnStockTransaction()
    {
        await _s.SetUpAsync();
        await _s.FundTownAsync(500);
        var request = new ShopSellRequestDto
        {
            UserId = _s.Bob, IdempotencyKey = "join-sell",
            Lines = new() { new ShopSellLineDto { ItemBlueprintId = Bread, Amount = 4 }, new ShopSellLineDto { ItemBlueprintId = ShopTestDb.Flour, Amount = 2 } },
        };
        request.ExpectedTotal = (await _s.Trading.SellQuoteAsync(_s.Bakery, request)).Total;
        request.Confirmed = true;
        var sold = await _s.Trading.SellAsync(_s.Bakery, request);
        Assert.Equal(2, sold.StockTransactionIds.Count);

        var account = (await _s.Accounts.GetByTownAsync(ProductionWorldTestDb.Phoenix))!;
        var legs = (await _s.Accounts.LedgerAsync(account.Id, new PagedQueryDto())).Items;

        var buyBacks = legs.Where(l => l.ReasonCode == CurrencyReasons.ShopBuyBack).ToList();
        Assert.Equal(2, buyBacks.Count);
        Assert.All(buyBacks, l => Assert.Equal("Bob", l.Username));
        Assert.Equal(sold.StockTransactionIds.OrderBy(x => x), buyBacks.Select(l => l.StockTransactionId!.Value).OrderBy(x => x));
        foreach (var l in buyBacks)
        {
            var stock = _s.E.Db.StockTransactions.AsNoTracking().Single(t => t.Id == l.StockTransactionId);
            Assert.Equal(StockReason.ShopBuyBack, stock.Reason);
        }
        var funding = legs.Single(l => l.ReasonCode == CurrencyReasons.ShopPurchase);
        Assert.Equal("Bank", funding.Username);
        Assert.NotNull(funding.StockTransactionId);
    }

    [Fact]
    public async Task ALegWhoseUserIsGone_HasNoUsername()
    {
        await _s.SetUpAsync();
        await _s.Entry(_s.Bakery, Bread);
        await _s.Put(_s.Merchandise(_s.Bakery), Bread, 10);
        await _s.Trading.PurchaseAsync(_s.Bakery, ShopTestDb.Buy(_s.Ann, Bread, 1, 20, "gone"));
        _s.E.Db.ChangeTracker.Clear();
        _s.E.Db.Users.Remove(_s.E.Db.Users.Single(u => u.Id == _s.Ann));
        await _s.E.Db.SaveChangesAsync();

        var account = (await _s.Accounts.GetByTownAsync(ProductionWorldTestDb.Phoenix))!;
        var leg = (await _s.Accounts.LedgerAsync(account.Id, new PagedQueryDto())).Items.Single();

        Assert.Equal((_s.Ann, (string?)null), (leg.UserId!.Value, leg.Username));
    }
}
